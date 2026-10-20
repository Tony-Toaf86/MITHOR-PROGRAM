using System;
using System.IO;
using System.ServiceProcess;
using Mithor.Core.Util;

namespace Mithor.App.Service
{
    /// <summary>Servicio de Windows que mantiene el agente de brillo en segundo plano.</summary>
    public sealed class MithorService : ServiceBase
    {
        private AgentSupervisor _supervisor;

        public MithorService()
        {
            ServiceName = "MITHOR";
            CanStop = true;
            CanShutdown = true;
            CanHandleSessionChangeEvent = true;
            AutoLog = true;
        }

        public static int Run()
        {
            try
            {
                ServiceBase.Run(new MithorService());
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "MITHOR", "logs");
                    Log.Init(directory, true);
                    Log.Error("No se pudo iniciar el servicio MITHOR", ex);
                }
                catch
                {
                }
                return 1;
            }
        }

        protected override void OnStart(string[] args)
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MITHOR", "logs");
            Log.Init(directory, true);
            Log.Info("Servicio MITHOR iniciado (pid " + System.Diagnostics.Process.GetCurrentProcess().Id + ")");

            _supervisor = new AgentSupervisor();
            _supervisor.Start();
        }

        protected override void OnStop()
        {
            Log.Info("Servicio MITHOR detenido.");
            if (_supervisor != null)
            {
                _supervisor.Stop();
                _supervisor = null;
            }
        }

        protected override void OnSessionChange(SessionChangeDescription change)
        {
            Log.Info("Cambio de sesión: " + change.Reason + " (id " + change.SessionId + ")");
            if (_supervisor != null)
            {
                _supervisor.Wake();
            }
        }
    }
}
