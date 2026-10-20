using System;
using System.Runtime.InteropServices;

namespace Mithor.App.Service
{
    /// <summary>P/Invoke necesario para lanzar el agente en la sesión interactiva del usuario.</summary>
    internal static class ServiceNative
    {
        public const uint WTS_CURRENT_SERVER_HANDLE = 0;
        public const uint MAXIMUM_ALLOWED = 0x02000000;
        public const int SecurityImpersonation = 2;
        public const int TokenPrimary = 1;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DuplicateTokenEx(
            IntPtr existingToken,
            uint desiredAccess,
            IntPtr tokenAttributes,
            int impersonationLevel,
            int tokenType,
            out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUser(
            IntPtr token,
            string applicationName,
            string commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessWithTokenW(
            IntPtr token,
            int logonFlags,
            string applicationName,
            string commandLine,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WTS_SESSION_INFO
        {
            public int SessionId;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pWinStationName;
            public int State;
        }

        public const int WTS_ACTIVE = 0;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version,
            out IntPtr sessionInfo, out int count);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);

        public static int[] ActiveSessions()
        {
            System.Collections.Generic.List<int> sessions = new System.Collections.Generic.List<int>();
            IntPtr buffer = IntPtr.Zero;
            int count = 0;
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buffer, out count))
            {
                return sessions.ToArray();
            }
            try
            {
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                long baseAddress = buffer.ToInt64();
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(baseAddress + i * size);
                    WTS_SESSION_INFO info = (WTS_SESSION_INFO)Marshal.PtrToStructure(item, typeof(WTS_SESSION_INFO));
                    if (info.State == WTS_ACTIVE && info.SessionId > 0)
                    {
                        sessions.Add(info.SessionId);
                    }
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
            return sessions.ToArray();
        }

        public static string LastError()
        {
            int code = Marshal.GetLastWin32Error();
            try
            {
                return new System.ComponentModel.Win32Exception(code).Message + " (0x" + code.ToString("X8") + ")";
            }
            catch
            {
                return "0x" + code.ToString("X8");
            }
        }
    }
}
