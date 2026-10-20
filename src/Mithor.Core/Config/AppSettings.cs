using System;
using System.Collections.Generic;
using System.Text;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Core.Config
{
    /// <summary>Preferencias guardadas para una pantalla concreta.</summary>
    public sealed class DisplaySettings
    {
        /// <summary>Valor centinela para "todavía no hay brillo guardado".</summary>
        public const int UnknownBrightness = int.MinValue;

        public string Backend { get; set; }

        /// <summary>Último brillo conocido (-100..100). UnknownBrightness = desconocido.</summary>
        public int Brightness { get; set; }

        public DisplaySettings()
        {
            Backend = "auto";
            Brightness = UnknownBrightness;
        }

        public bool HasBrightness
        {
            get
            {
                return Brightness >= BrightnessRange.Minimum
                    && Brightness <= BrightnessRange.Maximum;
            }
        }
    }

    /// <summary>Configuración persistente de MITHOR.</summary>
    public sealed class AppSettings
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; }
        public bool RestoreOnStartup { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; }
        public bool HotkeysEnabled { get; set; }
        public bool ShowTrayIcon { get; set; }
        public bool VerboseLog { get; set; }
        public int StepPercent { get; set; }
        public double OverlayMaxOpacity { get; set; }
        public string DefaultBackend { get; set; }
        public Dictionary<string, DisplaySettings> Displays { get; set; }

        public AppSettings()
        {
            Version = CurrentVersion;
            RestoreOnStartup = true;
            StartWithWindows = false;
            StartMinimized = false;
            HotkeysEnabled = true;
            ShowTrayIcon = true;
            VerboseLog = false;
            StepPercent = 5;
            OverlayMaxOpacity = 0.92;
            DefaultBackend = "auto";
            Displays = new Dictionary<string, DisplaySettings>(StringComparer.OrdinalIgnoreCase);
        }

        public DisplaySettings ForDisplay(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                id = "desconocida";
            }
            DisplaySettings settings;
            if (!Displays.TryGetValue(id, out settings))
            {
                settings = new DisplaySettings();
                Displays[id] = settings;
            }
            return settings;
        }

        public void Normalize()
        {
            if (StepPercent < 1) { StepPercent = 1; }
            if (StepPercent > 50) { StepPercent = 50; }
            if (OverlayMaxOpacity < 0.30) { OverlayMaxOpacity = 0.30; }
            if (OverlayMaxOpacity > 0.98) { OverlayMaxOpacity = 0.98; }
            if (string.IsNullOrEmpty(DefaultBackend)) { DefaultBackend = "auto"; }
            if (Displays == null)
            {
                Displays = new Dictionary<string, DisplaySettings>(StringComparer.OrdinalIgnoreCase);
            }
            foreach (KeyValuePair<string, DisplaySettings> pair in Displays)
            {
                if (pair.Value == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(pair.Value.Backend))
                {
                    pair.Value.Backend = "auto";
                }
                if (pair.Value.Brightness != DisplaySettings.UnknownBrightness)
                {
                    if (pair.Value.Brightness < BrightnessRange.Minimum)
                    {
                        pair.Value.Brightness = BrightnessRange.Minimum;
                    }
                    if (pair.Value.Brightness > BrightnessRange.Maximum)
                    {
                        pair.Value.Brightness = BrightnessRange.Maximum;
                    }
                }
            }
        }

        public static AppSettings Load(ISettingsStore store)
        {
            AppSettings settings = new AppSettings();
            if (store == null)
            {
                return settings;
            }
            try
            {
                string text = store.Read();
                if (string.IsNullOrEmpty(text))
                {
                    return settings;
                }
                JsonValue root = JsonValue.Parse(text);
                settings.Version = root["version"].AsInt(CurrentVersion);
                settings.RestoreOnStartup = root["restoreOnStartup"].AsBool(settings.RestoreOnStartup);
                settings.StartWithWindows = root["startWithWindows"].AsBool(settings.StartWithWindows);
                settings.StartMinimized = root["startMinimized"].AsBool(settings.StartMinimized);
                settings.HotkeysEnabled = root["hotkeysEnabled"].AsBool(settings.HotkeysEnabled);
                settings.ShowTrayIcon = root["showTrayIcon"].AsBool(settings.ShowTrayIcon);
                settings.VerboseLog = root["verboseLog"].AsBool(settings.VerboseLog);
                settings.StepPercent = root["stepPercent"].AsInt(settings.StepPercent);
                settings.OverlayMaxOpacity = root["overlayMaxOpacity"].AsDouble(settings.OverlayMaxOpacity);
                settings.DefaultBackend = root["defaultBackend"].AsString(settings.DefaultBackend);

                JsonValue displays = root["displays"];
                if (displays.IsObject)
                {
                    foreach (KeyValuePair<string, JsonValue> pair in displays.Members)
                    {
                        DisplaySettings ds = new DisplaySettings();
                        ds.Backend = pair.Value["backend"].AsString("auto");
                        ds.Brightness = pair.Value["brightness"].AsInt(DisplaySettings.UnknownBrightness);
                        settings.Displays[pair.Key] = ds;
                    }
                }
                settings.Normalize();
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo leer la configuración (" + store.Describe() + "): " + Log.Describe(ex));
                settings.Normalize();
            }
            return settings;
        }

        public void Save(ISettingsStore store)
        {
            if (store == null)
            {
                return;
            }
            try
            {
                Normalize();
                JsonValue root = JsonValue.NewObject();
                root.Set("version", JsonValue.From(Version));
                root.Set("restoreOnStartup", JsonValue.From(RestoreOnStartup));
                root.Set("startWithWindows", JsonValue.From(StartWithWindows));
                root.Set("startMinimized", JsonValue.From(StartMinimized));
                root.Set("hotkeysEnabled", JsonValue.From(HotkeysEnabled));
                root.Set("showTrayIcon", JsonValue.From(ShowTrayIcon));
                root.Set("verboseLog", JsonValue.From(VerboseLog));
                root.Set("stepPercent", JsonValue.From(StepPercent));
                root.Set("overlayMaxOpacity", JsonValue.From(OverlayMaxOpacity));
                root.Set("defaultBackend", JsonValue.From(DefaultBackend));

                JsonValue displays = JsonValue.NewObject();
                foreach (KeyValuePair<string, DisplaySettings> pair in Displays)
                {
                    JsonValue item = JsonValue.NewObject();
                    item.Set("backend", JsonValue.From(pair.Value.Backend));
                    if (pair.Value.HasBrightness)
                    {
                        item.Set("brightness", JsonValue.From(pair.Value.Brightness));
                    }
                    displays.Set(pair.Key, item);
                }
                root.Set("displays", displays);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("// MITHOR: configuración generada automáticamente. Puedes editarla, pero");
                sb.AppendLine("// se sobrescribirá al cambiar el brillo desde la aplicación.");
                sb.AppendLine(root.ToJson(true));
                store.Write(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo guardar la configuración: " + Log.Describe(ex));
            }
        }
    }
}
