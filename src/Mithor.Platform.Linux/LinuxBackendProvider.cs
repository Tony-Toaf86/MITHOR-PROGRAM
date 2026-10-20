using System;
using System.Collections.Generic;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Platform.Linux.Backends;

namespace Mithor.Platform.Linux
{
    /// <summary>
    /// Punto de entrada de la plataforma Linux. Replica la estructura de
    /// WindowsBackendProvider para que el núcleo y la interfaz funcionen sin cambios.
    /// </summary>
    public sealed class LinuxBackendProvider : IBackendProvider
    {
        private readonly List<IBrightnessBackend> _all = new List<IBrightnessBackend>();
        private readonly SysfsBacklightBackend _sysfs = new SysfsBacklightBackend();
        private readonly DdcutilBackend _ddcutil = new DdcutilBackend();
        private readonly RandrGammaBackend _randr = new RandrGammaBackend();
        private readonly LinuxOverlayBackend _overlay = new LinuxOverlayBackend();

        public LinuxBackendProvider()
        {
            _all.Add(_sysfs);
            _all.Add(_ddcutil);
            _all.Add(_randr);
            _all.Add(_overlay);
        }

        public IList<IBrightnessBackend> All { get { return _all; } }

        public IList<IBrightnessBackend> GetCandidates(DisplayInfo display)
        {
            List<IBrightnessBackend> candidates = new List<IBrightnessBackend>();
            if (display != null && display.Kind == MonitorKind.Internal)
            {
                Add(candidates, _sysfs);
                Add(candidates, _ddcutil);
                Add(candidates, _randr);
            }
            else if (display != null && display.Kind == MonitorKind.Virtual)
            {
                Add(candidates, _randr);
            }
            else
            {
                Add(candidates, _ddcutil);
                Add(candidates, _randr);
            }
            Add(candidates, _overlay);
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

    /// <summary>Fábrica de la plataforma Linux (usada por una futura compilación net8).</summary>
    public static class LinuxPlatform
    {
        public static bool IsLinux
        {
            get { return Environment.OSVersion.Platform == PlatformID.Unix; }
        }

        public static IBackendProvider CreateProvider()
        {
            return new LinuxBackendProvider();
        }

        public static IDisplayEnumerator CreateEnumerator()
        {
            return new LinuxDisplayEnumerator();
        }
    }
}
