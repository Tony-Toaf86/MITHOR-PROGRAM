using System;
using System.Collections.Generic;
using System.Threading;
using Mithor.Core.Abstractions;
using Mithor.Core.Config;
using Mithor.Core.Model;
using Mithor.Core.Util;

namespace Mithor.Core.Services
{
    /// <summary>
    /// Coordina la enumeración de pantallas, la elección del método de control y la
    /// aplicación del brillo. Todas las operaciones potencialmente lentas (DDC/CI, WMI)
    /// se ejecutan en un único hilo de trabajo, de modo que la interfaz nunca se bloquea
    /// y las llamadas al hardware quedan serializadas.
    /// </summary>
    public sealed class BrightnessController : IDisposable
    {
        private sealed class ProbeCacheEntry
        {
            public BackendProbe Probe;
            public DateTime When;
        }

        private readonly IBackendProvider _provider;
        private readonly IDisplayEnumerator _enumerator;
        private readonly AppSettings _settings;
        private readonly ISettingsStore _store;
        private readonly IDimLayer _dimLayer;

        private readonly object _gate = new object();
        private readonly object _saveGate = new object();
        private readonly List<DisplayStatus> _statuses = new List<DisplayStatus>();
        private readonly Dictionary<string, int> _pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProbeCacheEntry> _probeCache = new Dictionary<string, ProbeCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _initialScan = new ManualResetEvent(false);

        private Thread _worker;
        private volatile bool _stop;
        private volatile bool _rescanRequested = true;
        private volatile bool _readRequested;
        private volatile bool _firstScanCompleted;
        private DateTime _lastSave = DateTime.MinValue;
        private int _applyDepth;

        public BrightnessController(
            IBackendProvider provider,
            IDisplayEnumerator enumerator,
            AppSettings settings,
            ISettingsStore store,
            IDimLayer dimLayer)
        {
            if (provider == null) { throw new ArgumentNullException("provider"); }
            if (enumerator == null) { throw new ArgumentNullException("enumerator"); }
            if (settings == null) { throw new ArgumentNullException("settings"); }

            _provider = provider;
            _enumerator = enumerator;
            _settings = settings;
            _store = store;
            _dimLayer = dimLayer;
        }

        public event EventHandler DisplaysChanged;
        public event EventHandler<DisplayStatus> DisplayUpdated;
        public event EventHandler<string> Notice;

        public AppSettings Settings { get { return _settings; } }
        public bool FirstScanCompleted { get { return _firstScanCompleted; } }

        public int MasterPercent
        {
            get
            {
                int sum = 0;
                int count = 0;
                lock (_gate)
                {
                    for (int i = 0; i < _statuses.Count; i++)
                    {
                        DisplayStatus status = _statuses[i];
                        if (status.Backend == null) { continue; }
                        sum += status.EffectivePercent;
                        count++;
                    }
                }
                if (count == 0)
                {
                    return 100;
                }
                return BrightnessRange.Clamp((int)Math.Round(sum / (double)count));
            }
        }

        public void Start()
        {
            if (_worker != null)
            {
                return;
            }
            _stop = false;
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "MITHOR-control";
            _worker.Start();
        }

        public bool WaitForInitialScan(int timeoutMilliseconds)
        {
            return _initialScan.WaitOne(Math.Max(100, timeoutMilliseconds));
        }

        public IList<DisplayStatus> Snapshot()
        {
            lock (_gate)
            {
                return new List<DisplayStatus>(_statuses);
            }
        }

        public DisplayStatus Find(string displayId)
        {
            if (string.IsNullOrEmpty(displayId))
            {
                return null;
            }
            lock (_gate)
            {
                for (int i = 0; i < _statuses.Count; i++)
                {
                    DisplayStatus status = _statuses[i];
                    if (status.Display != null
                        && string.Equals(status.Display.Id, displayId, StringComparison.OrdinalIgnoreCase))
                    {
                        return status;
                    }
                }
            }
            return null;
        }

        public void RequestRescan()
        {
            _rescanRequested = true;
            _wake.Set();
        }

