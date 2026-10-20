using System;
using System.Collections.Generic;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;
using Mithor.Platform.Windows.Native;

namespace Mithor.Platform.Windows.Backends
{
    /// <summary>
    /// Brillo por software mediante la rampa de gamma de la GPU (SetDeviceGammaRamp).
    /// Funciona en cualquier salida de vídeo, incluso con adaptadores virtuales, pero
    /// el sistema lo ignora cuando la pantalla está en HDR y puede alterar el color.
    /// </summary>
    public sealed class GammaRampBackend : IBrightnessBackend
    {
        private const double MinimumFactor = 0.035;

        private readonly object _gate = new object();
        private readonly Dictionary<string, ushort[]> _originals = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lastValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Id { get { return "gamma"; } }
        public string Label { get { return "Gamma (software)"; } }
        public string Description { get { return "Atenuación por software ajustando la rampa de gamma de la GPU."; } }
        public int Priority { get { return 60; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            IntPtr dc = OpenDevice(display);
            if (dc == IntPtr.Zero)
            {
                return BackendProbe.Unsupported("no se pudo abrir la pantalla");
            }
            try
            {
                ushort[] ramp = new ushort[3 * 256];
                if (!Win32.GetDeviceGammaRamp(dc, ramp))
                {
                    return BackendProbe.Unsupported("el controlador no admite rampas de gamma");
                }
                return BackendProbe.SupportedRange(0, 100, 100, false,
                    display.IsHdrActive ? "HDR activo: el sistema puede ignorar el gamma" : "rampa de gamma");
            }
            finally
            {
                Win32.DeleteDC(dc);
            }
        }

        public int? TryRead(DisplayInfo display)
        {
            string key = KeyOf(display);
            lock (_gate)
            {
                int value;
                if (_lastValues.TryGetValue(key, out value))
                {
                    return value;
                }
            }
            return null;
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            percent = BackendProbe.ClampPercent(percent);
            IntPtr dc = OpenDevice(display);
            if (dc == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                string key = KeyOf(display);
                SaveOriginal(key, dc);

                double factor = Math.Max(MinimumFactor, percent / 100.0);
                ushort[] ramp = BuildRamp(factor);
                if (!Win32.SetDeviceGammaRamp(dc, ramp))
                {
                    Log.Warn("SetDeviceGammaRamp falló en " + display.FriendlyName + ": " + Win32.LastErrorText());
                    return false;
                }
                lock (_gate)
                {
                    _lastValues[key] = percent;
                }
                return true;
            }
            finally
            {
                Win32.DeleteDC(dc);
            }
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
            // Un cambio de modo o de resolución reinicia la rampa: se vuelve a aplicar.
            if (displays == null)
            {
                return;
            }
            for (int i = 0; i < displays.Count; i++)
            {
                DisplayInfo display = displays[i];
                if (display == null)
                {
                    continue;
                }
                int value;
                lock (_gate)
                {
                    if (!_lastValues.TryGetValue(KeyOf(display), out value))
                    {
                        continue;
                    }
                }
                TryWrite(display, value);
            }
        }

        public void Shutdown()
        {
            lock (_gate)
            {
                foreach (KeyValuePair<string, ushort[]> pair in _originals)
                {
                    IntPtr dc = OpenDeviceByKey(pair.Key);
                    if (dc == IntPtr.Zero)
                    {
                        continue;
                    }
                    try
                    {
                        Win32.SetDeviceGammaRamp(dc, pair.Value);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Win32.DeleteDC(dc);
                    }
                }
                _originals.Clear();
                _lastValues.Clear();
            }
        }

        private void SaveOriginal(string key, IntPtr dc)
        {
            lock (_gate)
            {
                if (_originals.ContainsKey(key))
                {
                    return;
                }
            }
            ushort[] ramp = new ushort[3 * 256];
            if (Win32.GetDeviceGammaRamp(dc, ramp))
            {
                lock (_gate)
                {
                    _originals[key] = ramp;
                }
            }
        }

        private static ushort[] BuildRamp(double factor)
        {
            ushort[] ramp = new ushort[3 * 256];
            for (int channel = 0; channel < 3; channel++)
            {
                int offset = channel * 256;
                for (int i = 0; i < 256; i++)
                {
                    double value = i * 257.0 * factor;
                    if (value < 0) { value = 0; }
                    if (value > 65535) { value = 65535; }
                    ramp[offset + i] = (ushort)Math.Round(value);
                }
            }
            return ramp;
        }

        private static string KeyOf(DisplayInfo display)
        {
            return string.IsNullOrEmpty(display.DeviceName) ? display.Id : display.DeviceName;
        }

        private static IntPtr OpenDevice(DisplayInfo display)
        {
            string deviceName = display.DeviceName;
            if (string.IsNullOrEmpty(deviceName))
            {
                return IntPtr.Zero;
            }
            try
            {
                return Win32.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static IntPtr OpenDeviceByKey(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return IntPtr.Zero;
            }
            try
            {
                return Win32.CreateDC("DISPLAY", deviceName, null, IntPtr.Zero);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
