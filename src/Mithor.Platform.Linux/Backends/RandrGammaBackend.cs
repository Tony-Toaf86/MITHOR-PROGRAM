using System;
using System.Collections.Generic;
using System.Globalization;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Platform.Linux.Backends
{
    /// <summary>
    /// Brillo por software en X11 con "xrandr --output ... --brightness". Es el
    /// equivalente a la rampa de gamma de Windows. En Wayland se sustituirá por
    /// wlr-randr / gammastep o el protocolo del compositor.
    /// </summary>
    public sealed class RandrGammaBackend : IBrightnessBackend
    {
        private readonly Dictionary<string, int> _last = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Id { get { return "randr"; } }
        public string Label { get { return "Gamma (RandR)"; } }
        public string Description { get { return "Atenuación por software con xrandr --brightness (X11)."; } }
        public int Priority { get { return 60; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            if (string.IsNullOrEmpty(display.DeviceName))
            {
                return BackendProbe.Unsupported("sin nombre de salida");
            }
            string output = LinuxProcess.Run("xrandr", "--query", 3000);
            if (string.IsNullOrEmpty(output))
            {
                return BackendProbe.Unsupported("xrandr no disponible (¿Wayland sin XWayland?)");
            }
            string[] lines = output.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith(display.DeviceName + " connected", StringComparison.OrdinalIgnoreCase))
                {
                    return BackendProbe.SupportedRange(0, 100, 100, false, "xrandr --brightness");
                }
            }
            return BackendProbe.Unsupported("la salida no aparece conectada en xrandr");
        }

        public int? TryRead(DisplayInfo display)
        {
            int value;
            if (_last.TryGetValue(display.Id, out value))
            {
                return value;
            }
            return null;
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            int clamped = BackendProbe.ClampPercent(percent);
            double factor = 0.05 + (clamped / 100.0) * 0.95;
            bool ok = LinuxProcess.RunOk("xrandr",
                "--output " + display.DeviceName
                + " --brightness " + factor.ToString("0.00", CultureInfo.InvariantCulture), 4000);
            if (ok)
            {
                _last[display.Id] = clamped;
            }
            return ok;
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
            if (displays == null)
            {
                return;
            }
            for (int i = 0; i < displays.Count; i++)
            {
                int value;
                if (_last.TryGetValue(displays[i].Id, out value))
                {
                    TryWrite(displays[i], value);
                }
            }
        }

        public void Shutdown()
        {
            foreach (KeyValuePair<string, int> pair in _last)
            {
                LinuxProcess.RunOk("xrandr", "--output " + pair.Key + " --brightness 1.00", 3000);
            }
            _last.Clear();
        }
    }
}
