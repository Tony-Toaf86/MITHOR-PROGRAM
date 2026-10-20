using System;
using System.Collections.Generic;
using System.Threading;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;
using Mithor.Platform.Windows.Native;

namespace Mithor.Platform.Windows.Backends
{
    /// <summary>
    /// Brillo por DDC/CI (VCP 0x10). Es el método que usan todos los monitores externos
    /// modernos, independientemente del fabricante o de la GPU, porque va por el cable
    /// de vídeo (HDMI, DisplayPort, DVI, VGA, USB-C). También funciona en algunos
    /// paneles integrados que exponen MCCS sobre eDP.
    /// </summary>
    public sealed class DdcCiBackend : IBrightnessBackend
    {
        private sealed class Session
        {
            public IntPtr Physical;
            public Dxva2.PHYSICAL_MONITOR[] Handles;
            public string Description;
            public uint Max;
            public uint Min;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        private string _lastError = string.Empty;

        public string Id { get { return "ddc"; } }
        public string Label { get { return "DDC/CI"; } }
        public string Description { get { return "Control directo del monitor por el canal DDC/CI (VCP 0x10)."; } }
        public int Priority { get { return 20; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            Session session = Open(display);
            if (session == null)
            {
                return BackendProbe.Unsupported(string.IsNullOrEmpty(_lastError) ? "DDC/CI no disponible" : _lastError);
            }

            uint current;
            uint max;
            if (TryGetVcp(session, Dxva2.VCP_BRIGHTNESS, out current, out max))
            {
                if (max == 0)
                {
                    max = 100;
                }
                if (current > max)
                {
                    current = max;
                }
                session.Max = max;
                return BackendProbe.SupportedRange(0, (int)max, (int)current, true,
                    "VCP 0x10 · " + SafeDescription(session.Description));
            }

            // El monitor puede aceptar escritura aunque no responda a la lectura.
            uint capabilities;
            uint colorTemperatures;
            if (Dxva2.GetMonitorCapabilities(session.Physical, out capabilities, out colorTemperatures)
                && (capabilities & Dxva2.MC_CAPS_BRIGHTNESS) != 0)
            {
                session.Max = 100;
                return BackendProbe.SupportedRange(0, 100, 100, false, "MCCS sin lectura");
            }

            return BackendProbe.Unsupported("el monitor no responde a DDC/CI (¿desactivado en el OSD?)");
        }

        public int? TryRead(DisplayInfo display)
        {
            Session session = Open(display);
            if (session == null)
            {
                return null;
            }
            uint current;
            uint max;
            if (!TryGetVcp(session, Dxva2.VCP_BRIGHTNESS, out current, out max))
            {
                return null;
            }
            if (max == 0)
            {
                max = 100;
            }
            session.Max = max;
            BackendProbe probe = BackendProbe.SupportedRange(0, (int)max, (int)current, true, string.Empty);
            return probe.ToPercent((int)current);
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            if (TryWriteOnce(display, percent))
            {
                return true;
            }

            // Reabrir una vez: el handle puede haberse invalidado tras un cambio de modo.
            CloseSession(display.Id);
            return TryWriteOnce(display, percent);
        }

        private bool TryWriteOnce(DisplayInfo display, int percent)
        {
            Session session = Open(display);
            if (session == null)
            {
                return false;
            }

            uint max = session.Max;
            if (max == 0)
            {
                uint current;
                uint reportedMax;
                if (TryGetVcp(session, Dxva2.VCP_BRIGHTNESS, out current, out reportedMax) && reportedMax > 0)
                {
                    max = reportedMax;
                    session.Max = reportedMax;
                }
                else
                {
                    max = 100;
                }
            }

            uint raw = (uint)Math.Round((max - session.Min) * (BackendProbe.ClampPercent(percent) / 100.0)) + session.Min;
            if (Dxva2.SetVCPFeature(session.Physical, Dxva2.VCP_BRIGHTNESS, raw))
            {
                return true;
            }

            _lastError = "SetVCPFeature falló: " + Win32.LastErrorText();
            Log.Warn("DDC/CI: " + _lastError + " en " + display.FriendlyName);
            return false;
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
            // Las direcciones físicas cambian al desconectar/conectar: se reabren.
            CloseAll();
        }

        public void Shutdown()
        {
            CloseAll();
        }

        private Session Open(DisplayInfo display)
        {
            lock (_gate)
            {
                Session existing;
                if (_sessions.TryGetValue(display.Id, out existing))
                {
                    return existing;
                }
                return OpenLocked(display);
            }
        }

        private Session OpenLocked(DisplayInfo display)
        {
            IntPtr hMonitor = display.Handle;
            if (hMonitor == IntPtr.Zero)
            {
                hMonitor = Win32.MonitorFromPoint(
                    new Win32.POINT { X = display.Bounds.CenterX, Y = display.Bounds.CenterY },
                    Win32.MONITOR_DEFAULTTONEAREST);
            }

            uint count;
            if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0)
            {
                _lastError = "el monitor no expone monitores físicos (" + Win32.LastErrorText() + ")";
                return null;
            }

            Dxva2.PHYSICAL_MONITOR[] monitors = new Dxva2.PHYSICAL_MONITOR[count];
            if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
            {
                _lastError = "GetPhysicalMonitorsFromHMONITOR falló (" + Win32.LastErrorText() + ")";
                return null;
            }

            int index = ChooseMonitor(monitors, display);
            Session session = new Session();
            session.Handles = monitors;
            session.Physical = monitors[index].hPhysicalMonitor;
            session.Description = monitors[index].szPhysicalMonitorDescription;
            session.Max = 0;
            session.Min = 0;
            _sessions[display.Id] = session;
            Log.Debug("DDC/CI abierto: " + display.FriendlyName + " -> " + session.Description);
            return session;
        }

