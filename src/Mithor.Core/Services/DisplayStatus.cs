using Mithor.Core.Abstractions;
using Mithor.Core.Model;

namespace Mithor.Core.Services
{
    /// <summary>Estado actual de una pantalla en el controlador.</summary>
    public sealed class DisplayStatus
    {
        public DisplayInfo Display { get; set; }
        public IBrightnessBackend Backend { get; set; }
        public BackendProbe Probe { get; set; }

        /// <summary>Último valor confirmado (0..100).</summary>
        public int Percent { get; set; }

        /// <summary>Valor solicitado que aún no se ha aplicado.</summary>
        public int PendingPercent { get; set; }

        public bool Pending { get; set; }
        public bool Failed { get; set; }
        public string FailureReason { get; set; }

        public string Id { get { return Display == null ? null : Display.Id; } }
        public string BackendId { get { return Backend == null ? "none" : Backend.Id; } }
        public string BackendLabel { get { return Backend == null ? "sin método" : Backend.Label; } }
        public int EffectivePercent { get { return Pending ? PendingPercent : Percent; } }
        public bool HardwareBackend
        {
            get { return Backend != null && (Backend.Id == "ddc" || Backend.Id == "wmi" || Backend.Id == "sysfs"); }
        }
    }
}
