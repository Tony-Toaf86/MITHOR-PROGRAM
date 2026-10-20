using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Mithor.Core.Util;
using Mithor.Platform.Windows.SystemIntegration;

namespace Mithor.App.Service
{
    /// <summary>Lanza MITHOR.exe --agent dentro de la sesión del usuario conectado.</summary>
    internal static class AgentLauncher
    {
        public static bool Launch(int sessionId, out string message)
        {
            message = string.Empty;
            string executable = AppInfo.ExecutablePath;
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                message = "no se encontró MITHOR.exe";
                return false;
            }

            if (IsSystemAccount())
            {
                if (LaunchAsSystem(sessionId, executable, out message))
                {
                    return true;
                }
                Log.Warn("CreateProcessAsUser falló: " + message);
            }

            // Modo interactivo (por ejemplo, --supervise ejecutado por el usuario).
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = executable;
                info.Arguments = "--agent";
                info.UseShellExecute = false;
                info.WorkingDirectory = Path.GetDirectoryName(executable);
                // Redirigir para no heredar la consola/pipe del proceso que lo lanza.
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                Process process = Process.Start(info);
                if (process == null)
                {
                    message = "Process.Start devolvió null";
                    return false;
                }
                process.Dispose();
                message = "agente iniciado (modo interactivo)";
                return true;
            }
            catch (Exception ex)
            {
                message = Log.Describe(ex);
                return false;
            }
        }

        private static bool LaunchAsSystem(int sessionId, string executable, out string message)
        {
            message = string.Empty;
            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environment = IntPtr.Zero;

            try
            {
                if (!ServiceNative.WTSQueryUserToken((uint)sessionId, out userToken))
                {
                    message = "WTSQueryUserToken: " + ServiceNative.LastError();
                    return false;
                }
                if (!ServiceNative.DuplicateTokenEx(userToken, ServiceNative.MAXIMUM_ALLOWED, IntPtr.Zero,
                    ServiceNative.SecurityImpersonation, ServiceNative.TokenPrimary, out primaryToken))
                {
                    message = "DuplicateTokenEx: " + ServiceNative.LastError();
                    return false;
                }
                ServiceNative.CreateEnvironmentBlock(out environment, primaryToken, false);

                ServiceNative.STARTUPINFO startup = new ServiceNative.STARTUPINFO();
                startup.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ServiceNative.STARTUPINFO));
                startup.lpDesktop = "winsta0\\default";

                string commandLine = "\"" + executable + "\" --agent";
                string workingDirectory = Path.GetDirectoryName(executable);
                ServiceNative.PROCESS_INFORMATION process;
                bool ok = ServiceNative.CreateProcessAsUser(
                    primaryToken, executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    ServiceNative.CREATE_UNICODE_ENVIRONMENT, environment, workingDirectory,
                    ref startup, out process);

                if (!ok)
                {
                    int firstError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    ok = ServiceNative.CreateProcessWithTokenW(
                        primaryToken, 0, executable, commandLine, ServiceNative.CREATE_UNICODE_ENVIRONMENT,
                        environment, workingDirectory, ref startup, out process);
                    if (!ok)
                    {
                        message = "CreateProcessAsUser/CreateProcessWithTokenW: " + ServiceNative.LastError()
                            + " (primer intento 0x" + firstError.ToString("X8") + ")";
                        return false;
                    }
                }

                ServiceNative.CloseHandle(process.hThread);
                ServiceNative.CloseHandle(process.hProcess);
                message = "agente iniciado en la sesión " + sessionId;
                return true;
            }
            finally
            {
                if (environment != IntPtr.Zero) { ServiceNative.DestroyEnvironmentBlock(environment); }
                if (primaryToken != IntPtr.Zero) { ServiceNative.CloseHandle(primaryToken); }
                if (userToken != IntPtr.Zero) { ServiceNative.CloseHandle(userToken); }
            }
        }

        private static bool IsSystemAccount()
        {
            try
            {
                return WindowsIdentity.GetCurrent().IsSystem;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Vigila la sesión activa y mantiene vivo el agente de MITHOR. Se ejecuta dentro
    /// del servicio (o de forma interactiva con --supervise).
    /// </summary>
    internal sealed class AgentSupervisor
    {
        private readonly object _gate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly System.Collections.Generic.Dictionary<int, DateTime> _lastAttempts =
            new System.Collections.Generic.Dictionary<int, DateTime>();
        private Thread _thread;
        private volatile bool _stop;

        public void Start()
        {
            lock (_gate)
            {
                if (_thread != null)
                {
                    return;
                }
                _stop = false;
                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Name = "MITHOR-supervisor";
                _thread.Start();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _stop = true;
                _wake.Set();
                Thread thread = _thread;
                _thread = null;
                if (thread != null && thread.IsAlive)
                {
                    try { thread.Join(4000); }
                    catch { }
                }
            }
        }

        public void Wake()
        {
            _wake.Set();
        }

        private void Loop()
        {
            while (!_stop)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log.Error("Supervisor: " + Log.Describe(ex));
                }
                _wake.WaitOne(4000);
            }
        }

        /// <summary>Comprueba todas las sesiones activas y arranca el agente donde falte.</summary>
        public bool Tick()
        {
            int[] sessions = ServiceNative.ActiveSessions();
            bool launched = false;
            for (int i = 0; i < sessions.Length; i++)
            {
                int sessionId = sessions[i];
                if (IsAgentRunning(sessionId))
                {
                    continue;
                }

                DateTime last;
                _lastAttempts.TryGetValue(sessionId, out last);
                if ((DateTime.UtcNow - last).TotalSeconds < 8)
                {
                    continue;
                }
                _lastAttempts[sessionId] = DateTime.UtcNow;

                string message;
                bool ok = AgentLauncher.Launch(sessionId, out message);
                if (ok)
                {
                    Log.Info("Sesión " + sessionId + ": " + message);
                    launched = true;
                }
                else
                {
                    Log.Warn("No se pudo iniciar el agente en la sesión " + sessionId + ": " + message);
                }
            }
            return launched;
        }

        public static int CurrentSessionId()
        {
            int[] sessions = ServiceNative.ActiveSessions();
            if (sessions.Length > 0)
            {
                return sessions[0];
            }
            uint console = ServiceNative.WTSGetActiveConsoleSessionId();
            return console == 0xFFFFFFFF ? 0 : (int)console;
        }

        public static bool IsAgentRunning(int sessionId)
        {
            Process[] processes = null;
            int currentProcessId = 0;
            try
            {
                currentProcessId = Process.GetCurrentProcess().Id;
            }
            catch
            {
            }
            try
            {
                processes = Process.GetProcessesByName("MITHOR");
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        // El propio proceso de la CLI/servicio también se llama MITHOR.
                        if (processes[i].Id == currentProcessId)
                        {
                            continue;
                        }
                        if (processes[i].SessionId == sessionId)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (processes != null)
                {
                    for (int i = 0; i < processes.Length; i++)
                    {
                        try { processes[i].Dispose(); }
                        catch { }
                    }
                }
            }
            return false;
        }

        public static int EnsureOnce()
        {
            Cli.ConsoleBridge.Attach();
            string logDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
            Log.Init(logDirectory, true);

            AgentSupervisor supervisor = new AgentSupervisor();
            int session = CurrentSessionId();
            Cli.ConsoleBridge.WriteLine("Sesión activa: " + session);
            bool running = IsAgentRunning(session);
            Cli.ConsoleBridge.WriteLine("Agente en ejecución: " + (running ? "sí" : "no"));
            if (!running)
            {
                supervisor.Tick();
                running = IsAgentRunning(session);
                Cli.ConsoleBridge.WriteLine("Tras el intento: " + (running ? "sí" : "no"));
            }
            return running ? 0 : 1;
        }

        public static int RunInteractive()
        {
            Cli.ConsoleBridge.Attach();
            string logDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
            Log.Init(logDirectory, true);
            Cli.ConsoleBridge.WriteLine("Supervisor de MITHOR en marcha (Ctrl+C para salir).");

            AgentSupervisor supervisor = new AgentSupervisor();
            ManualResetEvent stop = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                stop.Set();
            };
            supervisor.Start();
            stop.WaitOne();
            supervisor.Stop();
            return 0;
        }
    }
}
