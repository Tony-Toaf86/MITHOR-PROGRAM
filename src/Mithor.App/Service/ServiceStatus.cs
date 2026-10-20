using System;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using Mithor.Core.Util;

namespace Mithor.App.Service
{
    /// <summary>Consulta y control del servicio de Windows que supervisa MITHOR.</summary>
    internal static class ServiceStatus
    {
        public const string ServiceName = "MITHOR";

        public static bool IsInstalled()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + ServiceName, false))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunning()
        {
            try
            {
                using (ServiceController controller = new ServiceController(ServiceName))
                {
                    return controller.Status == ServiceControllerStatus.Running
                        || controller.Status == ServiceControllerStatus.StartPending;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string Describe()
        {
            if (!IsInstalled())
            {
                return "no instalado";
            }
            try
            {
                using (ServiceController controller = new ServiceController(ServiceName))
                {
                    switch (controller.Status)
                    {
                        case ServiceControllerStatus.Running: return "activo";
                        case ServiceControllerStatus.Stopped: return "detenido";
                        case ServiceControllerStatus.StartPending: return "iniciando";
                        case ServiceControllerStatus.StopPending: return "deteniéndose";
                        case ServiceControllerStatus.Paused: return "en pausa";
                        default: return controller.Status.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo consultar el servicio: " + Log.Describe(ex));
                return "desconocido";
            }
        }

        /// <summary>Detiene el servicio pidiendo elevación (UAC). Devuelve false si se cancela.</summary>
        public static bool StopElevated()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "sc.exe";
                info.Arguments = "stop " + ServiceName;
                info.UseShellExecute = true;
                info.Verb = "runas";
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(info))
                {
                    if (process != null)
                    {
                        process.WaitForExit(15000);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo detener el servicio: " + Log.Describe(ex));
                return false;
            }

            for (int i = 0; i < 20; i++)
            {
                if (!IsRunning())
                {
                    return true;
                }
                System.Threading.Thread.Sleep(300);
            }
            return !IsRunning();
        }
    }
}
