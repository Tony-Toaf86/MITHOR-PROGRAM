using System;
using System.Reflection;
using Microsoft.Win32;
using Mithor.Core.Util;

namespace Mithor.Platform.Windows.SystemIntegration
{
    /// <summary>Alta/baja de MITHOR en el arranque de Windows (HKCU\...\Run).</summary>
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MITHOR";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    return key.GetValue(ValueName) != null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo leer el arranque automático: " + Log.Describe(ex));
                return false;
            }
        }

        public static bool SetEnabled(bool enabled, string executablePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    if (enabled)
                    {
                        string command = "\"" + executablePath + "\" --tray";
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo cambiar el arranque automático: " + Log.Describe(ex));
                return false;
            }
        }
    }

    /// <summary>Metadatos de la aplicación.</summary>
    public static class AppInfo
    {
        public static string Version
        {
            get
            {
                try
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    object[] attributes = assembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
                    if (attributes != null && attributes.Length > 0)
                    {
                        return ((AssemblyFileVersionAttribute)attributes[0]).Version;
                    }
                    return assembly.GetName().Version.ToString();
                }
                catch
                {
                    return "1.0.0";
                }
            }
        }

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MITHOR.exe");
                }
            }
        }
    }
}
