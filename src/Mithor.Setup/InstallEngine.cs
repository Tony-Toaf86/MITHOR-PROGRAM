using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Mithor.Setup
{
    /// <summary>Instala, actualiza y desinstala MITHOR y su servicio de Windows.</summary>
    internal static class InstallEngine
    {
        public const string ServiceName = "MITHOR";
        public const string DisplayName = "MITHOR (control de brillo)";
        public const string Publisher = "MITHOR";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MITHOR";

        private const string LogFile = "mithor-setup.log";

        public static string DefaultDirectory
        {
            get
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(programFiles, "MITHOR");
            }
        }

        public static string Version
        {
            get
            {
                try
                {
                    Version version = Assembly.GetExecutingAssembly().GetName().Version;
                    return version.Major + "." + version.Minor + "." + version.Build;
                }
                catch
                {
                    return "1.1.0";
                }
            }
        }

        public static InstallResult Install(bool desktopShortcut, Action<string> progress)
        {
            string directory = DefaultDirectory;
            Log("=== Instalación de MITHOR " + Version + " en " + directory);
            try
            {
                Report(progress, "Deteniendo versiones anteriores…");
                ServiceInstaller.Remove();
                KillAgents();
                Thread.Sleep(500);

                Report(progress, "Copiando archivos en " + directory);
                Directory.CreateDirectory(directory);
                ExtractPayload(directory);

                string self = Process.GetCurrentProcess().MainModule.FileName;
                string targetSetup = Path.Combine(directory, "MITHOR-Setup.exe");
                if (!string.Equals(self, targetSetup, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(self, targetSetup, true);
                }
                File.WriteAllText(Path.Combine(directory, "version.txt"), Version + Environment.NewLine);

                Report(progress, "Registrando el servicio de Windows…");
                ServiceInstaller.Install(directory);

                Report(progress, "Creando accesos directos…");
                string executable = Path.Combine(directory, "MITHOR.exe");
                Shortcuts.Create(Shortcuts.StartMenuPath, executable, string.Empty,
                    "Control de brillo MITHOR", executable);
                if (desktopShortcut)
                {
                    Shortcuts.Create(Shortcuts.DesktopPath, executable, string.Empty,
                        "Control de brillo MITHOR", executable);
                }
                else
                {
                    Shortcuts.Remove(Shortcuts.DesktopPath);
                }

                Report(progress, "Registrando en Agregar o quitar programas…");
                WriteUninstallKey(directory, targetSetup);

                Report(progress, "Iniciando el servicio…");
                ServiceInstaller.Start();

                Log("Instalación completada.");
                return InstallResult.Ok(
                    "MITHOR " + Version + " se instaló correctamente.\n\n"
                    + "El servicio ya está activo: arrancará con Windows y mantendrá el icono "
                    + "de MITHOR en la bandeja del sistema. Al cerrar la ventana, MITHOR sigue "
                    + "funcionando en segundo plano.", directory);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
                return InstallResult.Fail(
                    "No se pudo completar la instalación.\n\n" + ex.Message
                    + "\n\nDetalle en: " + Path.Combine(Path.GetTempPath(), LogFile));
            }
        }

        public static InstallResult Uninstall(Action<string> progress)
        {
            string directory = DefaultDirectory;
            Log("=== Desinstalación de MITHOR");
            try
            {
                Report(progress, "Deteniendo el servicio…");
                ServiceInstaller.Remove();

                Report(progress, "Cerrando MITHOR…");
                KillAgents();
                Thread.Sleep(500);

                Report(progress, "Eliminando accesos directos…");
                Shortcuts.Remove(Shortcuts.StartMenuPath);
                Shortcuts.Remove(Shortcuts.DesktopPath);

                Report(progress, "Eliminando el registro de la aplicación…");
                try
                {
                    Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
                }
                catch (Exception ex)
                {
                    Log("No se pudo borrar la clave de desinstalación: " + ex.Message);
                }

                Report(progress, "Eliminando archivos…");
                DeleteInstallFiles(directory);

                Log("Desinstalación completada.");
                return InstallResult.Ok(
                    "MITHOR se desinstaló correctamente.\n\n"
                    + "Si algún archivo estaba en uso, Windows lo eliminará al reiniciar.", directory);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
                return InstallResult.Fail(
                    "No se pudo completar la desinstalación.\n\n" + ex.Message);
            }
        }

        public static void ExtractPayload(string directory)
        {
            Directory.CreateDirectory(directory);
            Assembly assembly = Assembly.GetExecutingAssembly();
            Extract(assembly, "payload.MITHOR.exe", Path.Combine(directory, "MITHOR.exe"));
            Extract(assembly, "payload.MITHOR.exe.config", Path.Combine(directory, "MITHOR.exe.config"));
        }

        private static void Extract(Assembly assembly, string resourceName, string destination)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Falta el recurso embebido " + resourceName + ".");
                }
                using (FileStream file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.CopyTo(file);
                }
            }
        }

        private static void WriteUninstallKey(string directory, string setupPath)
        {
            string executable = Path.Combine(directory, "MITHOR.exe");
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("No se pudo crear la clave de desinstalación.");
                }
                key.SetValue("DisplayName", "MITHOR — Control de brillo", RegistryValueKind.String);
                key.SetValue("DisplayVersion", Version, RegistryValueKind.String);
                key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                key.SetValue("InstallLocation", directory, RegistryValueKind.String);
                key.SetValue("DisplayIcon", executable + ",0", RegistryValueKind.String);
                key.SetValue("UninstallString", "\"" + setupPath + "\" --uninstall", RegistryValueKind.String);
                key.SetValue("QuietUninstallString", "\"" + setupPath + "\" --uninstall --quiet", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", EstimateSizeKb(directory), RegistryValueKind.DWord);
            }
        }

        private static int EstimateSizeKb(string directory)
        {
            try
            {
                long total = 0;
                foreach (string file in Directory.GetFiles(directory))
                {
                    total += new FileInfo(file).Length;
                }
                return (int)Math.Max(1, total / 1024);
            }
            catch
            {
                return 1;
            }
        }

        private static void DeleteInstallFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }
            string self = Process.GetCurrentProcess().MainModule.FileName;
            foreach (string file in Directory.GetFiles(directory))
            {
                if (string.Equals(file, self, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try { File.Delete(file); }
                catch (Exception ex) { Log("No se pudo borrar " + file + ": " + ex.Message); }
            }

            if (string.Equals(Path.GetDirectoryName(self), directory, StringComparison.OrdinalIgnoreCase))
            {
                // El desinstalador se está ejecutando desde la carpeta: se elimina al reiniciar.
                MoveFileEx(self, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                MoveFileEx(directory, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }

            try
            {
                if (Directory.GetFiles(directory).Length == 0)
                {
                    Directory.Delete(directory, false);
                }
            }
            catch
            {
            }
        }

        private static void KillAgents()
        {
            Process[] processes = Process.GetProcessesByName("MITHOR");
            int current = Process.GetCurrentProcess().Id;
            for (int i = 0; i < processes.Length; i++)
            {
                try
                {
                    if (processes[i].Id != current)
                    {
                        processes[i].Kill();
                    }
                }
                catch
                {
                }
                finally
                {
                    processes[i].Dispose();
                }
            }
        }

        public static void Report(Action<string> progress, string message)
        {
            Log(message);
            if (progress != null)
            {
                try { progress(message); }
                catch { }
            }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), LogFile),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFile, string newFile, uint flags);
    }
}
