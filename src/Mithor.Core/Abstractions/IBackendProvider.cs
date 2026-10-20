using System.Collections.Generic;
using Mithor.Core.Model;

namespace Mithor.Core.Abstractions
{
    /// <summary>Enumera las pantallas activas de la plataforma.</summary>
    public interface IDisplayEnumerator
    {
        IList<DisplayInfo> Enumerate();
    }

    /// <summary>
    /// Decide qué métodos de control probar y en qué orden para cada pantalla.
    /// Windows y Linux implementan este contrato con sus propias prioridades.
    /// </summary>
    public interface IBackendProvider
    {
        /// <summary>Todos los métodos registrados.</summary>
        IList<IBrightnessBackend> All { get; }

        /// <summary>Métodos candidatos, del más deseable al último recurso.</summary>
        IList<IBrightnessBackend> GetCandidates(DisplayInfo display);

        /// <summary>Busca un método por su identificador.</summary>
        IBrightnessBackend Get(string id);
    }
}
