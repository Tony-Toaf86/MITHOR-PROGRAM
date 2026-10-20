using System;
using System.IO;
using System.Text;
using Mithor.Core.Abstractions;

namespace Mithor.Core.Config
{
    /// <summary>
    /// Configuración en disco como JSON, con escritura atómica y copia de seguridad.
    /// El archivo puede contener comentarios de línea que se ignoran al leer.
    /// </summary>
    public sealed class FileSettingsStore : ISettingsStore
    {
        private readonly string _path;

        public FileSettingsStore(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }
            _path = path;
        }

        public string FilePath { get { return _path; } }

        public string Describe()
        {
            return _path;
        }

        public string Read()
        {
            if (!File.Exists(_path))
            {
                return null;
            }
            string text = File.ReadAllText(_path, Encoding.UTF8);
            return StripComments(text);
        }

        public void Write(string text)
        {
            string directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = _path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));

            if (File.Exists(_path))
            {
                string backup = _path + ".bak";
                try
                {
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                    File.Move(_path, backup);
                }
                catch
                {
                }
            }
            File.Move(temp, _path);
        }

        private static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            StringBuilder sb = new StringBuilder(text.Length);
            bool inString = false;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                    sb.Append(c);
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                    if (i < text.Length)
                    {
                        sb.Append('\n');
                    }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
