using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace Mithor.Setup
{
    internal static class SetupProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool uninstall = HasArgument(args, "--uninstall");
            bool quiet = HasArgument(args, "--quiet");
            bool noDesktop = HasArgument(args, "--nodesktop");
            string extractDirectory = GetArgumentValue(args, "--extract");

            // Modo de extracción: no requiere administrador y sirve para validar el
            // contenido embebido del instalador (también permite instalaciones manuales).
            if (!string.IsNullOrEmpty(extractDirectory))
            {
                try
                {
                    InstallEngine.ExtractPayload(extractDirectory);
                    Console.WriteLine("Contenido extraído en " + extractDirectory);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("No se pudo extraer: " + ex.Message);
                    return 1;
                }
            }

            if (!IsAdministrator())
            {
                if (HasArgument(args, "--elevated"))
                {
                    MessageBox.Show(
                        "El instalador necesita permisos de administrador para registrar el servicio de Windows.",
                        "MITHOR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
                }
                try
                {
                    RelaunchElevated(args);
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "No se pudo solicitar la elevación de permisos.\n\n" + ex.Message
                        + "\n\nHaz clic derecho en MITHOR-Setup.exe y elige «Ejecutar como administrador».",
                        "MITHOR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
            }

            if (quiet)
            {
                InstallResult result = uninstall
                    ? InstallEngine.Uninstall(delegate(string message) { })
                    : InstallEngine.Install(!noDesktop, delegate(string message) { });
                return result.Success ? 0 : 1;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(uninstall, noDesktop));
            return 0;
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void RelaunchElevated(string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Application.ExecutablePath;
            info.Arguments = JoinArguments(args) + " --elevated";
            info.UseShellExecute = true;
            info.Verb = "runas";
            Process.Start(info);
        }

        private static string JoinArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }
            string result = string.Empty;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--elevated")
                {
                    continue;
                }
                if (result.Length > 0)
                {
                    result += " ";
                }
                result += args[i].IndexOf(' ') >= 0 ? "\"" + args[i] + "\"" : args[i];
            }
            return result;
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
    }

    internal sealed class InstallResult
    {
        public bool Success;
        public string Message;
        public string Directory;

        public static InstallResult Ok(string message, string directory)
        {
            InstallResult result = new InstallResult();
            result.Success = true;
            result.Message = message;
            result.Directory = directory;
            return result;
        }

        public static InstallResult Fail(string message)
        {
            InstallResult result = new InstallResult();
            result.Success = false;
            result.Message = message;
            return result;
        }
    }
}
