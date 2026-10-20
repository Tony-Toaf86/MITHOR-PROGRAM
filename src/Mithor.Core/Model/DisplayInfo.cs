using System;

namespace Mithor.Core.Model
{
    /// <summary>
    /// Descripción de una pantalla activa. La rellena cada plataforma
    /// (WindowsDisplayEnumerator / LinuxDisplayEnumerator).
    /// </summary>
    public sealed class DisplayInfo
    {
        /// <summary>Identificador estable usado para guardar preferencias.</summary>
        public string Id { get; set; }

        /// <summary>Nombre de dispositivo GDI, p. ej. \\.\DISPLAY1.</summary>
        public string DeviceName { get; set; }

        /// <summary>Nombre legible del monitor ("DELL U2720Q").</summary>
        public string FriendlyName { get; set; }

        /// <summary>Código PNP del fabricante ("DEL", "AUO", "GSM").</summary>
        public string Manufacturer { get; set; }

        /// <summary>Número de serie del EDID, si está disponible.</summary>
        public string SerialNumber { get; set; }

        /// <summary>Adaptador de vídeo que controla la pantalla.</summary>
        public string AdapterName { get; set; }

        /// <summary>Ruta de instancia del dispositivo de monitor (EDID / PnP).</summary>
        public string DeviceId { get; set; }

        /// <summary>Ruta del conector (incluye UID####), útil para cruzar con WMI.</summary>
        public string ConnectorId { get; set; }

        public MonitorKind Kind { get; set; }

        public bool IsPrimary { get; set; }

        /// <summary>Índice 1..N mostrado al usuario.</summary>
        public int MonitorIndex { get; set; }

        public PixelRect Bounds { get; set; }

        /// <summary>Handle nativo opaco (HMONITOR en Windows, IntPtr.Zero en Linux).</summary>
        public IntPtr Handle { get; set; }

        /// <summary>Tecnología de salida legible: "HDMI", "DisplayPort", "Integrado"...</summary>
        public string OutputTechnology { get; set; }

        /// <summary>True si la pantalla está en modo HDR activo (el gamma se ignora).</summary>
        public bool IsHdrActive { get; set; }

        public int RefreshHz { get; set; }

        /// <summary>Rotación en grados: 0, 90, 180 o 270.</summary>
        public int Rotation { get; set; }

        /// <summary>Notas de diagnóstico de la plataforma.</summary>
        public string Notes { get; set; }

        public bool IsInternal { get { return Kind == MonitorKind.Internal; } }
        public bool IsVirtual { get { return Kind == MonitorKind.Virtual; } }

        public string DisplaySize
        {
            get { return string.Format("{0}x{1}", Bounds.Width, Bounds.Height); }
        }

        public override string ToString()
        {
            return string.Format("#{0} {1} [{2}] {3}", MonitorIndex, FriendlyName, OutputTechnology, Bounds);
        }
    }
}
