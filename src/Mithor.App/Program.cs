using System;
using System.Windows.Forms;
using Mithor.App.Cli;
using Mithor.App.Service;
using Mithor.App.Ui;
using Mithor.Core.Config;
using Mithor.Core.Services;
using Mithor.Core.Util;
using Mithor.Platform.Windows;
using Mithor.Platform.Windows.Displays;
using Mithor.Platform.Windows.SystemIntegration;

namespace Mithor.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Modo servicio: lo arranca el Administrador de servicios al iniciar Windows.
            if (HasArgument(args, "--service"))
            {
                return MithorService.Run();
            }
            if (HasArgument(args, "--ensure-agent"))
            {
                return AgentSupervisor.EnsureOnce();
            }
            if (HasArgument(args, "--supervise"))
            {
                return AgentSupervisor.RunInteractive();
            }
            if (CliRunner.IsCliInvocation(args))
            {
                return CliRunner.Run(args);
            }
            return RunGui(args);
        }

        private static int RunGui(string[] args)
        {
            bool startMinimized = HasArgument(args, "--tray")
                || HasArgument(args, "--minimized")
                || HasArgument(args, "--agent");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            DpiAwareness.EnablePerMonitor();

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.ThreadException += OnThreadException;

            AppPaths.EnsureCreated();
            FileSettingsStore store = new FileSettingsStore(AppPaths.SettingsFile);
            AppSettings settings = AppSettings.Load(store);
            string forcedBackend = GetArgumentValue(args, "--backend");
            if (!string.IsNullOrEmpty(forcedBackend))
            {
                settings.DefaultBackend = forcedBackend;
            }
            Log.Init(AppPaths.LogDirectory, settings.VerboseLog);
            Log.Info("MITHOR " + AppInfo.Version + " · Windows " + Environment.OSVersion.Version
                + " · " + (Environment.Is64BitProcess ? "x64" : "x86"));

            SingleInstance singleInstance = SingleInstance.TryAcquire();
            if (singleInstance == null)
            {
                Log.Info("Ya hay una instancia en ejecución; se solicita mostrarla.");
                return 0;
            }

            BrightnessController controller = null;
            try
            {
                WindowsBackendProvider provider = new WindowsBackendProvider();
                WindowsDisplayEnumerator enumerator = new WindowsDisplayEnumerator();
                controller = new BrightnessController(provider, enumerator, settings, store, provider.Overlay);
                provider.Overlay.SetMaxOpacity(settings.OverlayMaxOpacity);

                MainForm form = new MainForm(controller, provider, settings, store, singleInstance,
                    startMinimized || settings.StartMinimized);
                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("Error fatal", ex);
                try
                {
                    MessageBox.Show(
                        "MITHOR encontró un error inesperado y debe cerrarse.\n\n"
                        + ex.Message + "\n\nRegistro: " + (Log.CurrentFile ?? "no disponible"),
                        "MITHOR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                }
                return 1;
            }
            finally
            {
                if (controller != null)
                {
                    controller.Shutdown();
                }
                singleInstance.Dispose();
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            if (args == null)
            {
                return false;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetArgumentValue(string[] args, string name)
        {
            if (args == null)
            {
                return null;
            }
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Log.Exception("Excepción en la interfaz", e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Exception("Excepción no controlada", e.ExceptionObject as Exception);
        }
    }
}