        private static int ChooseMonitor(Dxva2.PHYSICAL_MONITOR[] monitors, DisplayInfo display)
        {
            if (monitors.Length <= 1)
            {
                return 0;
            }
            string friendly = display.FriendlyName == null ? string.Empty : display.FriendlyName;
            for (int i = 0; i < monitors.Length; i++)
            {
                string description = monitors[i].szPhysicalMonitorDescription;
                if (string.IsNullOrEmpty(description))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(friendly)
                    && description.IndexOf(friendly, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }
            int candidate = display.MonitorIndex - 1;
            if (candidate >= 0 && candidate < monitors.Length)
            {
                return candidate;
            }
            return 0;
        }

        private static bool TryGetVcp(Session session, byte code, out uint current, out uint max)
        {
            current = 0;
            max = 0;
            uint type;
            try
            {
                if (!Dxva2.GetVCPFeatureAndVCPFeatureReply(session.Physical, code, out type, out current, out max))
                {
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void CloseSession(string displayId)
        {
            lock (_gate)
            {
                Session session;
                if (!_sessions.TryGetValue(displayId, out session))
                {
                    return;
                }
                _sessions.Remove(displayId);
                ReleaseSession(session);
            }
        }

        private void CloseAll()
        {
            lock (_gate)
            {
                foreach (KeyValuePair<string, Session> pair in _sessions)
                {
                    ReleaseSession(pair.Value);
                }
                _sessions.Clear();
            }
        }

        private static void ReleaseSession(Session session)
        {
            if (session == null || session.Handles == null || session.Handles.Length == 0)
            {
                return;
            }
            try
            {
                Dxva2.DestroyPhysicalMonitors((uint)session.Handles.Length, session.Handles);
            }
            catch
            {
            }
            session.Handles = null;
            session.Physical = IntPtr.Zero;
        }

        private static string SafeDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return "monitor";
            }
            return description.Trim();
        }
    }
}
