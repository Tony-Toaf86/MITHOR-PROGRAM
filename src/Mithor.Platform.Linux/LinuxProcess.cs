using System;
using System.Diagnostics;
using System.Globalization;

namespace Mithor.Platform.Linux
{
    /// <summary>Ejecución de utilidades del sistema (xrandr, ddcutil) con tiempo límite.</summary>
    internal static class LinuxProcess
    {
        public static bool Exists(string command)
        {
            try
            {
                return !string.IsNullOrEmpty(Run("/bin/sh", "-c \"command -v " + command + "\"", 2500));
            }
            catch
            {
                return false;
            }
        }

        public static string Run(string fileName, string arguments, int timeoutMilliseconds)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return null;
                    }
                    string output = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try { process.Kill(); }
                        catch { }
                        return null;
                    }
                    return output;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool RunOk(string fileName, string arguments, int timeoutMilliseconds)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return false;
                    }
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try { process.Kill(); }
                        catch { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static int ParseInt(string text, int fallback)
        {
            int value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return fallback;
        }
    }
}
