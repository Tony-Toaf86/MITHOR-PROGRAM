using System;
using System.IO;
using System.Text;
using Mithor.Platform.Windows.Native;

namespace Mithor.App.Cli
{
    /// <summary>
    /// Conecta una aplicación de subsistema gráfico con la consola del proceso padre
    /// para que la CLI (--list, --set, ...) escriba en la misma ventana.
    /// </summary>
    internal static class ConsoleBridge
    {
        private static bool _attached;

        public static void Attach()
        {
            if (_attached)
            {
                return;
            }
            _attached = true;

            try
            {
                if (Win32.GetConsoleWindow() == IntPtr.Zero)
                {
                    Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
                }
            }
            catch
            {
            }

            try
            {
                if (Console.IsOutputRedirected)
                {
                    Stream stream = Console.OpenStandardOutput();
                    Console.SetOut(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
                }
            }
            catch
            {
            }
        }

        public static void WriteLine(string text)
        {
            try { Console.WriteLine(text); }
            catch { }
        }

        public static void WriteError(string text)
        {
            try { Console.Error.WriteLine(text); }
            catch { }
        }
    }
}
