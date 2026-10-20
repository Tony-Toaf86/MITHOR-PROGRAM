using System;
using System.Collections.Generic;
using System.Text;
using Mithor.App.Service;
using Mithor.Core.Config;
using Mithor.Core.Services;
using Mithor.Core.Util;
using Mithor.Platform.Windows;
using Mithor.Platform.Windows.Native;
using Mithor.Platform.Windows.SystemIntegration;

namespace Mithor.App.Ui
{
    /// <summary>Genera informes de diagnóstico pensados para soporte y resolución de problemas.</summary>
    internal static class Diagnostics
    {
        public static string Build(BrightnessController controller, AppSettings settings, WindowsBackendProvider provider)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MITHOR " + AppInfo.Version);
            sb.AppendLine("Sistema     : " + Environment.OSVersion.VersionString
                + " · " + (Environment.Is64BitProcess ? "x64" : "x86"));
            sb.AppendLine("Sesión      : " + (Win32.GetSystemMetrics(Win32.SM_REMOTESESSION) != 0 ? "remota" : "local"));
            sb.AppendLine("Servicio    : " + ServiceStatus.Describe()
                + (ServiceStatus.IsInstalled() ? " (MITHOR)" : string.Empty));
            sb.AppendLine("Proceso     : " + AppInfo.ExecutablePath);
            sb.AppendLine("Configuración: " + AppPaths.SettingsFile);
            sb.AppendLine("Registro    : " + (Log.CurrentFile ?? "no disponible"));
            sb.AppendLine("Método por defecto: " + settings.DefaultBackend);
            sb.AppendLine();

            IList<DisplayStatus> statuses = controller.Snapshot();
            sb.AppendLine("Pantallas activas: " + statuses.Count);
            for (int i = 0; i < statuses.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine(DescribeDisplay(statuses[i]));
            }

            sb.AppendLine();
            sb.AppendLine("Métodos registrados:");
            IList<Mithor.Core.Abstractions.IBrightnessBackend> backends = provider.All;
            for (int i = 0; i < backends.Count; i++)
            {
                sb.AppendLine(string.Format("  - {0,-8} {1,-24} prioridad {2,3}  soportado: {3}",
                    backends[i].Id, backends[i].Label, backends[i].Priority,
                    backends[i].IsPlatformSupported ? "sí" : "no"));
            }
            return sb.ToString();
        }

        public static string DescribeDisplay(DisplayStatus status)
        {
            if (status == null || status.Display == null)
            {
                return "Pantalla no disponible.";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("Pantalla #{0}: {1}", status.Display.MonitorIndex, status.Display.FriendlyName));
            sb.AppendLine("  id            : " + status.Display.Id);
            sb.AppendLine("  dispositivo   : " + status.Display.DeviceName);
            sb.AppendLine("  tecnología    : " + status.Display.OutputTechnology
                + (status.Display.IsInternal ? " (panel interno)" : string.Empty)
                + (status.Display.IsVirtual ? " (virtual)" : string.Empty));
            sb.AppendLine("  resolución    : " + status.Display.Bounds
                + (status.Display.RefreshHz > 1 ? " · " + status.Display.RefreshHz + " Hz" : string.Empty)
                + (status.Display.IsHdrActive ? " · HDR activo" : string.Empty));
            sb.AppendLine("  fabricante    : " + (string.IsNullOrEmpty(status.Display.Manufacturer) ? "—" : status.Display.Manufacturer));
            sb.AppendLine("  ruta conector : " + (string.IsNullOrEmpty(status.Display.ConnectorId) ? "—" : status.Display.ConnectorId));
            sb.AppendLine("  método        : " + status.BackendLabel
                + (status.Backend == null ? string.Empty : " (" + status.Backend.Id + ")"));
            sb.AppendLine("  sondeo        : " + status.Probe.Describe());
            sb.AppendLine("  brillo        : " + status.EffectivePercent + " %"
                + (status.Pending ? " (aplicando " + status.PendingPercent + " %)" : string.Empty));
            if (status.Failed && !string.IsNullOrEmpty(status.FailureReason))
            {
                sb.AppendLine("  error         : " + status.FailureReason);
            }
            if (!string.IsNullOrEmpty(status.Display.Notes))
            {
                sb.AppendLine("  notas         : " + status.Display.Notes);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
