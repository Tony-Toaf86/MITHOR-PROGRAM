using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mithor.Setup
{
    /// <summary>Alta, arranque y baja del servicio de Windows mediante la API SCM.</summary>
    internal static class ServiceInstaller
    {
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        private const uint SERVICE_ALL_ACCESS = 0xF01FF;
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const uint SERVICE_AUTO_START = 0x00000002;
        private const uint SERVICE_ERROR_NORMAL = 0x00000001;
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const uint SERVICE_START = 0x0010;
        private const uint SERVICE_STOP = 0x0020;
        private const uint SERVICE_CONTROL_STOP = 0x00000001;
        private const uint SERVICE_CONFIG_DESCRIPTION = 1;
        private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
        private const uint SERVICE_CONFIG_FAILURE_ACTIONS_FLAG = 4;
        private const uint SERVICE_STOPPED = 1;
        private const uint SERVICE_RUNNING = 4;
        private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
        private const int ERROR_SERVICE_EXISTS = 1073;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_DESCRIPTION
        {
            public string lpDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SC_ACTION
        {
            public uint Type;
            public uint Delay;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_FAILURE_ACTIONS
        {
            public uint dwResetPeriod;
            public string lpRebootMsg;
            public string lpCommand;
            public uint cActions;
            public IntPtr lpsaActions;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateService(
            IntPtr scManager, string serviceName, string displayName, uint desiredAccess,
            uint serviceType, uint startType, uint errorControl, string binaryPathName,
            string loadOrderGroup, IntPtr tagId, string dependencies,
            string serviceStartName, string password);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ChangeServiceConfig(
            IntPtr service, uint serviceType, uint startType, uint errorControl,
            string binaryPathName, string loadOrderGroup, IntPtr tagId, string dependencies,
            string serviceStartName, string password, string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref SERVICE_DESCRIPTION info);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref SERVICE_FAILURE_ACTIONS info);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref int info);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(IntPtr service, uint argumentCount, string[] arguments);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS status);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr service, ref SERVICE_STATUS status);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr service);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        public static void Install(string installDirectory)
        {
            string binaryPath = "\"" + Path.Combine(installDirectory, "MITHOR.exe") + "\" --service";
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "No se pudo abrir el Administrador de servicios.");
            }

            try
            {
                IntPtr service = IntPtr.Zero;
                int lastError = 0;
                for (int attempt = 0; attempt < 6 && service == IntPtr.Zero; attempt++)
                {
                    service = CreateService(scm, InstallEngine.ServiceName, InstallEngine.DisplayName,
                        SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL,
                        binaryPath, null, IntPtr.Zero, null, null, null);
                    if (service != IntPtr.Zero)
                    {
                        break;
                    }

                    lastError = Marshal.GetLastWin32Error();
                    if (lastError == ERROR_SERVICE_EXISTS)
                    {
                        service = OpenService(scm, InstallEngine.ServiceName, SERVICE_ALL_ACCESS);
                        if (service == IntPtr.Zero)
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        if (!ChangeServiceConfig(service, SERVICE_NO_CHANGE, SERVICE_AUTO_START, SERVICE_NO_CHANGE,
                            binaryPath, null, IntPtr.Zero, null, null, null, InstallEngine.DisplayName))
                        {
                            int changeError = Marshal.GetLastWin32Error();
                            CloseServiceHandle(service);
                            throw new Win32Exception(changeError, "No se pudo actualizar el servicio existente.");
                        }
                        break;
                    }
                    if (lastError == 1072) // marcado para eliminar
                    {
                        Thread.Sleep(600);
                        continue;
                    }
                    throw new Win32Exception(lastError, "No se pudo crear el servicio de Windows.");
                }

                if (service == IntPtr.Zero)
                {
                    throw new Win32Exception(lastError,
                        "El servicio anterior todavía se está eliminando. Reinicia el equipo e inténtalo de nuevo.");
                }

                try
                {
                    ApplyDescription(service);
                    ApplyRecovery(service);
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public static void Start()
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                IntPtr service = OpenService(scm, InstallEngine.ServiceName, SERVICE_START | SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "No se pudo abrir el servicio para iniciarlo.");
                }
                try
                {
                    if (!StartService(service, 0, null))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != ERROR_SERVICE_ALREADY_RUNNING)
                        {
                            throw new Win32Exception(error, "No se pudo iniciar el servicio.");
                        }
                    }
                    WaitForState(service, SERVICE_RUNNING, 15000);
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public static void Remove()
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
            {
                return;
            }
            try
            {
                IntPtr service = OpenService(scm, InstallEngine.ServiceName,
                    SERVICE_STOP | SERVICE_QUERY_STATUS | 0x00010000);
                if (service == IntPtr.Zero)
                {
                    return;
                }
                try
                {
                    SERVICE_STATUS status = new SERVICE_STATUS();
                    ControlService(service, SERVICE_CONTROL_STOP, ref status);
                    WaitForState(service, SERVICE_STOPPED, 10000);
                    if (!DeleteService(service))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 1072)
                        {
                            InstallEngine.Log("No se pudo eliminar el servicio: " + error);
                        }
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        private static void WaitForState(IntPtr service, uint desiredState, int timeoutMilliseconds)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            SERVICE_STATUS status = new SERVICE_STATUS();
            while (DateTime.UtcNow < limit)
            {
                if (!QueryServiceStatus(service, ref status))
                {
                    return;
                }
                if (status.dwCurrentState == desiredState)
                {
                    return;
                }
                Thread.Sleep(200);
            }
        }

        private static void ApplyDescription(IntPtr service)
        {
            SERVICE_DESCRIPTION description = new SERVICE_DESCRIPTION();
            description.lpDescription = "Mantiene MITHOR en segundo plano y arranca el control de brillo "
                + "al iniciar sesión.";
            ChangeServiceConfig2(service, SERVICE_CONFIG_DESCRIPTION, ref description);
        }

        private static void ApplyRecovery(IntPtr service)
        {
            SC_ACTION[] actions = new SC_ACTION[3];
            actions[0].Type = 1; // SC_ACTION_RESTART
            actions[0].Delay = 5000;
            actions[1].Type = 1;
            actions[1].Delay = 15000;
            actions[2].Type = 1;
            actions[2].Delay = 60000;

            GCHandle handle = GCHandle.Alloc(actions, GCHandleType.Pinned);
            try
            {
                SERVICE_FAILURE_ACTIONS failure = new SERVICE_FAILURE_ACTIONS();
                failure.dwResetPeriod = 86400;
                failure.cActions = (uint)actions.Length;
                failure.lpsaActions = handle.AddrOfPinnedObject();
                ChangeServiceConfig2(service, SERVICE_CONFIG_FAILURE_ACTIONS, ref failure);

                int enabled = 1;
                ChangeServiceConfig2(service, SERVICE_CONFIG_FAILURE_ACTIONS_FLAG, ref enabled);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
