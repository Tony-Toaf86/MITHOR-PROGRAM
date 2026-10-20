using System;

namespace Mithor.Core.Abstractions
{
    /// <summary>
    /// Resultado de sondear un método de control sobre una pantalla concreta.
    /// Los valores son "crudos" (0..Max del hardware) y se convierten a porcentaje
    /// con <see cref="ToPercent"/> / <see cref="ToRaw"/>.
    /// </summary>
    public struct BackendProbe
    {
        public bool Supported;
        public int Min;
        public int Max;
        public int Current;
        public bool HasCurrent;
        public string Detail;
        public string Reason;

        public static BackendProbe Unsupported(string reason)
        {
            BackendProbe probe = new BackendProbe();
            probe.Supported = false;
            probe.Detail = string.Empty;
            probe.Reason = reason;
            return probe;
        }

        public static BackendProbe SupportedRange(int min, int max, int current, bool hasCurrent, string detail)
        {
            BackendProbe probe = new BackendProbe();
            probe.Supported = true;
            probe.Min = min;
            probe.Max = max;
            probe.Current = current;
            probe.HasCurrent = hasCurrent;
            probe.Detail = detail;
            probe.Reason = string.Empty;
            return probe;
        }

        public bool HasRange { get { return Supported && Max > Min; } }

        public int ClampRaw(int value)
        {
            int lo = Math.Min(Min, Max);
            int hi = Math.Max(Min, Max);
            return Math.Max(lo, Math.Min(hi, value));
        }

        public int ToPercent(int raw)
        {
            if (!HasRange)
            {
                return ClampPercent(raw);
            }
            double p = (raw - Min) * 100.0 / (Max - Min);
            return ClampPercent((int)Math.Round(p));
        }

        public int ToRaw(int percent)
        {
            if (!HasRange)
            {
                return ClampRaw(percent);
            }
            return Min + (int)Math.Round((Max - Min) * (ClampPercent(percent) / 100.0));
        }

        public static int ClampPercent(int percent)
        {
            if (percent < 0) { return 0; }
            if (percent > 100) { return 100; }
            return percent;
        }

        public string Describe()
        {
            if (!Supported)
            {
                return "no compatible" + (string.IsNullOrEmpty(Reason) ? string.Empty : " (" + Reason + ")");
            }
            if (HasCurrent)
            {
                return string.Format("0-{0} (actual {1}) {2}", Max, Current, Detail);
            }
            return string.Format("0-{0} {1}", Max, Detail);
        }
    }
}
