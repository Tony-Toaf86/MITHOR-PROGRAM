using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Platform.Linux.Backends
{
    /// <summary>
    /// Brillo del panel interno en Linux mediante /sys/class/backlight (intel_backlight,
    /// amdgpu_bl0, acpi_video0...). Requiere permiso de escritura sobre el atributo
    /// "brightness" (regla udev o systemd-logind en la hoja de ruta).
    /// </summary>
    public sealed class SysfsBacklightBackend : IBrightnessBackend
    {
        private const string Root = "/sys/class/backlight";
        private readonly Dictionary<string, string> _devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Id { get { return "sysfs"; } }
        public string Label { get { return "Panel interno (sysfs)"; } }
        public string Description { get { return "Control del panel integrado mediante /sys/class/backlight."; } }
        public int Priority { get { return 10; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            string directory = FindDevice(display);
            if (string.IsNullOrEmpty(directory))
            {
                return BackendProbe.Unsupported("/sys/class/backlight no contiene dispositivos utilizables");
            }
            int max = ReadInt(Path.Combine(directory, "max_brightness"), 0);
            int current = ReadInt(Path.Combine(directory, "brightness"), 0);
            if (max <= 0)
            {
                return BackendProbe.Unsupported("max_brightness no válido");
            }
            _devices[display.Id] = directory;
            _last[display.Id] = ToPercent(current, max);
            return BackendProbe.SupportedRange(0, max, current, true,
                "sysfs · " + Path.GetFileName(directory));
        }

        public int? TryRead(DisplayInfo display)
        {
            string directory = FindDevice(display);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }
            int max = ReadInt(Path.Combine(directory, "max_brightness"), 0);
            int current = ReadInt(Path.Combine(directory, "brightness"), 0);
            if (max <= 0)
            {
                return null;
            }
            return ToPercent(current, max);
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            string directory = FindDevice(display);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }
            int max = ReadInt(Path.Combine(directory, "max_brightness"), 0);
            if (max <= 0)
            {
                return false;
            }
            int raw = (int)Math.Round(max * (BackendProbe.ClampPercent(percent) / 100.0));
            try
            {
                File.WriteAllText(Path.Combine(directory, "brightness"),
                    raw.ToString(CultureInfo.InvariantCulture));
                WriteBlPower(directory, 0);
                _last[display.Id] = percent;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("sysfs: no se pudo escribir brightness: " + Log.Describe(ex));
                return false;
            }
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
        }

        public void Shutdown()
        {
            _devices.Clear();
            _last.Clear();
        }

        private string FindDevice(DisplayInfo display)
        {
            string cached;
            if (_devices.TryGetValue(display.Id, out cached) && Directory.Exists(cached))
            {
                return cached;
            }
            try
            {
                if (!Directory.Exists(Root))
                {
                    return null;
                }
                string[] candidates = Directory.GetDirectories(Root);
                if (candidates.Length == 0)
                {
                    return null;
                }
                string best = null;
                for (int i = 0; i < candidates.Length; i++)
                {
                    string name = Path.GetFileName(candidates[i]);
                    if (display.IsInternal
                        && (name.IndexOf("edp", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("intel_backlight", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("amdgpu", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        best = candidates[i];
                        break;
                    }
                }
                if (best == null)
                {
                    best = candidates[0];
                }
                _devices[display.Id] = best;
                return best;
            }
            catch (Exception ex)
            {
                Log.Warn("sysfs: " + Log.Describe(ex));
                return null;
            }
        }

        private static void WriteBlPower(string directory, int value)
        {
            try
            {
                string path = Path.Combine(directory, "bl_power");
                if (File.Exists(path))
                {
                    File.WriteAllText(path, value.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch
            {
            }
        }

        private static int ReadInt(string path, int fallback)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return fallback;
                }
                string text = File.ReadAllText(path).Trim();
                int value;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    return value;
                }
            }
            catch
            {
            }
            return fallback;
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
