using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Mithor.App.Ui;
using Mithor.Core.Abstractions;
using Mithor.Core.Config;
using Mithor.Core.Services;
using Mithor.Core.Util;
using Mithor.Platform.Windows;
using Mithor.Platform.Windows.Backends;

namespace Mithor.App.Cli
{
    /// <summary>Autocomprobación de la instalación: no deja el brillo modificado.</summary>
    internal static class SelfTest
    {
        private sealed class Reporter
        {
            public int Failures;
            public readonly List<string> Lines = new List<string>();

            public void Ok(string name)
            {
                Lines.Add("[ OK  ] " + name);
            }

            public void Fail(string name, string detail)
            {
                Failures++;
                Lines.Add("[FALLO] " + name + (string.IsNullOrEmpty(detail) ? string.Empty : " · " + detail));
            }

            public void Info(string text)
            {
                Lines.Add("        " + text);
            }
        }

        public static int Run(
            BrightnessController controller,
            WindowsBackendProvider provider,
            AppSettings settings,
            FileSettingsStore store)
        {
            Reporter report = new Reporter();
            ConsoleBridge.WriteLine(string.Empty);
            ConsoleBridge.WriteLine("MITHOR · autocomprobación");
            ConsoleBridge.WriteLine(new string('-', 76));

            TestJson(report);
            TestPercent(report);
            TestSettings(report);

            IList<DisplayStatus> statuses = controller.Snapshot();
            if (statuses.Count > 0)
            {
                report.Ok("Enumeración de pantallas: " + statuses.Count + " detectada(s)");
            }
            else
            {
                report.Fail("Enumeración de pantallas", "no se detectó ninguna");
            }

            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                if (status.Display == null)
                {
                    continue;
                }
                report.Info(string.Format("#{0} {1} · {2} · {3} % · {4}",
                    status.Display.MonitorIndex,
                    status.Display.FriendlyName,
                    status.BackendLabel,
                    status.EffectivePercent,
                    status.Display.DisplaySize));
            }

            TestProbes(report, provider, statuses);
            TestOverlay(report, provider, statuses);

            for (int i = 0; i < report.Lines.Count; i++)
            {
                ConsoleBridge.WriteLine(report.Lines[i]);
            }
            ConsoleBridge.WriteLine(new string('-', 76));
            if (report.Failures == 0)
            {
                ConsoleBridge.WriteLine("Resultado: todo correcto.");
            }
            else
            {
                ConsoleBridge.WriteLine("Resultado: " + report.Failures + " comprobación(es) fallida(s).");
            }
            return report.Failures == 0 ? 0 : 1;
        }

        private static void TestJson(Reporter report)
        {
            try
            {
                JsonValue root = JsonValue.NewObject();
                root.Set("nombre", JsonValue.From("MITHOR \"prueba\" \\ español"));
                root.Set("valor", JsonValue.From(42));
                root.Set("activo", JsonValue.From(true));
                JsonValue list = JsonValue.NewArray();
                list.Add(JsonValue.From("uno"));
                list.Add(JsonValue.From(2.5));
                list.Add(JsonValue.NewObject());
                root.Set("lista", list);

                string text = root.ToJson(true);
                JsonValue back = JsonValue.Parse(text);
                bool ok = back["nombre"].AsString(string.Empty) == "MITHOR \"prueba\" \\ español"
                    && back["valor"].AsInt(0) == 42
                    && back["activo"].AsBool(false)
                    && back["lista"].Count == 3
                    && back["lista"][0].AsString(string.Empty) == "uno"
                    && Math.Abs(back["lista"][1].AsDouble(0) - 2.5) < 0.0001;
                if (ok)
                {
                    report.Ok("JSON: serialización y lectura");
                }
                else
                {
                    report.Fail("JSON: serialización y lectura", "los valores no coinciden");
                }
            }
            catch (Exception ex)
            {
                report.Fail("JSON: serialización y lectura", ex.Message);
            }
        }

        private static void TestPercent(Reporter report)
        {
            BackendProbe wide = BackendProbe.SupportedRange(0, 255, 128, true, string.Empty);
            BackendProbe narrow = BackendProbe.SupportedRange(20, 80, 50, true, string.Empty);
            bool ok = Math.Abs(wide.ToPercent(128) - 50) <= 1
                && wide.ToRaw(50) == 128
                && narrow.ToRaw(0) == 20
                && narrow.ToRaw(100) == 80
                && narrow.ToPercent(20) == 0
                && narrow.ToPercent(80) == 100;
            if (ok)
            {
                report.Ok("Conversión de porcentaje y rango del hardware");
            }
            else
            {
                report.Fail("Conversión de porcentaje y rango del hardware", "resultado inesperado");
            }
        }

