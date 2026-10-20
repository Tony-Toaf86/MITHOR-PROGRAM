using System;
using System.Collections.Generic;
using System.Globalization;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Platform.Linux.Backends
{
    /// <summary>
    /// Brillo de monitores externos en Linux mediante la utilidad ddcutil (DDC/CI).
    /// Es el equivalente multiplataforma del backend DDC/CI de Windows.
    /// </summary>
    public sealed class DdcutilBackend : IBrightnessBackend
    {
        private readonly Dictionary<string, int> _displayNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _maxValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Id { get { return "ddcutil"; } }
        public string Label { get { return "DDC/CI (ddcutil)"; } }
        public string Description { get { return "Control del monitor por DDC/CI usando la utilidad ddcutil."; } }
        public int Priority { get { return 20; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            if (!LinuxProcess.Exists("ddcutil"))
            {
                return BackendProbe.Unsupported("ddcutil no está instalado (sudo apt install ddcutil)");
            }

            List<int> displays = DetectDisplays();
            if (displays.Count == 0)
            {
                return BackendProbe.Unsupported("ddcutil no detectó monitores (revisa permisos de /dev/i2c-*)");
            }

            int number = displays[0];
            int index = display.MonitorIndex - 1;
            if (index >= 0 && index < displays.Count)
            {
                number = displays[index];
            }

            int current;
            int max;
            if (!TryGetVcp(number, out current, out max))
            {
                return BackendProbe.Unsupported("el monitor no respondió a getvcp 10");
            }
            if (max <= 0)
            {
                max = 100;
            }

            _displayNumbers[display.Id] = number;
            _maxValues[display.Id] = max;
            _last[display.Id] = ToPercent(current, max);
            return BackendProbe.SupportedRange(0, max, current, true, "ddcutil · pantalla " + number);
        }

        public int? TryRead(DisplayInfo display)
        {
            int number;
            if (!_displayNumbers.TryGetValue(display.Id, out number))
            {
                return null;
            }
            int current;
            int max;
            if (!TryGetVcp(number, out current, out max))
            {
                return null;
            }
            return ToPercent(current, max);
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            int number;
            if (!_displayNumbers.TryGetValue(display.Id, out number))
            {
                return false;
            }
            int max;
            if (!_maxValues.TryGetValue(display.Id, out max) || max <= 0)
            {
                max = 100;
            }
            int raw = (int)Math.Round(max * (BackendProbe.ClampPercent(percent) / 100.0));
            bool ok = LinuxProcess.RunOk("ddcutil",
                "--display " + number.ToString(CultureInfo.InvariantCulture)
                + " setvcp 10 " + raw.ToString(CultureInfo.InvariantCulture), 8000);
            if (ok)
            {
                _last[display.Id] = percent;
            }
            else
            {
                Log.Warn("ddcutil: setvcp 10 falló en " + display.FriendlyName);
            }
            return ok;
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
            _displayNumbers.Clear();
            _maxValues.Clear();
        }

        public void Shutdown()
        {
            OnDisplaysChanged(null);
        }

        private static List<int> DetectDisplays()
        {
            List<int> numbers = new List<int>();
            string output = LinuxProcess.Run("ddcutil", "detect --brief", 6000);
            if (string.IsNullOrEmpty(output))
            {
                return numbers;
            }
            string[] lines = output.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("Display ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int space = line.IndexOf(' ', 8);
                string numberText = space > 0 ? line.Substring(8, space - 8) : line.Substring(8);
                int number;
                if (int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    numbers.Add(number);
                }
            }
            numbers.Sort();
            return numbers;
        }

        private static bool TryGetVcp(int number, out int current, out int max)
        {
            current = 0;
            max = 0;
            string output = LinuxProcess.Run("ddcutil",
                "--display " + number.ToString(CultureInfo.InvariantCulture) + " getvcp 10 --brief", 8000);
            if (string.IsNullOrEmpty(output))
            {
                return false;
            }

            // Formato esperado: "VCP 10 C 50 100"
            string[] tokens = output.Replace("\r", string.Empty)
                .Replace("\n", " ")
                .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length - 3; i++)
            {
                if (!string.Equals(tokens[i], "VCP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!string.Equals(tokens[i + 1], "10", StringComparison.Ordinal))
                {
                    continue;
                }
                int parsedCurrent = LinuxProcess.ParseInt(tokens[i + 3], -1);
                int parsedMax = LinuxProcess.ParseInt(tokens[i + 4 < tokens.Length ? i + 4 : i + 3], -1);
                if (parsedCurrent < 0)
                {
                    return false;
                }
                current = parsedCurrent;
                max = parsedMax > 0 ? parsedMax : 100;
                return true;
            }
            return false;
        }

        private static int ToPercent(int raw, int max)
        {
            if (max <= 0)
            {
                return 0;
            }
            return BackendProbe.ClampPercent((int)Math.Round(raw * 100.0 / max));
        }
    }
}
