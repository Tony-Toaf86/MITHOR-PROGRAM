using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Platform.Windows.Backends;
using Mithor.Platform.Windows.Native;

namespace Mithor.Platform.Windows
{
    /// <summary>
    /// Registro de métodos de control de Windows y su orden de preferencia según el
    /// tipo de pantalla. Este es el punto que se sustituye en Linux por
    /// LinuxBackendProvider sin tocar el núcleo.
    /// </summary>
    public sealed class WindowsBackendProvider : IBackendProvider
    {
        private readonly List<IBrightnessBackend> _all = new List<IBrightnessBackend>();
        private readonly DdcCiBackend _ddc = new DdcCiBackend();
        private readonly WmiBackend _wmi = new WmiBackend();
        private readonly GammaRampBackend _gamma = new GammaRampBackend();
        private readonly OverlayBackend _overlay = new OverlayBackend();

        public WindowsBackendProvider()
        {
            _all.Add(_wmi);
            _all.Add(_ddc);
            _all.Add(_gamma);
            _all.Add(_overlay);
        }

        public IList<IBrightnessBackend> All { get { return _all; } }

        public OverlayBackend Overlay { get { return _overlay; } }

        public IList<IBrightnessBackend> GetCandidates(DisplayInfo display)
        {
            List<IBrightnessBackend> candidates = new List<IBrightnessBackend>();
            bool virtualDisplay = display != null && display.Kind == MonitorKind.Virtual;
            bool internalPanel = display != null && display.Kind == MonitorKind.Internal;
            bool hdr = display != null && display.IsHdrActive;

            if (virtualDisplay)
            {
                // En escritorio remoto / VM el gamma suele aceptarse pero no aplicarse.
                Add(candidates, _ddc);
                Add(candidates, _wmi);
                Add(candidates, _overlay);
                Add(candidates, _gamma);
                return candidates;
            }

            if (internalPanel)
            {
                Add(candidates, _wmi);
                Add(candidates, _ddc);
            }
            else
            {
                Add(candidates, _ddc);
                Add(candidates, _wmi);
            }

            if (hdr)
            {
                // SetDeviceGammaRamp se ignora en HDR: la superposición es más fiable.
                Add(candidates, _overlay);
                Add(candidates, _gamma);
            }
            else
            {
                Add(candidates, _gamma);
                Add(candidates, _overlay);
            }
            return candidates;
        }

        public IBrightnessBackend Get(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return _all[i];
                }
            }
            return null;
        }

        private static void Add(List<IBrightnessBackend> list, IBrightnessBackend backend)
        {
            if (backend == null || !backend.IsPlatformSupported)
            {
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == backend)
                {
                    return;
                }
            }
            list.Add(backend);
        }
    }

    /// <summary>Conciencia de PPP por monitor (Per-Monitor V2) para que las ventanas de
    /// superposición y la interfaz usen píxeles físicos exactos.</summary>
    public static class DpiAwareness
    {
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        public static void EnablePerMonitor()
        {
            try
            {
                if (Win32.SetProcessDpiAwarenessContext(PerMonitorAwareV2))
                {
                    return;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (Exception)
            {
            }
            try
            {
                Win32.SetProcessDPIAware();
            }
            catch
            {
            }
        }
    }
}