        private static void TestSettings(Reporter report)
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            string path = Path.Combine(AppPaths.DataDirectory,
                "selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                AppSettings original = new AppSettings();
                original.StepPercent = 7;
                original.OverlayMaxOpacity = 0.75;
                original.DefaultBackend = "gamma";
                original.ForDisplay("pantalla-prueba").Brightness = 42;
                FileSettingsStore temporary = new FileSettingsStore(path);
                original.Save(temporary);

                AppSettings loaded = AppSettings.Load(temporary);
                bool ok = loaded.StepPercent == 7
                    && Math.Abs(loaded.OverlayMaxOpacity - 0.75) < 0.001
                    && string.Equals(loaded.DefaultBackend, "gamma", StringComparison.OrdinalIgnoreCase)
                    && loaded.ForDisplay("pantalla-prueba").Brightness == 42;
                if (ok)
                {
                    report.Ok("Configuración: guardar y recuperar");
                }
                else
                {
                    report.Fail("Configuración: guardar y recuperar",
                        string.Format("paso={0} opacidad={1} método={2} brillo={3}",
                            loaded.StepPercent, loaded.OverlayMaxOpacity, loaded.DefaultBackend,
                            loaded.ForDisplay("pantalla-prueba").Brightness));
                }
            }
            catch (Exception ex)
            {
                report.Fail("Configuración: guardar y recuperar", ex.Message);
            }
            finally
            {
                try { if (File.Exists(path)) { File.Delete(path); } }
                catch { }
                try { if (File.Exists(path + ".bak")) { File.Delete(path + ".bak"); } }
                catch { }
            }
        }

        private static void TestProbes(Reporter report, WindowsBackendProvider provider, IList<DisplayStatus> statuses)
        {
            if (statuses.Count == 0)
            {
                return;
            }
            report.Info(string.Empty);
            report.Info("Sondeo de métodos (solo lectura):");
            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                if (status.Display == null)
                {
                    continue;
                }
                IList<IBrightnessBackend> candidates = provider.GetCandidates(status.Display);
                for (int j = 0; j < candidates.Count; j++)
                {
                    IBrightnessBackend backend = candidates[j];
                    try
                    {
                        BackendProbe probe = backend.Probe(status.Display);
                        if (probe.Supported)
                        {
                            report.Info(string.Format("  [{0}] {1}: {2}", status.Display.MonitorIndex, backend.Label, probe.Describe()));
                        }
                        else
                        {
                            report.Info(string.Format("  [{0}] {1}: no compatible · {2}",
                                status.Display.MonitorIndex, backend.Label, probe.Reason));
                        }
                    }
                    catch (Exception ex)
                    {
                        report.Info(string.Format("  [{0}] {1}: error · {2}",
                            status.Display.MonitorIndex, backend.Label, ex.Message));
                    }
                }
            }
        }

        private static void TestOverlay(Reporter report, WindowsBackendProvider provider, IList<DisplayStatus> statuses)
        {
            if (statuses.Count == 0 || statuses[0].Display == null)
            {
                return;
            }
            DisplayStatus first = statuses[0];
            try
            {
                OverlayBackend overlay = provider.Overlay;
                BackendProbe probe = overlay.Probe(first.Display);
                if (!probe.Supported)
                {
                    report.Fail("Superposición universal", probe.Reason);
                    return;
                }
                int restore = first.BackendId == "overlay" ? first.EffectivePercent : 100;
                bool wrote = overlay.TryWrite(first.Display, 85);
                Thread.Sleep(400);
                int windows = overlay.WindowCount;
                overlay.TryWrite(first.Display, restore);
                Thread.Sleep(120);
                if (wrote && windows > 0)
                {
                    report.Ok("Superposición universal: ventana creada y retirada");
                }
                else
                {
                    report.Fail("Superposición universal",
                        "wrote=" + wrote + " ventanas=" + windows);
                }
            }
            catch (Exception ex)
            {
                report.Fail("Superposición universal", ex.Message);
            }
        }
    }
}
