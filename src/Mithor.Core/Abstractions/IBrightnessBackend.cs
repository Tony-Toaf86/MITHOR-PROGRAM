using System.Collections.Generic;
using Mithor.Core.Model;

namespace Mithor.Core.Abstractions
{
    /// <summary>
    /// Un método concreto de control de brillo (DDC/CI, WMI, gamma, superposición,
    /// sysfs, ddcutil, RandR...). Todas las llamadas se hacen desde el hilo de trabajo
    /// del controlador, nunca desde el hilo de la interfaz.
    /// </summary>
    public interface IBrightnessBackend
    {
        /// <summary>Identificador corto y estable: "ddc", "wmi", "gamma", "overlay"...</summary>
        string Id { get; }

        /// <summary>Etiqueta para la interfaz ("DDC/CI (hardware)").</summary>
        string Label { get; }

        /// <summary>Explicación corta del método.</summary>
        string Description { get; }

        /// <summary>Menor = se intenta antes en modo automático.</summary>
        int Priority { get; }

        /// <summary>Indica si el método puede funcionar en esta plataforma/sesión.</summary>
        bool IsPlatformSupported { get; }

        /// <summary>Comprueba si este método puede controlar la pantalla indicada.</summary>
        BackendProbe Probe(DisplayInfo display);

        /// <summary>Lee el brillo actual. Devuelve null si el método no puede leerlo.</summary>
        int? TryRead(DisplayInfo display);

        /// <summary>Aplica un brillo en porcentaje (0..100).</summary>
        bool TryWrite(DisplayInfo display, int percent);

        /// <summary>Se llama cuando cambia la lista de pantallas activas.</summary>
        void OnDisplaysChanged(IList<DisplayInfo> displays);

        /// <summary>Libera recursos (ventanas, rampas de gamma, handles DDC...).</summary>
        void Shutdown();
    }
}
