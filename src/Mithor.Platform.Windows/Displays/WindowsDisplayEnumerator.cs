using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;
using Mithor.Platform.Windows.Native;

namespace Mithor.Platform.Windows.Displays
{
    /// <summary>
    /// Enumera las pantallas activas con QueryDisplayConfig (la API moderna) y, si no
    /// está disponible, recurre a EnumDisplayDevices. Detecta paneles integrados,
    /// monitores externos, pantallas virtuales y el estado HDR.
    /// </summary>
    public sealed class WindowsDisplayEnumerator : IDisplayEnumerator
    {
        public IList<DisplayInfo> Enumerate()
        {
            List<DisplayInfo> result = new List<DisplayInfo>();
            bool remote = Win32.GetSystemMetrics(Win32.SM_REMOTESESSION) != 0;

            try
            {
                EnumerateViaDisplayConfig(result, remote);
            }
            catch (Exception ex)
            {
                Log.Warn("QueryDisplayConfig falló: " + Log.Describe(ex));
                result.Clear();
            }

            if (result.Count == 0)
            {
                try
                {
                    EnumerateFallback(result, remote);
                }
                catch (Exception ex)
                {
                    Log.Warn("Enumeración de reserva falló: " + Log.Describe(ex));
                    result.Clear();
                }
            }

            if (result.Count == 0)
            {
                DisplayInfo synthetic = new DisplayInfo();
                synthetic.Id = "virtual-1";
                synthetic.DeviceName = "\\\\.\\DISPLAY1";
                synthetic.FriendlyName = "Pantalla del sistema";
                synthetic.Kind = MonitorKind.Virtual;
                synthetic.MonitorIndex = 1;
                synthetic.Bounds = new PixelRect(0, 0,
                    Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CXSCREEN)),
                    Math.Max(1, Win32.GetSystemMetrics(Win32.SM_CYSCREEN)));
                synthetic.IsPrimary = true;
                synthetic.OutputTechnology = "Virtual";
                synthetic.Notes = "No se pudo consultar el subsistema de pantallas.";
                result.Add(synthetic);
            }

