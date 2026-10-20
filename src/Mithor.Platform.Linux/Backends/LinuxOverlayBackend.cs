using System;
using System.Collections.Generic;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;

namespace Mithor.Platform.Linux.Backends
{
    /// <summary>
    /// Atenuación universal en Linux. En X11 se puede implementar con una ventana
    /// override-redirect con propiedad _NET_WM_WINDOW_OPACITY; en Wayland cada
    /// compositor expone su propio mecanismo. Se deja como punto de extensión
    /// explícito (ver docs/ROADMAP-LINUX.md).
    /// </summary>
    public sealed class LinuxOverlayBackend : IBrightnessBackend
    {
        public string Id { get { return "overlay"; } }
        public string Label { get { return "Superposición"; } }
        public string Description { get { return "Capa de atenuación universal (pendiente de implementar en Linux)."; } }
        public int Priority { get { return 90; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            return BackendProbe.Unsupported("pendiente: requiere integración con el compositor (X11/Wayland)");
        }

        public int? TryRead(DisplayInfo display)
        {
            return null;
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            return false;
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
        }

        public void Shutdown()
        {
        }
    }
}
