using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Mithor.Setup
{
    /// <summary>Creación y borrado de accesos directos (.lnk) sin dependencias externas.</summary>
    internal static class Shortcuts
    {
        public static string StartMenuPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    "Programs", "MITHOR.lnk");
            }
        }

        public static string DesktopPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    "MITHOR.lnk");
            }
        }

        public static void Create(string shortcutPath, string targetPath, string arguments,
            string description, string iconPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    throw new InvalidOperationException("WScript.Shell no está disponible.");
                }
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { targetPath });
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments ?? string.Empty });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description ?? string.Empty });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link,
                    new object[] { Path.GetDirectoryName(targetPath) });
                if (!string.IsNullOrEmpty(iconPath))
                {
                    linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link,
                        new object[] { iconPath + ",0" });
                }
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);

                Marshal.ReleaseComObject(link);
                Marshal.ReleaseComObject(shell);
            }
            catch (Exception ex)
            {
                InstallEngine.Log("No se pudo crear el acceso directo " + shortcutPath + ": " + ex.Message);
            }
        }

        public static void Remove(string shortcutPath)
        {
            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch (Exception ex)
            {
                InstallEngine.Log("No se pudo borrar el acceso directo " + shortcutPath + ": " + ex.Message);
            }
        }
    }
}
