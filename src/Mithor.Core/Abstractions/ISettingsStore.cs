namespace Mithor.Core.Abstractions
{
    /// <summary>Persistencia de la configuración (archivo JSON en Windows/Linux).</summary>
    public interface ISettingsStore
    {
        /// <summary>Ruta legible para el usuario.</summary>
        string Describe();

        /// <summary>Devuelve el contenido guardado o null si no existe.</summary>
        string Read();

        /// <summary>Escribe el contenido de forma atómica (temporal + reemplazo).</summary>
        void Write(string text);
    }
}
