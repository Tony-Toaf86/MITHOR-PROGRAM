using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Mithor.App.Ui;
using Mithor.Core.Config;
using Mithor.Core.Model;
using Mithor.Core.Services;
using Mithor.Core.Util;
using Mithor.Platform.Windows;
using Mithor.Platform.Windows.Displays;
using Mithor.Platform.Windows.SystemIntegration;

namespace Mithor.App.Cli
{
    /// <summary>Interfaz de línea de comandos de MITHOR (misma lógica que la interfaz gráfica).</summary>
    internal static class CliRunner
    {
        private sealed class Options
        {
            public string Command;
            public string ValueText;
            public string Display;
            public string Backend;
            public bool Json;
            public bool Verbose;
            public bool Help;
            public bool Version;
        }

        public static bool IsCliInvocation(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "--list" || arg == "-l" || arg == "--get" || arg == "--set"
                    || arg == "--step" || arg == "--refresh" || arg == "--diagnose"
                    || arg == "--selftest" || arg == "--help" || arg == "-h"
                    || arg == "/?" || arg == "--version" || arg == "--json")
                {
                    return true;
                }
            }
            return false;
        }

        public static int Run(string[] args)
        {
            Options options = Parse(args);
            ConsoleBridge.Attach();
            AppPaths.EnsureCreated();
            Log.Init(AppPaths.LogDirectory, options.Verbose);

            if (options.Version)
            {
                ConsoleBridge.WriteLine("MITHOR " + AppInfo.Version);
                return 0;
            }
            if (options.Help || string.IsNullOrEmpty(options.Command))
            {
                PrintHelp();
                return options.Help ? 0 : 2;
            }

            FileSettingsStore store = new FileSettingsStore(AppPaths.SettingsFile);
            AppSettings settings = AppSettings.Load(store);
            settings.RestoreOnStartup = false;

            string originalDefault = settings.DefaultBackend;
            Dictionary<string, string> originalOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(options.Backend))
            {
                if (!IsKnownBackend(options.Backend))
                {
                    ConsoleBridge.WriteError("Método desconocido: " + options.Backend
                        + " (usa auto, ddc, wmi, gamma u overlay).");
                    return 2;
                }
                foreach (KeyValuePair<string, DisplaySettings> pair in settings.Displays)
                {
                    originalOverrides[pair.Key] = pair.Value.Backend;
                    pair.Value.Backend = "auto";
                }
                settings.DefaultBackend = options.Backend;
            }

            WindowsBackendProvider provider = new WindowsBackendProvider();
            WindowsDisplayEnumerator enumerator = new WindowsDisplayEnumerator();
            BrightnessController controller = new BrightnessController(provider, enumerator, settings, store, provider.Overlay);

            try
            {
                controller.Start();
                if (!controller.WaitForInitialScan(30000))
                {
                    ConsoleBridge.WriteError("No se pudo completar la detección de pantallas a tiempo.");
                    return 1;
                }

                switch (options.Command)
                {
                    case "list":
                    case "get":
                        return PrintStatuses(controller.Snapshot(), options.Json);

                    case "set":
                        return RunSet(controller, options);

                    case "step":
                        return RunStep(controller, options);

                    case "refresh":
                        controller.RequestReadAll();
                        WaitIdle(controller, 8000);
                        return PrintStatuses(controller.Snapshot(), options.Json);

                    case "diagnose":
                        ConsoleBridge.WriteLine(Diagnostics.Build(controller, settings, provider));
                        return 0;

                    case "selftest":
                        return SelfTest.Run(controller, provider, settings, store);

                    default:
                        PrintHelp();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error en la CLI", ex);
                ConsoleBridge.WriteError("Error: " + ex.Message);
                return 1;
            }
            finally
            {
                settings.DefaultBackend = originalDefault;
                foreach (KeyValuePair<string, string> pair in originalOverrides)
                {
                    DisplaySettings displaySettings;
                    if (settings.Displays.TryGetValue(pair.Key, out displaySettings))
                    {
                        displaySettings.Backend = pair.Value;
                    }
                }
                controller.Shutdown();
            }
        }

        private static int RunSet(BrightnessController controller, Options options)
        {
            int value;
            if (!int.TryParse(options.ValueText, out value)
                || value < BrightnessRange.Minimum || value > BrightnessRange.Maximum)
            {
                ConsoleBridge.WriteError("--set necesita un valor entre -100 y 100 (los valores negativos aplican atenuación extra).");
                return 2;
            }

            IList<DisplayStatus> targets = Filter(controller.Snapshot(), options.Display);
            if (targets.Count == 0)
            {
                ConsoleBridge.WriteError("Ninguna pantalla coincide con el filtro indicado.");
                return 2;
            }
            for (int i = 0; i < targets.Count; i++)
            {
                controller.SetBrightness(targets[i].Id, value);
            }
            WaitIdle(controller, 10000);
            return PrintStatuses(controller.Snapshot(), options.Json);
        }

        private static int RunStep(BrightnessController controller, Options options)
        {
            int delta;
            if (!int.TryParse(options.ValueText, out delta) || delta == 0
                || delta < BrightnessRange.Minimum || delta > BrightnessRange.Maximum)
            {
                ConsoleBridge.WriteError("--step necesita un incremento entre -100 y 100 distinto de cero, por ejemplo --step -5.");
                return 2;
            }

            if (string.IsNullOrEmpty(options.Display))
            {
                controller.Step(delta);
            }
            else
            {
                IList<DisplayStatus> targets = Filter(controller.Snapshot(), options.Display);
                if (targets.Count == 0)
                {
                    ConsoleBridge.WriteError("Ninguna pantalla coincide con el filtro indicado.");
                    return 2;
                }
                for (int i = 0; i < targets.Count; i++)
                {
                    controller.SetBrightness(targets[i].Id, targets[i].EffectivePercent + delta);
                }
            }
            WaitIdle(controller, 10000);
            return PrintStatuses(controller.Snapshot(), options.Json);
        }

        private static IList<DisplayStatus> Filter(IList<DisplayStatus> statuses, string filter)
        {
            List<DisplayStatus> result = new List<DisplayStatus>();
            if (statuses == null)
            {
                return result;
            }
            if (string.IsNullOrEmpty(filter))
            {
                for (int i = 0; i < statuses.Count; i++)
                {
                    result.Add(statuses[i]);
                }
                return result;
            }

            int index;
            bool byIndex = int.TryParse(filter, out index);
            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                if (status.Display == null)
                {
                    continue;
                }
                if (byIndex)
                {
                    if (status.Display.MonitorIndex == index)
                    {
                        result.Add(status);
                    }
                    continue;
                }
                if (Contains(status.Display.FriendlyName, filter)
                    || Contains(status.Display.DeviceName, filter)
                    || Contains(status.Display.Id, filter))
                {
                    result.Add(status);
                }
            }
            return result;
        }

        private static bool Contains(string text, string filter)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void WaitIdle(BrightnessController controller, int timeoutMilliseconds)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < limit)
            {
                bool pending = false;
                IList<DisplayStatus> statuses = controller.Snapshot();
                for (int i = 0; i < statuses.Count; i++)
                {
                    if (statuses[i].Pending)
                    {
                        pending = true;
                        break;
                    }
                }
                if (!pending)
                {
                    break;
                }
                Thread.Sleep(60);
            }
            Thread.Sleep(60);
        }

        private static int PrintStatuses(IList<DisplayStatus> statuses, bool json)
        {
            if (json)
            {
                PrintJson(statuses);
            }
            else
            {
                PrintTable(statuses);
            }
            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Failed)
                {
                    return 1;
                }
            }
            return 0;
        }

        private static void PrintTable(IList<DisplayStatus> statuses)
        {
            ConsoleBridge.WriteLine(string.Empty);
            ConsoleBridge.WriteLine(string.Format("MITHOR {0} · {1} pantalla(s)", AppInfo.Version, statuses.Count));
            ConsoleBridge.WriteLine(string.Empty);
            ConsoleBridge.WriteLine(string.Format("{0}  {1,-32} {2,-20} {3,7}  {4}",
                "#", "Pantalla", "Método", "Brillo", "Resolución"));
            ConsoleBridge.WriteLine(new string('-', 92));

            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                if (status.Display == null)
                {
                    continue;
                }
                string name = status.Display.FriendlyName;
                if (!string.IsNullOrEmpty(status.Display.Manufacturer)
                    && name.IndexOf(status.Display.Manufacturer, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    name += " (" + status.Display.Manufacturer + ")";
                }
                string method = status.BackendLabel;
                if (status.Display.IsHdrActive)
                {
                    method += " · HDR";
                }
                string brightness = status.Backend == null ? "—" : status.EffectivePercent + " %";
                string bounds = status.Display.DisplaySize + " en " + status.Display.Bounds.X + "," + status.Display.Bounds.Y;

                ConsoleBridge.WriteLine(string.Format("{0,2}  {1,-32} {2,-20} {3,7}  {4}",
                    status.Display.MonitorIndex,
                    Truncate(name, 32),
                    Truncate(method, 20),
                    brightness,
                    bounds));

                if (status.Failed && !string.IsNullOrEmpty(status.FailureReason))
                {
                    ConsoleBridge.WriteLine("     └─ " + status.FailureReason);
                }
            }
            ConsoleBridge.WriteLine(string.Empty);
        }

        private static void PrintJson(IList<DisplayStatus> statuses)
        {
            JsonValue array = JsonValue.NewArray();
            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                if (status.Display == null)
                {
                    continue;
                }
                JsonValue item = JsonValue.NewObject();
                item.Set("index", JsonValue.From(status.Display.MonitorIndex));
                item.Set("name", JsonValue.From(status.Display.FriendlyName));
                item.Set("manufacturer", JsonValue.From(status.Display.Manufacturer));
                item.Set("id", JsonValue.From(status.Display.Id));
                item.Set("device", JsonValue.From(status.Display.DeviceName));
                item.Set("technology", JsonValue.From(status.Display.OutputTechnology));
                item.Set("internal", JsonValue.From(status.Display.IsInternal));
                item.Set("virtual", JsonValue.From(status.Display.IsVirtual));
                item.Set("hdr", JsonValue.From(status.Display.IsHdrActive));
                item.Set("backend", JsonValue.From(status.BackendId));
                item.Set("backendLabel", JsonValue.From(status.BackendLabel));
                item.Set("brightness", JsonValue.From(status.EffectivePercent));
                item.Set("hardwareBrightness", JsonValue.From(BrightnessRange.HardwarePart(status.EffectivePercent)));
                item.Set("extraDim", JsonValue.From(BrightnessRange.ExtraDimPart(status.EffectivePercent)));
                item.Set("pending", JsonValue.From(status.Pending));
                item.Set("failed", JsonValue.From(status.Failed));
                item.Set("error", JsonValue.From(status.FailureReason));

                JsonValue bounds = JsonValue.NewObject();
                bounds.Set("x", JsonValue.From(status.Display.Bounds.X));
                bounds.Set("y", JsonValue.From(status.Display.Bounds.Y));
                bounds.Set("width", JsonValue.From(status.Display.Bounds.Width));
                bounds.Set("height", JsonValue.From(status.Display.Bounds.Height));
                item.Set("bounds", bounds);

                array.Add(item);
            }
            ConsoleBridge.WriteLine(array.ToJson(true));
        }

        private static string Truncate(string text, int length)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            if (text.Length <= length)
            {
                return text;
            }
            return text.Substring(0, Math.Max(0, length - 1)) + "…";
        }

        private static bool IsKnownBackend(string backend)
        {
            return string.Equals(backend, "auto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(backend, "ddc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(backend, "wmi", StringComparison.OrdinalIgnoreCase)
                || string.Equals(backend, "gamma", StringComparison.OrdinalIgnoreCase)
                || string.Equals(backend, "overlay", StringComparison.OrdinalIgnoreCase);
        }

        private static Options Parse(string[] args)
        {
            Options options = new Options();
            if (args == null)
            {
                return options;
            }
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                switch (arg)
                {
                    case "--list":
                    case "-l":
                        options.Command = "list";
                        break;
                    case "--get":
                        options.Command = "get";
                        break;
                    case "--set":
                        options.Command = "set";
                        options.ValueText = Next(args, ref i);
                        break;
                    case "--step":
                        options.Command = "step";
                        options.ValueText = Next(args, ref i);
                        break;
                    case "--refresh":
                        options.Command = "refresh";
                        break;
                    case "--diagnose":
                        options.Command = "diagnose";
                        break;
                    case "--selftest":
                        options.Command = "selftest";
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    case "--display":
                        options.Display = Next(args, ref i);
                        break;
                    case "--backend":
                        options.Backend = Next(args, ref i);
                        break;
                    case "--verbose":
                    case "-v":
                        options.Verbose = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;
                    case "--version":
                        options.Version = true;
                        break;
                    case "--tray":
                    case "--minimized":
                        break;
                    default:
                        if (arg.StartsWith("-"))
                        {
                            ConsoleBridge.WriteError("Opción desconocida: " + args[i]);
                            options.Help = true;
                        }
                        break;
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                return null;
            }
            return args[++index];
        }

        private static void PrintHelp()
        {
            ConsoleBridge.WriteLine("MITHOR " + AppInfo.Version + " · Control de brillo universal para Windows");
            ConsoleBridge.WriteLine(string.Empty);
            ConsoleBridge.WriteLine("Uso:");
            ConsoleBridge.WriteLine("  MITHOR.exe                      Abre la interfaz gráfica");
            ConsoleBridge.WriteLine("  MITHOR.exe --tray               Abre directamente en la bandeja");
            ConsoleBridge.WriteLine("  MITHOR.exe --list [--json]      Lista las pantallas y su brillo");
            ConsoleBridge.WriteLine("  MITHOR.exe --set 65             Fija el brillo de todas las pantallas");
            ConsoleBridge.WriteLine("  MITHOR.exe --set -30            Baja por debajo del mínimo del monitor");
            ConsoleBridge.WriteLine("  MITHOR.exe --set 40 --display 2 Fija el brillo de una pantalla");
            ConsoleBridge.WriteLine("  MITHOR.exe --step -5            Baja 5 puntos cada pantalla");
            ConsoleBridge.WriteLine("  MITHOR.exe --refresh            Relee el brillo real del hardware");
            ConsoleBridge.WriteLine("  MITHOR.exe --diagnose           Informe completo de diagnóstico");
            ConsoleBridge.WriteLine("  MITHOR.exe --selftest           Autocomprobación (no cambia el brillo)");
            ConsoleBridge.WriteLine(string.Empty);
            ConsoleBridge.WriteLine("Opciones:");
            ConsoleBridge.WriteLine("  --backend <auto|ddc|wmi|gamma|overlay>   Fuerza el método de control");
            ConsoleBridge.WriteLine("  --display <número|nombre|id>            Selecciona una pantalla");
            ConsoleBridge.WriteLine("  --json                                  Salida legible por máquina");
            ConsoleBridge.WriteLine("  --verbose                               Registro detallado");
            ConsoleBridge.WriteLine(string.Empty);
            ConsoleBridge.WriteLine("Atajos globales: Ctrl+Alt+↑ / Ctrl+Alt+↓ / Ctrl+Alt+1 (100 %)");
        }
    }
}

