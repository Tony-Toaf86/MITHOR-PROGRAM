using Mithor.Core.Model;

namespace Mithor.Core.Abstractions
{
    /// <summary>
    /// Capa de atenuación independiente del método de brillo. Permite bajar el brillo
    /// por debajo del mínimo del monitor: el hardware se queda en 0 % y la capa
    /// oscurece la imagen por encima.
    /// </summary>
    public interface IDimLayer
    {
        /// <summary>Aplica una atenuación extra (0..100) sobre la pantalla.</summary>
        void SetExtraDim(DisplayInfo display, int percent);

        /// <summary>Quita la atenuación extra de una pantalla.</summary>
        void ClearExtraDim(string displayId);

        /// <summary>Libera las ventanas y recursos de la capa.</summary>
        void Shutdown();
    }
}
