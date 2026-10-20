using System;
using System.Collections.Generic;
using System.IO;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Platform.Linux
{
    /// <summary>
    /// Enumeración de pantallas en Linux usando xrandr (X11) y, como respaldo,
    /// /sys/class/backlight. En Wayland la geometría se obtendrá del protocolo del
    /// compositor en una fase posterior (ver docs/ROADMAP-LINUX.md).
    /// </summary>
    public sealed class LinuxDisplayEnumerator : IDisplayEnumerator
    {
        private const string BacklightRoot = "/sys/class/backlight";

        public IList<DisplayInfo> Enumerate()
        {
            List<DisplayInfo> result = new List<DisplayInfo>();
            try
            {
                EnumerateWithXrandr(result);
            }
            catch (Exception ex)
            {
                Log.Warn("xrandr no disponible: " + Log.Describe(ex));
                result.Clear();
            }

            if (result.Count == 0)
            {
                EnumerateWithSysfs(result);
            }
            if (result.Count == 0)
            {
                DisplayInfo synthetic = new DisplayInfo();
                synthetic.Id = "linux-display-1";
                synthetic.DeviceName = "LVDS-1";
                synthetic.FriendlyName = "Pantalla del sistema";
                synthetic.Kind = MonitorKind.Unknown;
                synthetic.IsPrimary = true;
                synthetic.Bounds = new PixelRect(0, 0, 1920, 1080);
                synthetic.OutputTechnology = "Desconocido";
                synthetic.Notes = "No se pudo consultar xrandr ni sysfs.";
                result.Add(synthetic);
            }
            for (int i = 0; i < result.Count; i++)
            {
                result[i].MonitorIndex = i + 1;
            }
            return result;
        }

        private static void EnumerateWithXrandr(List<DisplayInfo> result)
        {
            string output = LinuxProcess.Run("xrandr", "--query", 3000);
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            string[] lines = output.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int connected = line.IndexOf(" connected", StringComparison.Ordinal);
                if (connected <= 0)
                {
                    continue;
                }
                string name = line.Substring(0, connected).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                int width;
                int height;
                int x;
                int y;
                if (!TryParseGeometry(line, out width, out height, out x, out y))
                {
                    continue;
                }

                DisplayInfo info = new DisplayInfo();
                info.Id = "x11:" + name;
                info.DeviceName = name;
                info.FriendlyName = name;
                info.Manufacturer = string.Empty;
                info.Bounds = new PixelRect(x, y, width, height);
                info.IsPrimary = line.IndexOf(" primary ", StringComparison.Ordinal) > 0;
                info.Kind = IsInternal(name) ? MonitorKind.Internal : MonitorKind.External;
                info.OutputTechnology = TechnologyOf(name);
                info.Notes = "Detectado con xrandr.";
                result.Add(info);
            }
        }

        private static void EnumerateWithSysfs(List<DisplayInfo> result)
        {
            try
            {
                if (!Directory.Exists(BacklightRoot))
                {
                    return;
                }
                string[] devices = Directory.GetDirectories(BacklightRoot);
                for (int i = 0; i < devices.Length; i++)
                {
                    string device = Path.GetFileName(devices[i]);
                    DisplayInfo info = new DisplayInfo();
                    info.Id = "sysfs:" + device;
                    info.DeviceName = device;
                    info.FriendlyName = "Panel (" + device + ")";
                    info.Kind = MonitorKind.Internal;
                    info.IsPrimary = i == 0;
                    info.Bounds = new PixelRect(0, 0, 1920, 1080);
                    info.OutputTechnology = "Panel interno";
                    info.Notes = "Detectado por /sys/class/backlight (sin servidor gráfico).";
                    result.Add(info);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo leer /sys/class/backlight: " + Log.Describe(ex));
            }
        }

        private static bool TryParseGeometry(string line, out int width, out int height, out int x, out int y)
        {
            width = 0;
            height = 0;
            x = 0;
            y = 0;

            string[] tokens = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int xIndex = token.IndexOf('x');
                int plusIndex = token.IndexOf('+');
                if (xIndex <= 0 || plusIndex <= xIndex)
                {
                    continue;
                }
                string widthText = token.Substring(0, xIndex);
                string heightText = token.Substring(xIndex + 1, plusIndex - xIndex - 1);
                int parseWidth;
                if (!int.TryParse(widthText, out parseWidth) || parseWidth <= 0)
                {
                    continue;
                }
                int parseHeight;
                if (!int.TryParse(heightText, out parseHeight) || parseHeight <= 0)
                {
                    continue;
                }

                width = parseWidth;
                height = parseHeight;

                int plus2 = token.IndexOf('+', plusIndex + 1);
                if (plus2 > plusIndex)
                {
                    x = LinuxProcess.ParseInt(token.Substring(plusIndex + 1, plus2 - plusIndex - 1), 0);
                    y = LinuxProcess.ParseInt(token.Substring(plus2 + 1), 0);
                }
                return true;
            }
            return false;
        }

        private static bool IsInternal(string name)
        {
            return name.StartsWith("eDP", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("DSI", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("DVI-I-0", StringComparison.OrdinalIgnoreCase);
        }

        private static string TechnologyOf(string name)
        {
            if (name.StartsWith("eDP", StringComparison.OrdinalIgnoreCase)) { return "eDP"; }
            if (name.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase)) { return "LVDS"; }
            if (name.StartsWith("DSI", StringComparison.OrdinalIgnoreCase)) { return "DSI"; }
            if (name.StartsWith("HDMI", StringComparison.OrdinalIgnoreCase)) { return "HDMI"; }
            if (name.StartsWith("DP", StringComparison.OrdinalIgnoreCase)) { return "DisplayPort"; }
            if (name.StartsWith("DVI", StringComparison.OrdinalIgnoreCase)) { return "DVI"; }
            if (name.StartsWith("VGA", StringComparison.OrdinalIgnoreCase)) { return "VGA"; }
            return "Desconocido";
        }
    }
}
