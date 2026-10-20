namespace Mithor.Core.Model
{
    /// <summary>
    /// Rango de brillo que maneja MITHOR. Los valores positivos son brillo del
    /// hardware; los negativos representan atenuación extra por software cuando el
    /// monitor ya está en su mínimo (0 = mínimo del monitor).
    /// </summary>
    public static class BrightnessRange
    {
        public const int Minimum = -100;
        public const int Maximum = 100;
        public const int HardwareMinimum = 0;

        public static int Clamp(int value)
        {
            if (value < Minimum) { return Minimum; }
            if (value > Maximum) { return Maximum; }
            return value;
        }

        /// <summary>Parte que se envía al monitor (0..100).</summary>
        public static int HardwarePart(int value)
        {
            return value > 0 ? value : 0;
        }

        /// <summary>Parte que se aplica con la capa de atenuación (0..100).</summary>
        public static int ExtraDimPart(int value)
        {
            return value < 0 ? -value : 0;
        }

        public static string Format(int value)
        {
            return value.ToString();
        }
    }
}