        public void RequestReadAll()
        {
            _readRequested = true;
            _wake.Set();
        }

        public void SetBrightness(string displayId, int percent)
        {
            Queue(displayId, BrightnessRange.Clamp(percent));
        }

        public void SetAll(int percent)
        {
            IList<DisplayStatus> snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Display != null)
                {
                    Queue(snapshot[i].Display.Id, BrightnessRange.Clamp(percent));
                }
            }
        }

        /// <summary>Ajusta cada pantalla respecto a su propio valor actual.</summary>
        public void Step(int delta)
        {
            IList<DisplayStatus> snapshot = Snapshot();
            for (int i = 0; i < snapshot.Count; i++)
            {
                DisplayStatus status = snapshot[i];
                if (status.Display == null)
                {
                    continue;
                }
                Queue(status.Display.Id, BrightnessRange.Clamp(status.EffectivePercent + delta));
            }
        }

        public void SelectBackend(string displayId, string backendId)
        {
            if (string.IsNullOrEmpty(displayId))
            {
                return;
            }
            DisplaySettings displaySettings = _settings.ForDisplay(displayId);
            displaySettings.Backend = string.IsNullOrEmpty(backendId) ? "auto" : backendId;
            SaveSettingsNow();
            lock (_gate)
            {
                List<string> keys = new List<string>();
                foreach (string key in _probeCache.Keys)
                {
                    if (key.EndsWith("|" + displayId, StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(key);
                    }
                }
                for (int i = 0; i < keys.Count; i++)
                {
                    _probeCache.Remove(keys[i]);
                }
            }
            _rescanRequested = true;
            _wake.Set();
        }

        public void Shutdown()
        {
            if (_stop)
            {
                return;
            }
            _stop = true;
            _wake.Set();

            Thread worker = _worker;
            if (worker != null && worker.IsAlive)
            {
                try { worker.Join(5000); }
                catch (Exception ex) { Log.Warn("Esperando al hilo de control: " + Log.Describe(ex)); }
            }
            _worker = null;

            SaveSettingsNow();

            IList<IBrightnessBackend> backends = _provider.All;
            for (int i = 0; i < backends.Count; i++)
            {
                try { backends[i].Shutdown(); }
                catch (Exception ex) { Log.Warn("Cerrando " + backends[i].Id + ": " + Log.Describe(ex)); }
            }
            _initialScan.Set();
        }

        public void Dispose()
        {
            Shutdown();
        }

        // ------------------------------------------------------------------
        //  Cola y aplicación
        // ------------------------------------------------------------------

        private void Queue(string displayId, int percent)
        {
            if (string.IsNullOrEmpty(displayId))
            {
                return;
            }
            percent = BrightnessRange.Clamp(percent);
            DisplayStatus status = Find(displayId);
            lock (_gate)
            {
                _pending[displayId] = percent;
                if (status != null)
                {
                    status.Pending = true;
                    status.PendingPercent = percent;
                    status.Failed = false;
                    status.FailureReason = null;
                }
            }
            _wake.Set();
            if (status != null)
            {
                RaiseUpdated(status);
            }
        }

        private bool HasPending()
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }

        private void WorkerLoop()
        {
            while (!_stop)
            {
                bool didWork = false;
                try
                {
                    if (_rescanRequested && !_stop)
                    {
                        _rescanRequested = false;
                        DoRescan();
                        didWork = true;
                    }
                    if (!_stop && HasPending())
                    {
                        DoApplyOne();
                        didWork = true;
                    }
                    if (!_stop && !didWork && _readRequested)
                    {
                        _readRequested = false;
                        DoReadAll();
                        didWork = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Fallo en el hilo de control", ex);
                    RaiseNotice("Error interno: " + ex.Message);
                }
                if (!didWork)
                {
                    _wake.WaitOne(120);
                }
            }
        }

        private void DoApplyOne()
        {
            string id = null;
            int percent = 0;
            DisplayStatus status = null;

            lock (_gate)
            {
                foreach (KeyValuePair<string, int> pair in _pending)
                {
                    id = pair.Key;
                    percent = pair.Value;
                    break;
                }
                if (id == null)
                {
                    return;
                }
                _pending.Remove(id);
                for (int i = 0; i < _statuses.Count; i++)
                {
                    if (_statuses[i].Display != null
                        && string.Equals(_statuses[i].Display.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        status = _statuses[i];
                        break;
                    }
                }
            }

            if (status == null)
            {
                return;
            }
            Apply(status, percent);
        }

        private void Apply(DisplayStatus status, int percent)
        {
            if (status == null || status.Display == null)
            {
                return;
            }
            percent = BrightnessRange.Clamp(percent);
            int hardwarePercent = BrightnessRange.HardwarePart(percent);
            int extraDim = BrightnessRange.ExtraDimPart(percent);

            if (status.Backend == null)
            {
                MarkFailed(status, "no hay ningún método de control disponible");
                return;
            }

            bool ok = false;
            string error = null;
            try
            {
                ok = status.Backend.TryWrite(status.Display, hardwarePercent);
                if (!ok)
                {
                    error = "el método " + status.Backend.Label + " rechazó el cambio";
                }
            }
            catch (Exception ex)
            {
                error = Log.Describe(ex);
            }

            if (ok)
            {
                ApplyExtraDim(status.Display, extraDim);
                lock (_gate)
                {
                    status.Percent = percent;
                    status.PendingPercent = percent;
                    status.Pending = false;
                    status.Failed = false;
                    status.FailureReason = null;
                }
                _failures[status.Display.Id] = 0;
                SaveBrightness(status.Display.Id, percent);
                RaiseUpdated(status);
                return;
            }

            int failures;
            _failures.TryGetValue(status.Display.Id, out failures);
            failures++;
            _failures[status.Display.Id] = failures;
            Log.Warn(string.Format("Fallo al aplicar {0}% en {1} con {2}: {3}",
                percent, status.Display.FriendlyName, status.Backend.Id, error));

            if (failures >= 2 && IsAutomatic(status.Display.Id) && _applyDepth < 2)
            {
                _applyDepth++;
                try
                {
                    if (TryRebind(status))
                    {
                        Apply(status, percent);
                        return;
                    }
                }
                finally
                {
                    _applyDepth--;
                }
            }

            MarkFailed(status, error);
        }

        private void ApplyExtraDim(DisplayInfo display, int extraDim)
        {
            if (_dimLayer == null || display == null)
            {
                return;
            }
            try
            {
                _dimLayer.SetExtraDim(display, extraDim);
            }
            catch (Exception ex)
            {
                Log.Warn("Atenuación extra en " + display.FriendlyName + ": " + Log.Describe(ex));
            }
        }

        private void MarkFailed(DisplayStatus status, string error)
        {
            lock (_gate)
            {
                status.Pending = false;
                status.Failed = true;
                status.FailureReason = error;
            }
            RaiseUpdated(status);
            RaiseNotice("No se pudo ajustar " + status.Display.FriendlyName + ": " + error);
        }

        private bool TryRebind(DisplayStatus status)
        {
            string currentId = status.Backend == null ? null : status.Backend.Id;
            IList<IBrightnessBackend> candidates = _provider.GetCandidates(status.Display);
            for (int i = 0; i < candidates.Count; i++)
            {
                IBrightnessBackend candidate = candidates[i];
                if (candidate == null || !candidate.IsPlatformSupported)
                {
                    continue;
                }
                if (currentId != null && string.Equals(candidate.Id, currentId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                BackendProbe probe = ProbeBackend(candidate, status.Display);
                if (!probe.Supported)
                {
                    continue;
                }
                Log.Info(string.Format("Reasignando {0}: {1} -> {2}", status.Display.FriendlyName, currentId, candidate.Id));
                status.Backend = candidate;
                status.Probe = probe;
                if (!probe.HasCurrent)
                {
                    status.Percent = status.EffectivePercent;
                }
                status.Failed = false;
                status.FailureReason = null;
                _failures[status.Display.Id] = 0;
                RaiseUpdated(status);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  Enumeración y elección de método
        // ------------------------------------------------------------------

        private void DoRescan()
        {
            IList<DisplayInfo> displays = null;
            try
            {
                displays = _enumerator.Enumerate();
            }
            catch (Exception ex)
            {
                Log.Error("Error al enumerar pantallas", ex);
                RaiseNotice("No se pudieron enumerar las pantallas: " + ex.Message);
            }

            List<DisplayInfo> ordered = new List<DisplayInfo>();
            if (displays != null)
            {
                for (int i = 0; i < displays.Count; i++)
                {
                    if (displays[i] != null)
                    {
                        ordered.Add(displays[i]);
                    }
                }
            }
            ordered.Sort(delegate(DisplayInfo a, DisplayInfo b)
            {
                int byX = a.Bounds.X.CompareTo(b.Bounds.X);
                if (byX != 0) { return byX; }
                int byY = a.Bounds.Y.CompareTo(b.Bounds.Y);
                if (byY != 0) { return byY; }
                return string.Compare(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                DisplayInfo display = ordered[i];
                display.MonitorIndex = i + 1;
                if (string.IsNullOrEmpty(display.Id))
                {
                    display.Id = "pantalla-" + (i + 1).ToString();
                }
            }

            List<DisplayStatus> rebuilt = new List<DisplayStatus>();
            bool structureChanged = false;

            lock (_gate)
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    DisplayInfo display = ordered[i];
                    DisplayStatus existing = null;
                    for (int j = 0; j < _statuses.Count; j++)
                    {
                        DisplayStatus candidate = _statuses[j];
                        if (candidate.Display != null
                            && string.Equals(candidate.Display.Id, display.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            existing = candidate;
                            break;
                        }
                    }
                    if (existing == null)
                    {
                        existing = new DisplayStatus();
                        existing.Display = display;
                        existing.Percent = 100;
                        existing.PendingPercent = 100;
                        structureChanged = true;
                    }
                    else
                    {
                        if (!existing.Display.Bounds.Equals(display.Bounds)
                            || existing.Display.Handle != display.Handle
                            || existing.Display.IsHdrActive != display.IsHdrActive)
                        {
                            structureChanged = true;
                        }
                        existing.Display = display;
                    }
                    rebuilt.Add(existing);
                }
                if (rebuilt.Count != _statuses.Count)
                {
                    structureChanged = true;
                }
                _statuses.Clear();
                _statuses.AddRange(rebuilt);
            }

            IList<IBrightnessBackend> backends = _provider.All;
            for (int i = 0; i < backends.Count; i++)
            {
                try { backends[i].OnDisplaysChanged(ordered); }
                catch (Exception ex) { Log.Warn("OnDisplaysChanged en " + backends[i].Id + ": " + Log.Describe(ex)); }
            }

            for (int i = 0; i < rebuilt.Count; i++)
            {
                DisplayStatus status = rebuilt[i];
                if (status.Backend == null || !status.Backend.IsPlatformSupported)
                {
                    BindBackend(status);
                    structureChanged = true;
                }
            }

            bool first = !_firstScanCompleted;
            if (first)
            {
                _firstScanCompleted = true;
                if (_settings.RestoreOnStartup)
                {
                    RestoreSavedValues(rebuilt);
                }
                _initialScan.Set();
            }

            if (structureChanged || first)
            {
                RaiseDisplaysChanged();
            }
            for (int i = 0; i < rebuilt.Count; i++)
            {
                RaiseUpdated(rebuilt[i]);
            }
        }

        private void RestoreSavedValues(IList<DisplayStatus> statuses)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                if (status.Display == null)
                {
                    continue;
                }
                DisplaySettings saved = _settings.ForDisplay(status.Display.Id);
                if (!saved.HasBrightness)
                {
                    continue;
                }
                if (saved.Brightness == status.Percent)
                {
                    continue;
                }
                Log.Info(string.Format("Restaurando {0} al {1}% (valor actual {2}%)",
                    status.Display.FriendlyName, saved.Brightness, status.Percent));
                Queue(status.Display.Id, saved.Brightness);
            }
        }

        private void BindBackend(DisplayStatus status)
        {
            DisplayInfo display = status.Display;
            string forced = PreferredBackend(display.Id);
            IList<IBrightnessBackend> candidates = _provider.GetCandidates(display);

            IBrightnessBackend chosen = null;
            BackendProbe chosenProbe = new BackendProbe();

            if (!string.IsNullOrEmpty(forced))
            {
                IBrightnessBackend forcedBackend = _provider.Get(forced);
                if (forcedBackend != null && forcedBackend.IsPlatformSupported)
                {
                    BackendProbe probe = ProbeBackend(forcedBackend, display);
                    if (probe.Supported)
                    {
                        chosen = forcedBackend;
                        chosenProbe = probe;
                    }
                    else
                    {
                        Log.Warn(string.Format("El método {0} no está disponible en {1}: {2}",
                            forced, display.FriendlyName, probe.Reason));
                    }
                }
            }

            if (chosen == null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    IBrightnessBackend candidate = candidates[i];
                    if (candidate == null || !candidate.IsPlatformSupported)
                    {
                        continue;
                    }
                    BackendProbe probe = ProbeBackend(candidate, display);
                    if (probe.Supported)
                    {
                        chosen = candidate;
                        chosenProbe = probe;
                        break;
                    }
                }
            }

            if (chosen == null)
            {
                status.Backend = null;
                status.Failed = true;
                status.FailureReason = "ningún método disponible";
                Log.Warn("Sin método de control para " + display.FriendlyName);
                return;
            }

            status.Backend = chosen;
            status.Probe = chosenProbe;
            status.Failed = false;
            status.FailureReason = null;

            int percent;
            DisplaySettings saved = _settings.ForDisplay(display.Id);
            bool wantsExtraDim = saved.HasBrightness && saved.Brightness < 0;

            if (chosenProbe.HasCurrent && !wantsExtraDim)
            {
                percent = chosenProbe.ToPercent(chosenProbe.Current);
            }
            else if (saved.HasBrightness)
            {
                percent = saved.Brightness;
            }
            else if (chosenProbe.HasCurrent)
            {
                percent = chosenProbe.ToPercent(chosenProbe.Current);
            }
            else
            {
                percent = 100;
            }
            status.Percent = BrightnessRange.Clamp(percent);
            status.PendingPercent = status.Percent;

            Log.Info(string.Format("Pantalla {0}: método {1} ({2}) brillo {3}%",
                display.FriendlyName, chosen.Id, chosenProbe.Describe(), status.Percent));

            // Los métodos sin lectura (gamma, superposición) y la atenuación extra
            // necesitan que se les comunique el valor inicial.
            if (!chosenProbe.HasCurrent || wantsExtraDim)
            {
                Queue(display.Id, status.Percent);
            }
        }

        private BackendProbe ProbeBackend(IBrightnessBackend backend, DisplayInfo display)
        {
            string key = backend.Id + "|" + display.Id;
            DateTime now = DateTime.UtcNow;
            lock (_gate)
            {
                ProbeCacheEntry cached;
                if (_probeCache.TryGetValue(key, out cached))
                {
                    TimeSpan ttl = cached.Probe.Supported ? TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(20);
                    if (now - cached.When < ttl)
                    {
                        return cached.Probe;
                    }
                }
            }

            DateTime started = DateTime.UtcNow;
            BackendProbe probe;
            try
            {
                probe = backend.Probe(display);
            }
            catch (Exception ex)
            {
                probe = BackendProbe.Unsupported(Log.Describe(ex));
            }
            double elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
            if (elapsed > 400)
            {
                Log.Warn(string.Format("Sondeo lento: {0} en {1} ({2:0} ms)",
                    backend.Id, display.FriendlyName, elapsed));
            }

            lock (_gate)
            {
                ProbeCacheEntry entry = new ProbeCacheEntry();
                entry.Probe = probe;
                entry.When = now;
                _probeCache[key] = entry;
            }
            return probe;
        }

        private string PreferredBackend(string displayId)
        {
            DisplaySettings displaySettings = _settings.ForDisplay(displayId);
            if (!string.IsNullOrEmpty(displaySettings.Backend)
                && !string.Equals(displaySettings.Backend, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return displaySettings.Backend;
            }
            if (!string.IsNullOrEmpty(_settings.DefaultBackend)
                && !string.Equals(_settings.DefaultBackend, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return _settings.DefaultBackend;
            }
            return null;
        }

        private bool IsAutomatic(string displayId)
        {
            DisplaySettings displaySettings = _settings.ForDisplay(displayId);
            bool displayAuto = string.IsNullOrEmpty(displaySettings.Backend)
                || string.Equals(displaySettings.Backend, "auto", StringComparison.OrdinalIgnoreCase);
            bool globalAuto = string.IsNullOrEmpty(_settings.DefaultBackend)
                || string.Equals(_settings.DefaultBackend, "auto", StringComparison.OrdinalIgnoreCase);
            return displayAuto && globalAuto;
        }

        // ------------------------------------------------------------------
        //  Lectura de valores reales
        // ------------------------------------------------------------------

        private void DoReadAll()
        {
            IList<DisplayStatus> snapshot = Snapshot();
            int updated = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                DisplayStatus status = snapshot[i];
                if (status.Backend == null || status.Display == null)
                {
                    continue;
                }
                int? value = null;
                try
                {
                    value = status.Backend.TryRead(status.Display);
                }
                catch (Exception ex)
                {
                    Log.Debug("TryRead " + status.Backend.Id + ": " + Log.Describe(ex));
                }
                if (!value.HasValue)
                {
                    continue;
                }
                int percent = BrightnessRange.Clamp(value.Value);
                if (_dimLayer != null)
                {
                    try { _dimLayer.ClearExtraDim(status.Display.Id); }
                    catch { }
                }
                lock (_gate)
                {
                    status.Percent = percent;
                    status.PendingPercent = percent;
                    status.Pending = false;
                    status.Failed = false;
                    status.FailureReason = null;
                }
                SaveBrightness(status.Display.Id, percent);
                RaiseUpdated(status);
                updated++;
            }
            if (updated > 0)
            {
                RaiseNotice("Valores actualizados desde el hardware.");
            }
        }

        // ------------------------------------------------------------------
        //  Persistencia
        // ------------------------------------------------------------------

        private void SaveBrightness(string displayId, int percent)
        {
            if (string.IsNullOrEmpty(displayId))
            {
                return;
            }
            _settings.ForDisplay(displayId).Brightness = BrightnessRange.Clamp(percent);
            if ((DateTime.UtcNow - _lastSave).TotalMilliseconds > 900)
            {
                SaveSettingsNow();
            }
        }

        public void SaveSettingsNow()
        {
            lock (_saveGate)
            {
                _lastSave = DateTime.UtcNow;
                _settings.Save(_store);
            }
        }

        // ------------------------------------------------------------------
        //  Eventos
        // ------------------------------------------------------------------

        private void RaiseDisplaysChanged()
        {
            EventHandler handler = DisplaysChanged;
            if (handler == null)
            {
                return;
            }
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Warn("DisplaysChanged: " + Log.Describe(ex)); }
        }

        private void RaiseUpdated(DisplayStatus status)
        {
            EventHandler<DisplayStatus> handler = DisplayUpdated;
            if (handler == null)
            {
                return;
            }
            try { handler(this, status); }
            catch (Exception ex) { Log.Warn("DisplayUpdated: " + Log.Describe(ex)); }
        }

        private void RaiseNotice(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            Log.Info("Aviso: " + message);
            EventHandler<string> handler = Notice;
            if (handler == null)
            {
                return;
            }
            try { handler(this, message); }
            catch (Exception ex) { Log.Warn("Notice: " + Log.Describe(ex)); }
        }
    }
}