            for (int i = 0; i < result.Count; i++)
            {
                if (string.IsNullOrEmpty(result[i].Id))
                {
                    result[i].Id = "pantalla-" + (i + 1).ToString();
                }
                if (string.IsNullOrEmpty(result[i].FriendlyName))
                {
                    result[i].FriendlyName = "Pantalla " + (i + 1).ToString();
                }
            }
            return result;
        }

        private static void EnumerateViaDisplayConfig(List<DisplayInfo> result, bool remote)
        {
            uint pathCount;
            uint modeCount;
            int error = DisplayConfig.GetDisplayConfigBufferSizes(
                DisplayConfig.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
            if (error != 0)
            {
                throw new Win32Exception(error);
            }
            if (pathCount == 0)
            {
                return;
            }

            DisplayConfig.DISPLAYCONFIG_PATH_INFO[] paths = new DisplayConfig.DISPLAYCONFIG_PATH_INFO[pathCount];
            DisplayConfig.MODE_INFO_BLOB[] modes = new DisplayConfig.MODE_INFO_BLOB[Math.Max(1, modeCount)];

            int attempts = 0;
            while (true)
            {
                uint currentPaths = pathCount;
                uint currentModes = (uint)modes.Length;
                error = DisplayConfig.QueryDisplayConfig(
                    DisplayConfig.QDC_ONLY_ACTIVE_PATHS,
                    ref currentPaths,
                    paths,
                    ref currentModes,
                    modes,
                    IntPtr.Zero);

                if (error == DisplayConfig.ERROR_INSUFFICIENT_BUFFER && attempts < 3)
                {
                    attempts++;
                    DisplayConfig.GetDisplayConfigBufferSizes(DisplayConfig.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                    paths = new DisplayConfig.DISPLAYCONFIG_PATH_INFO[pathCount];
                    modes = new DisplayConfig.MODE_INFO_BLOB[Math.Max(1, modeCount)];
                    continue;
                }
                if (error == DisplayConfig.ERROR_BUSY && attempts < 4)
                {
                    attempts++;
                    Thread.Sleep(40);
                    continue;
                }
                if (error != 0)
                {
                    throw new Win32Exception(error);
                }
                pathCount = currentPaths;
                break;
            }

            for (uint i = 0; i < pathCount; i++)
            {
                DisplayConfig.DISPLAYCONFIG_PATH_INFO path = paths[i];
                if (!path.targetInfo.targetAvailable)
                {
                    continue;
                }

                string gdiName = GetSourceName(path);
                if (string.IsNullOrEmpty(gdiName))
                {
                    continue;
                }
                if (ContainsDevice(result, gdiName))
                {
                    continue;
                }

                DisplayConfig.DISPLAYCONFIG_TARGET_DEVICE_NAME target;
                bool hasTarget = TryGetTargetName(path, out target);

                Win32.DEVMODE mode = new Win32.DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf(typeof(Win32.DEVMODE));
                bool hasMode = Win32.EnumDisplaySettings(gdiName, Win32.ENUM_CURRENT_SETTINGS, ref mode);

                Win32.DISPLAY_DEVICE monitor = new Win32.DISPLAY_DEVICE();
                monitor.cb = Marshal.SizeOf(typeof(Win32.DISPLAY_DEVICE));
                bool hasMonitor = Win32.EnumDisplayDevices(gdiName, 0, ref monitor, 0);

                int centerX = hasMode ? mode.dmPositionX + Math.Max(1, mode.dmPelsWidth) / 2 : 0;
                int centerY = hasMode ? mode.dmPositionY + Math.Max(1, mode.dmPelsHeight) / 2 : 0;
                IntPtr hMonitor = Win32.MonitorFromPoint(
                    new Win32.POINT { X = centerX, Y = centerY }, Win32.MONITOR_DEFAULTTONEAREST);

                Win32.MONITORINFOEX monitorInfo = new Win32.MONITORINFOEX();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(Win32.MONITORINFOEX));
                bool hasMonitorInfo = Win32.GetMonitorInfo(hMonitor, ref monitorInfo);

                PixelRect bounds;
                if (hasMonitorInfo)
                {
                    bounds = ToRect(monitorInfo.rcMonitor);
                }
                else if (hasMode)
                {
                    bounds = new PixelRect(mode.dmPositionX, mode.dmPositionY,
                        Math.Max(1, mode.dmPelsWidth), Math.Max(1, mode.dmPelsHeight));
                }
                else
                {
                    continue;
                }

                DisplayInfo info = new DisplayInfo();
                info.DeviceName = gdiName;
                info.Handle = hMonitor;
                info.Bounds = bounds;
                info.IsPrimary = hasMonitorInfo
                    ? (monitorInfo.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0
                    : (bounds.X <= 0 && bounds.Y <= 0);

                info.DeviceId = hasMonitor ? monitor.DeviceID : string.Empty;
                info.ConnectorId = hasTarget ? target.monitorDevicePath : string.Empty;
                info.Id = !string.IsNullOrEmpty(info.DeviceId)
                    ? info.DeviceId
                    : (!string.IsNullOrEmpty(info.ConnectorId) ? info.ConnectorId : gdiName);

                info.FriendlyName = hasTarget ? CleanName(target.monitorFriendlyDeviceName) : string.Empty;
                if (string.IsNullOrEmpty(info.FriendlyName) && hasMonitor)
                {
                    info.FriendlyName = CleanName(monitor.DeviceString);
                }
                info.Manufacturer = hasTarget ? DisplayConfig.DecodePnpId(target.edidManufactureId) : string.Empty;
                string fromDeviceId = ManufacturerFromDeviceId(info.DeviceId);
                if (!string.IsNullOrEmpty(fromDeviceId))
                {
                    info.Manufacturer = fromDeviceId;
                }

                info.OutputTechnology = DisplayConfig.OutputTechnologyName(path.targetInfo.outputTechnology);
                info.RefreshHz = hasMode ? mode.dmDisplayFrequency : 0;
                info.Rotation = hasMode ? ToDegrees(mode.dmDisplayOrientation) : 0;
                info.IsHdrActive = TryGetHdr(path);
                info.Kind = Classify(path, remote);
                info.AdapterName = string.Empty;

                if (remote)
                {
                    info.Notes = "Sesión remota: el control de hardware no suele estar disponible.";
                }
                else if (info.Kind == MonitorKind.Internal)
                {
                    info.Notes = "Panel integrado: se controla con WMI o, si el panel lo permite, DDC/CI.";
                }
                else if (info.Kind == MonitorKind.External)
                {
                    info.Notes = "Monitor externo: DDC/CI es el método preferido.";
                }
                else
                {
                    info.Notes = "Tecnología no reconocida: se probarán todos los métodos.";
                }

                Log.Debug(string.Format("Pantalla detectada: {0} | {1} | {2} | {3} | HDR={4}",
                    info.FriendlyName, info.DeviceName, info.OutputTechnology, bounds, info.IsHdrActive));

                result.Add(info);
            }
        }

        private static void EnumerateFallback(List<DisplayInfo> result, bool remote)
        {
            for (uint i = 0; i < 32; i++)
            {
                Win32.DISPLAY_DEVICE adapter = new Win32.DISPLAY_DEVICE();
                adapter.cb = Marshal.SizeOf(typeof(Win32.DISPLAY_DEVICE));
                if (!Win32.EnumDisplayDevices(null, i, ref adapter, 0))
                {
                    break;
                }
                if ((adapter.StateFlags & Win32.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                {
                    continue;
                }
                if ((adapter.StateFlags & Win32.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0)
                {
                    continue;
                }
                if (ContainsDevice(result, adapter.DeviceName))
                {
                    continue;
                }

                Win32.DEVMODE mode = new Win32.DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf(typeof(Win32.DEVMODE));
                if (!Win32.EnumDisplaySettings(adapter.DeviceName, Win32.ENUM_CURRENT_SETTINGS, ref mode))
                {
                    continue;
                }

                Win32.DISPLAY_DEVICE monitor = new Win32.DISPLAY_DEVICE();
                monitor.cb = Marshal.SizeOf(typeof(Win32.DISPLAY_DEVICE));
                bool hasMonitor = Win32.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0);

                IntPtr hMonitor = Win32.MonitorFromPoint(
                    new Win32.POINT
                    {
                        X = mode.dmPositionX + Math.Max(1, mode.dmPelsWidth) / 2,
                        Y = mode.dmPositionY + Math.Max(1, mode.dmPelsHeight) / 2
                    },
                    Win32.MONITOR_DEFAULTTONEAREST);

                Win32.MONITORINFOEX monitorInfo = new Win32.MONITORINFOEX();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(Win32.MONITORINFOEX));
                bool hasMonitorInfo = Win32.GetMonitorInfo(hMonitor, ref monitorInfo);
                PixelRect bounds = hasMonitorInfo
                    ? ToRect(monitorInfo.rcMonitor)
                    : new PixelRect(mode.dmPositionX, mode.dmPositionY,
                        Math.Max(1, mode.dmPelsWidth), Math.Max(1, mode.dmPelsHeight));

                DisplayInfo info = new DisplayInfo();
                info.DeviceName = adapter.DeviceName;
                info.Handle = hMonitor;
                info.Bounds = bounds;
                info.IsPrimary = hasMonitorInfo
                    ? (monitorInfo.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0
                    : (bounds.X <= 0 && bounds.Y <= 0);
                info.DeviceId = hasMonitor ? monitor.DeviceID : string.Empty;
                info.ConnectorId = adapter.DeviceID;
                info.Id = !string.IsNullOrEmpty(info.DeviceId) ? info.DeviceId : adapter.DeviceName;
                info.FriendlyName = hasMonitor ? CleanName(monitor.DeviceString) : adapter.DeviceString;
                info.OutputTechnology = "Desconocido";
                info.RefreshHz = mode.dmDisplayFrequency;
                info.Rotation = ToDegrees(mode.dmDisplayOrientation);
                info.Kind = remote ? MonitorKind.Virtual : MonitorKind.Unknown;
                info.Notes = "Detectado con EnumDisplayDevices (sin QueryDisplayConfig).";
                result.Add(info);
            }
        }

        private static bool ContainsDevice(List<DisplayInfo> result, string deviceName)
        {
            for (int i = 0; i < result.Count; i++)
            {
                if (string.Equals(result[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetSourceName(DisplayConfig.DISPLAYCONFIG_PATH_INFO path)
        {
            DisplayConfig.DISPLAYCONFIG_SOURCE_DEVICE_NAME source = new DisplayConfig.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            source.header.type = DisplayConfig.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfig.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            int error = DisplayConfig.DisplayConfigGetDeviceInfo(ref source);
            if (error != 0)
            {
                return null;
            }
            return CleanName(source.viewGdiDeviceName);
        }

        private static bool TryGetTargetName(DisplayConfig.DISPLAYCONFIG_PATH_INFO path,
            out DisplayConfig.DISPLAYCONFIG_TARGET_DEVICE_NAME target)
        {
            target = new DisplayConfig.DISPLAYCONFIG_TARGET_DEVICE_NAME();
            target.header.type = DisplayConfig.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfig.DISPLAYCONFIG_TARGET_DEVICE_NAME));
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;
            int error = DisplayConfig.DisplayConfigGetDeviceInfo(ref target);
            return error == 0;
        }

        private static bool TryGetHdr(DisplayConfig.DISPLAYCONFIG_PATH_INFO path)
        {
            try
            {
                DisplayConfig.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info = new DisplayConfig.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                info.header.type = DisplayConfig.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                info.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfig.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
                info.header.adapterId = path.targetInfo.adapterId;
                info.header.id = path.targetInfo.id;
                int error = DisplayConfig.DisplayConfigGetDeviceInfo(ref info);
                if (error != 0)
                {
                    return false;
                }
                // bit 0 = compatible con color avanzado, bit 1 = activo (HDR).
                return (info.value & 0x2) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static MonitorKind Classify(DisplayConfig.DISPLAYCONFIG_PATH_INFO path, bool remote)
        {
            if (remote)
            {
                return MonitorKind.Virtual;
            }
            if ((int)path.targetInfo.outputTechnology == DisplayConfig.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_FORCE_UINT32)
            {
                return MonitorKind.Virtual;
            }
            if (DisplayConfig.IsInternalTechnology(path.targetInfo.outputTechnology))
            {
                return MonitorKind.Internal;
            }
            return MonitorKind.External;
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.Replace("\0", string.Empty).Trim();
        }

        /// <summary>Extrae el código PNP ("DEL", "VIE"...) de MONITOR\VIE2700\...</summary>
        private static string ManufacturerFromDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return string.Empty;
            }
            const string prefix = "MONITOR\\";
            if (!deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            string rest = deviceId.Substring(prefix.Length);
            int slash = rest.IndexOf('\\');
            string model = slash > 0 ? rest.Substring(0, slash) : rest;
            if (model.Length < 3)
            {
                return string.Empty;
            }
            return model.Substring(0, 3).ToUpperInvariant();
        }

        private static PixelRect ToRect(Win32.RECT rect)
        {
            return new PixelRect(rect.Left, rect.Top,
                Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        private static int ToDegrees(int orientation)
        {
            switch (orientation)
            {
                case Win32.DMDO_90: return 90;
                case Win32.DMDO_180: return 180;
                case Win32.DMDO_270: return 270;
                default: return 0;
            }
        }
    }
}
