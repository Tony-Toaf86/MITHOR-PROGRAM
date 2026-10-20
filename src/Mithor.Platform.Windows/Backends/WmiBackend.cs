using System;
using System.Collections.Generic;
using System.Management;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Platform.Windows.Backends
{
    /// <summary>
    /// Brillo de paneles integrados mediante WMI (root\wmi). Es la vía que usan los
    /// portátiles, tabletas y equipos todo-en-uno: WmiMonitorBrightnessMethods
    /// WmiSetBrightness. No requiere permisos de administrador.
    /// </summary>
    public sealed class WmiBackend : IBrightnessBackend
    {
        private sealed class Target
        {
            public string InstanceName;
            public ManagementObject Methods;
            public int Current;
            public byte[] Levels;
        }

        private sealed class Record
        {
            public string InstanceName;
            public int Current;
            public byte[] Levels;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Target> _targets = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        private ManagementScope _scope;
        private string _scopeError;
        private DateTime _scopeFailedAt = DateTime.MinValue;

        public string Id { get { return "wmi"; } }
        public string Label { get { return "Panel interno (WMI)"; } }
        public string Description { get { return "Brillo del panel integrado mediante WMI (WmiMonitorBrightnessMethods)."; } }
        public int Priority { get { return 10; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        public BackendProbe Probe(DisplayInfo display)
        {
            if (!EnsureScope())
            {
                return BackendProbe.Unsupported("WMI no disponible: " + _scopeError);
            }

            List<Record> records = QueryBrightness();
            if (records.Count == 0)
            {
                return BackendProbe.Unsupported("el equipo no expone brillo interno por WMI");
            }

            Record match = Match(display, records);
            if (match == null)
            {
                return BackendProbe.Unsupported("el panel no aparece en WMI");
            }

            ManagementObject methods = FindMethods(match.InstanceName);
            if (methods == null)
            {
                return BackendProbe.Unsupported("WMI no expone WmiSetBrightness");
            }

            Target target = new Target();
            target.InstanceName = match.InstanceName;
            target.Methods = methods;
            target.Current = Clamp(match.Current);
            target.Levels = match.Levels;

            lock (_gate)
            {
                Target previous;
                if (_targets.TryGetValue(display.Id, out previous) && previous.Methods != null)
                {
                    try { previous.Methods.Dispose(); }
                    catch { }
                }
                _targets[display.Id] = target;
            }

            return BackendProbe.SupportedRange(0, 100, target.Current, true, "WMI · panel interno");
        }

        public int? TryRead(DisplayInfo display)
        {
            Target target = GetTarget(display);
            if (target == null)
            {
                return null;
            }
            return Clamp(target.Current);
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            Target target = GetTarget(display);
            if (target == null)
            {
                Probe(display);
                target = GetTarget(display);
                if (target == null)
                {
                    return false;
                }
            }

            byte value = SnapToLevels(BackendProbe.ClampPercent(percent), target.Levels);
            try
            {
                using (ManagementBaseObject inParameters = target.Methods.GetMethodParameters("WmiSetBrightness"))
                {
                    inParameters["Timeout"] = (uint)0;
                    inParameters["Brightness"] = value;
                    using (ManagementBaseObject outParameters = target.Methods.InvokeMethod("WmiSetBrightness", inParameters, null))
                    {
                        if (outParameters == null)
                        {
                            return false;
                        }
                    }
                }
                target.Current = value;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("WMI WmiSetBrightness falló en " + display.FriendlyName + ": " + Log.Describe(ex));
                lock (_gate)
                {
                    Target removed;
                    if (_targets.TryGetValue(display.Id, out removed))
                    {
                        _targets.Remove(display.Id);
                        if (removed.Methods != null)
                        {
                            try { removed.Methods.Dispose(); }
                            catch { }
                        }
                    }
                }
                return false;
            }
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
            lock (_gate)
            {
                foreach (KeyValuePair<string, Target> pair in _targets)
                {
                    if (pair.Value.Methods != null)
                    {
                        try { pair.Value.Methods.Dispose(); }
                        catch { }
                    }
                }
                _targets.Clear();
            }
        }

        public void Shutdown()
        {
            OnDisplaysChanged(null);
        }

        private Target GetTarget(DisplayInfo display)
        {
            lock (_gate)
            {
                Target target;
                if (_targets.TryGetValue(display.Id, out target))
                {
                    return target;
                }
            }
            return null;
        }

        private bool EnsureScope()
        {
            lock (_gate)
            {
                if (_scope != null)
                {
                    return true;
                }
                if (_scopeError != null && (DateTime.UtcNow - _scopeFailedAt).TotalSeconds < 20)
                {
                    return false;
                }
            }

            try
            {
                ManagementScope scope = new ManagementScope("\\\\.\\root\\wmi");
                scope.Options.Timeout = TimeSpan.FromSeconds(4);
                scope.Options.EnablePrivileges = true;
                scope.Connect();

                lock (_gate)
                {
                    _scope = scope;
                    _scopeError = null;
                }
                return true;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _scope = null;
                    _scopeError = ex.Message;
                    _scopeFailedAt = DateTime.UtcNow;
                }
                Log.Warn("WMI root\\wmi no disponible: " + Log.Describe(ex));
                return false;
            }
        }

        private List<Record> QueryBrightness()
        {
            List<Record> records = new List<Record>();
            ManagementScope scope;
            lock (_gate)
            {
                scope = _scope;
            }
            if (scope == null)
            {
                return records;
            }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    scope, new ObjectQuery("SELECT InstanceName, CurrentBrightness, Levels FROM WmiMonitorBrightness")))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(4);
                    searcher.Options.ReturnImmediately = true;
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementBaseObject item in results)
                        {
                            try
                            {
                                Record record = new Record();
                                record.InstanceName = Convert.ToString(item["InstanceName"]);
                                record.Current = item["CurrentBrightness"] == null
                                    ? 100
                                    : Convert.ToInt32(item["CurrentBrightness"]);
                                record.Levels = item["Levels"] as byte[];
                                if (!string.IsNullOrEmpty(record.InstanceName))
                                {
                                    records.Add(record);
                                }
                            }
                            finally
                            {
                                item.Dispose();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _scopeError = Log.Describe(ex);
                Log.Warn("Consulta WmiMonitorBrightness falló: " + _scopeError);
            }
            return records;
        }

        private ManagementObject FindMethods(string instanceName)
        {
            ManagementScope scope;
            lock (_gate)
            {
                scope = _scope;
            }
            if (scope == null)
            {
                return null;
            }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods")))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(4);
                    searcher.Options.ReturnImmediately = true;
                    using (ManagementObjectCollection results = searcher.Get())
                    {
                        foreach (ManagementBaseObject item in results)
                        {
                            string candidate = Convert.ToString(item["InstanceName"]);
                            ManagementObject methods = item as ManagementObject;
                            if (methods != null && string.Equals(candidate, instanceName, StringComparison.OrdinalIgnoreCase))
                            {
                                try { methods.Get(); }
                                catch { }
                                return methods;
                            }
                            item.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Consulta WmiMonitorBrightnessMethods falló: " + Log.Describe(ex));
            }
            return null;
        }

        private static Record Match(DisplayInfo display, List<Record> records)
        {
            if (records.Count == 1 && display.Kind != MonitorKind.External)
            {
                return records[0];
            }

            string uid = ExtractUid(display.ConnectorId);
            if (string.IsNullOrEmpty(uid))
            {
                uid = ExtractUid(display.DeviceId);
            }
            if (!string.IsNullOrEmpty(uid))
            {
                for (int i = 0; i < records.Count; i++)
                {
                    if (string.Equals(ExtractUid(records[i].InstanceName), uid, StringComparison.OrdinalIgnoreCase))
                    {
                        return records[i];
                    }
                }
            }

            if (display.Kind == MonitorKind.Internal)
            {
                return records[0];
            }
            return null;
        }

        /// <summary>Extrae el sufijo UID#### que comparten WMI y las rutas de monitor.</summary>
        internal static string ExtractUid(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            int index = text.IndexOf("UID", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }
            int start = index + 3;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }
            if (end == start)
            {
                return string.Empty;
            }
            return text.Substring(start, end - start);
        }

        private static byte SnapToLevels(int percent, byte[] levels)
        {
            if (levels == null || levels.Length == 0)
            {
                return (byte)percent;
            }
            int best = levels[0];
            int bestDelta = Math.Abs(best - percent);
            for (int i = 1; i < levels.Length; i++)
            {
                int delta = Math.Abs(levels[i] - percent);
                if (delta < bestDelta)
                {
                    best = levels[i];
                    bestDelta = delta;
                }
            }
            return (byte)best;
        }

        private static int Clamp(int value)
        {
            if (value < 0) { return 0; }
            if (value > 100) { return 100; }
            return value;
        }
    }
}
