using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mithor.Core.Util
{
    /// <summary>Registro de eventos en archivo, seguro entre hilos y con rotación simple.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _directory;
        private static string _file;
        private static bool _verbose;
        private static long _maxBytes = 2L * 1024L * 1024L;

        public static string Directory { get { return _directory; } }
        public static string CurrentFile { get { return _file; } }
        public static bool Verbose { get { return _verbose; } }

        public static void Init(string directory, bool verbose)
        {
            lock (Gate)
            {
                try
                {
                    _directory = directory;
                    _verbose = verbose;
                    if (string.IsNullOrEmpty(directory))
                    {
                        return;
                    }
                    System.IO.Directory.CreateDirectory(directory);
                    _file = Path.Combine(directory, "mithor-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                }
                catch
                {
                    _directory = null;
                    _file = null;
                }
            }
        }

        public static void Debug(string message)
        {
            if (_verbose)
            {
                Write("DEBUG", message);
            }
        }

        public static void Info(string message)
        {
            Write("INFO ", message);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message + (exception == null ? string.Empty : " :: " + Describe(exception)));
        }

        /// <summary>Registra la excepción con su traza completa (para diagnóstico).</summary>
        public static void Exception(string message, Exception exception)
        {
            if (exception == null)
            {
                Write("ERROR", message);
                return;
            }
            Write("ERROR", message + Environment.NewLine + exception);
        }

        public static string Describe(Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder();
            Exception current = exception;
            int depth = 0;
            while (current != null && depth < 4)
            {
                if (depth > 0)
                {
                    sb.Append(" <- ");
                }
                sb.Append(current.GetType().Name).Append(": ").Append(current.Message);
                current = current.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        private static void Write(string level, string message)
        {
            string line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [T{2}] {3}",
                DateTime.Now, level, CurrentManagedThreadIdOrZero(), message);

            lock (Gate)
            {
                if (string.IsNullOrEmpty(_file))
                {
                    return;
                }
                try
                {
                    FileInfo info = new FileInfo(_file);
                    if (info.Exists && info.Length > _maxBytes)
                    {
                        Rotate();
                    }
                    File.AppendAllText(_file, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                    // Un fallo al registrar nunca debe tumbar la aplicación.
                }
            }
        }

        private static void Rotate()
        {
            try
            {
                string previous = _file + ".1";
                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }
                File.Move(_file, previous);
            }
            catch
            {
            }
        }

        internal static int CurrentManagedThreadIdOrZero()
        {
            try
            {
                return System.Threading.Thread.CurrentThread.ManagedThreadId;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Rutas de datos de la aplicación (portables a Linux).</summary>
    public static class AppPaths
    {
        public const string ProductName = "MITHOR";

        public static string DataDirectory { get; private set; }

        static AppPaths()
        {
            string overrideDirectory = Environment.GetEnvironmentVariable("MITHOR_DATA_DIR");
            if (!string.IsNullOrEmpty(overrideDirectory))
            {
                DataDirectory = overrideDirectory;
                return;
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataDirectory = FirstWritable(
                Path.Combine(local, ProductName),
                Path.Combine(Path.GetTempPath(), ProductName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datos"));
        }

        /// <summary>
        /// Elige la primera carpeta en la que realmente se puede escribir. En un equipo
        /// normal gana %LOCALAPPDATA%; en entornos restringidos se usa una carpeta
        /// temporal o una carpeta "datos" junto al ejecutable (modo portable).
        /// </summary>
        private static string FirstWritable(string first, string second, string third)
        {
            string[] candidates = new string[] { first, second, third };
            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }
                try
                {
                    Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".mithor-write-probe");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return candidate;
                }
                catch
                {
                }
            }
            return string.IsNullOrEmpty(third) ? first : third;
        }

        public static string SettingsFile { get { return Path.Combine(DataDirectory, "settings.json"); } }
        public static string LogDirectory { get { return Path.Combine(DataDirectory, "logs"); } }
        public static string DiagnosticsFile { get { return Path.Combine(DataDirectory, "diagnostico.txt"); } }

        public static void EnsureCreated()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                Directory.CreateDirectory(LogDirectory);
            }
            catch
            {
            }
        }

        public static void OpenInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch
            {
            }
        }
    }
}
