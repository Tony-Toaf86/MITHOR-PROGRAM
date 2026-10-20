using System;
using System.Runtime.InteropServices;

namespace Mithor.Platform.Windows.Native
{
    /// <summary>
    /// P/Invoke de la API de configuración de pantallas (QueryDisplayConfig).
    /// Es la vía moderna para saber qué monitores están activos, su tecnología de
    /// conexión, su nombre EDID y su estado HDR.
    /// </summary>
    internal static class DisplayConfig
    {
        public const uint QDC_ALL_PATHS = 0x00000001;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        public const uint QDC_DATABASE_CURRENT = 0x00000004;
        public const uint QDC_VIRTUAL_MODE_AWARE = 0x00000010;

        public const int ERROR_SUCCESS = 0;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_BUSY = 170;

        public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
        public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_VGA = 0;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTV_DONGLE = 14;
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = unchecked((int)0x80000000);
        public const int DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32 = unchecked((int)0xFFFFFFFF);

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)]
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        /// <summary>Modo opaco de 64 bytes: no se necesita su contenido.</summary>
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        public struct MODE_INFO_BLOB
        {
            public long A;
            public long B;
            public long C;
            public long D;
            public long E;
            public long F;
            public long G;
            public long H;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
            public uint colorEncoding;
            public uint bitsPerColorChannel;
        }

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] MODE_INFO_BLOB[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO colorInfo);

        public static string OutputTechnologyName(uint technology)
        {
            switch ((int)technology)
            {
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_VGA: return "VGA";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO: return "S-Video";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO: return "Vídeo compuesto";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO: return "Vídeo por componentes";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI: return "DVI";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI: return "HDMI";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS: return "LVDS (integrado)";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN: return "D-Terminal";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI: return "SDI";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL: return "DisplayPort";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED: return "eDP (integrado)";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL: return "UDI";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED: return "UDI (integrado)";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTV_DONGLE: return "TV (dongle)";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL: return "Panel interno";
                case DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32: return "Virtual";
                default: return "Desconocido (" + technology + ")";
            }
        }

        public static bool IsInternalTechnology(uint technology)
        {
            int value = (int)technology;
            if (value == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
                || value == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                || value == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
                || value == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL)
            {
                return true;
            }
            return value < 0 && value != DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32;
        }

        public static string DecodePnpId(ushort manufacturer)
        {
            if (manufacturer == 0)
            {
                return string.Empty;
            }
            // Windows entrega el identificador EDID con los bytes invertidos.
            ushort value = (ushort)((manufacturer >> 8) | (manufacturer << 8));
            char a = (char)(((value >> 10) & 0x1F) + 'A' - 1);
            char b = (char)(((value >> 5) & 0x1F) + 'A' - 1);
            char c = (char)((value & 0x1F) + 'A' - 1);
            string text = new string(new char[] { a, b, c });
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < 'A' || text[i] > 'Z')
                {
                    return string.Empty;
                }
            }
            return text;
        }
    }
}
