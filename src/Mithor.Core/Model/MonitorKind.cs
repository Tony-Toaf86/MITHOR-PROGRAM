namespace Mithor.Core.Model
{
    /// <summary>Tipo físico de pantalla, tal como lo reporta el sistema operativo.</summary>
    public enum MonitorKind
    {
        /// <summary>No se pudo determinar (adaptadores virtuales, capturadoras, KVM...).</summary>
        Unknown = 0,

        /// <summary>Panel integrado de portátil / todo-en-uno (eDP, LVDS, MIPI).</summary>
        Internal = 1,

        /// <summary>Monitor externo (HDMI, DisplayPort, DVI, VGA, USB-C, TV, proyector).</summary>
        External = 2,

        /// <summary>Pantalla virtual: escritorio remoto, VM, pantalla espaciada, capturadora.</summary>
        Virtual = 3
    }
}
