using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Mithor.Core.Abstractions;
using Mithor.Core.Model;
using Mithor.Core.Util;
using Mithor.Platform.Windows.Native;

namespace Mithor.Platform.Windows.Backends
{
    /// <summary>
    /// Atenuación por superposición: una ventana negra translúcida, siempre visible y
    /// que deja pasar los clics, cubre cada pantalla. Funciona en absolutamente
    /// cualquier salida de vídeo (monitores físicos, televisores, proyectores,
    /// pantallas virtuales, escritorio remoto, capturadoras), por eso es el último
    /// recurso universal.
    /// </summary>
    public sealed class OverlayBackend : IBrightnessBackend, IDimLayer
    {
        private readonly object _gate = new object();
        private readonly object _threadGate = new object();
        private readonly List<OverlaySpec> _specs = new List<OverlaySpec>();
        private readonly Dictionary<string, int> _values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _extra = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private double _maxOpacity = 0.92;
        private PixelRect? _exclusion;
        private OverlayThread _thread;
        private FlashRequest _flash;

        public string Id { get { return "overlay"; } }
        public string Label { get { return "Superposición"; } }
        public string Description { get { return "Atenuación universal mediante una capa negra translúcida sobre la pantalla."; } }
        public int Priority { get { return 90; } }
        public bool IsPlatformSupported { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        public int WindowCount
        {
            get
            {
                OverlayThread thread = _thread;
                return thread == null ? 0 : thread.WindowCount;
            }
        }

        public BackendProbe Probe(DisplayInfo display)
        {
            if (!EnsureThread())
            {
                return BackendProbe.Unsupported("no se pudo crear la capa de atenuación");
            }
            return BackendProbe.SupportedRange(0, 100, 100, false,
                "atenuación por superposición (compatible con cualquier pantalla)");
        }

        public int? TryRead(DisplayInfo display)
        {
            lock (_gate)
            {
                int value;
                if (_values.TryGetValue(display.Id, out value))
                {
                    return value;
                }
            }
            return null;
        }

        public bool TryWrite(DisplayInfo display, int percent)
        {
            percent = BackendProbe.ClampPercent(percent);
            if (!EnsureThread())
            {
                return false;
            }

            lock (_gate)
            {
                OverlaySpec spec = EnsureSpecLocked(display);
                spec.Bounds = display.Bounds;
                spec.Percent = percent;
                _values[display.Id] = percent;
            }
            RequestReconcile();
            return true;
        }

        /// <summary>
        /// Atenuación por debajo del mínimo del monitor: el hardware se queda en 0 %
        /// y esta capa oscurece la imagen por encima.
        /// </summary>
        public void SetExtraDim(DisplayInfo display, int percent)
        {
            if (display == null)
            {
                return;
            }
            percent = BackendProbe.ClampPercent(percent);
            if (!EnsureThread())
            {
                return;
            }

            bool changed;
            lock (_gate)
            {
                int previous;
                _extra.TryGetValue(display.Id, out previous);
                changed = previous != percent;
                _extra[display.Id] = percent;
                OverlaySpec spec = EnsureSpecLocked(display);
                spec.Bounds = display.Bounds;
            }
            if (changed)
            {
                RequestReconcile();
            }
        }

        public void ClearExtraDim(string displayId)
        {
            if (string.IsNullOrEmpty(displayId))
            {
                return;
            }
            bool changed;
            lock (_gate)
            {
                changed = _extra.Remove(displayId);
            }
            if (changed)
            {
                RequestReconcile();
            }
        }

        private OverlaySpec EnsureSpecLocked(DisplayInfo display)
        {
            for (int i = 0; i < _specs.Count; i++)
            {
                if (string.Equals(_specs[i].Id, display.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return _specs[i];
                }
            }
            OverlaySpec spec = new OverlaySpec();
            spec.Id = display.Id;
            spec.Bounds = display.Bounds;
            spec.Percent = 100;
            _specs.Add(spec);
            return spec;
        }

        public void OnDisplaysChanged(IList<DisplayInfo> displays)
        {
            if (displays == null)
            {
                return;
            }
            lock (_gate)
            {
                List<OverlaySpec> updated = new List<OverlaySpec>();
                for (int i = 0; i < displays.Count; i++)
                {
                    DisplayInfo display = displays[i];
                    if (display == null)
                    {
                        continue;
                    }
                    OverlaySpec spec = new OverlaySpec();
                    spec.Id = display.Id;
                    spec.Bounds = display.Bounds;
                    int value;
                    spec.Percent = _values.TryGetValue(display.Id, out value) ? value : 100;
                    updated.Add(spec);
                }
                _specs.Clear();
                _specs.AddRange(updated);
            }
            RequestReconcile();
        }

        public void SetMaxOpacity(double opacity)
        {
            if (opacity < 0.30) { opacity = 0.30; }
            if (opacity > 0.98) { opacity = 0.98; }
            lock (_gate)
            {
                _maxOpacity = opacity;
            }
            RequestReconcile();
        }

        /// <summary>
        /// Área que la capa debe dejar sin cubrir (la ventana de MITHOR), para que la
        /// interfaz siga legible y utilizable aunque el resto de la pantalla esté oscuro.
        /// </summary>
        public void SetExclusion(PixelRect? bounds)
        {
            bool changed;
            lock (_gate)
            {
                changed = !Nullable.Equals(_exclusion, bounds);
                _exclusion = bounds;
            }
            if (changed)
            {
                RequestReconcile();
            }
        }

        public void FlashIdentity(DisplayInfo display, int number, int durationMilliseconds)
        {
            if (display == null || !EnsureThread())
            {
                return;
            }
            FlashRequest request = new FlashRequest();
            request.Bounds = display.Bounds;
            request.Title = number.ToString();
            request.Subtitle = display.FriendlyName;
            request.DurationMs = Math.Max(400, durationMilliseconds);
            lock (_gate)
            {
                _flash = request;
            }
            OverlayThread thread = _thread;
            if (thread != null)
            {
                thread.PostFlash();
            }
        }

        public void Shutdown()
        {
            OverlayThread thread = _thread;
            _thread = null;
            if (thread != null)
            {
                thread.Dispose();
            }
            lock (_gate)
            {
                _extra.Clear();
            }
        }

        internal List<OverlaySpec> SnapshotSpecs()
        {
            lock (_gate)
            {
                List<OverlaySpec> copy = new List<OverlaySpec>(_specs.Count);
                for (int i = 0; i < _specs.Count; i++)
                {
                    OverlaySpec source = _specs[i];
                    OverlaySpec item = new OverlaySpec();
                    item.Id = source.Id;
                    item.Bounds = source.Bounds;
                    item.Percent = source.Percent;

                    int extra;
                    _extra.TryGetValue(source.Id, out extra);

                    double baseOpacity = (100 - BackendProbe.ClampPercent(source.Percent)) / 100.0 * _maxOpacity;
                    double extraOpacity = BackendProbe.ClampPercent(extra) / 100.0 * _maxOpacity;
                    // Las dos capas se combinan como si fueran dos filtros de densidad.
                    double opacity = 1.0 - (1.0 - baseOpacity) * (1.0 - extraOpacity);
                    int alpha = (int)Math.Round(opacity * 255.0);
                    if (alpha < 0) { alpha = 0; }
                    if (alpha > 245) { alpha = 245; }
                    item.Alpha = alpha;
                    copy.Add(item);
                }
                return copy;
            }
        }

        internal PixelRect? Exclusion
        {
            get
            {
                lock (_gate)
                {
                    return _exclusion;
                }
            }
        }

        internal FlashRequest TakeFlashRequest()
        {
            lock (_gate)
            {
                FlashRequest request = _flash;
                _flash = null;
                return request;
            }
        }

        private bool EnsureThread()
        {
            lock (_threadGate)
            {
                if (_thread != null)
                {
                    return true;
                }
                OverlayThread thread = new OverlayThread(this);
                if (!thread.Start())
                {
                    Log.Error("No se pudo iniciar el hilo de superposición.");
                    thread.Dispose();
                    return false;
                }
                _thread = thread;
                return true;
            }
        }

        private void RequestReconcile()
        {
            OverlayThread thread = _thread;
            if (thread != null)
            {
                thread.PostReconcile();
            }
        }
    }

    internal sealed class OverlaySpec
    {
        public string Id;
        public PixelRect Bounds;
        public int Percent;
        public int Alpha;
    }

    internal sealed class FlashRequest
    {
        public PixelRect Bounds;
        public string Title;
        public string Subtitle;
        public int DurationMs;
    }

    /// <summary>Hilo propietario de las ventanas de superposición y su bucle de mensajes.</summary>
    internal sealed class OverlayThread : IDisposable
    {
        private sealed class WindowState
        {
            public IntPtr Hwnd;
            public PixelRect Bounds;
            public int Alpha;
            public bool Visible;
            public PixelRect? AppliedExclusion;
        }

        private const uint WM_APP_RECONCILE = Win32.WM_APP + 1;
        private const uint WM_APP_FLASH = Win32.WM_APP + 2;
        private const string OverlayClassName = "MithorOverlayWindow";
        private const string FlashClassName = "MithorOverlayLabel";
        private const string HostClassName = "MithorOverlayHost";

        private readonly OverlayBackend _owner;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly Dictionary<string, WindowState> _windows = new Dictionary<string, WindowState>(StringComparer.OrdinalIgnoreCase);

        private Thread _thread;
        private IntPtr _host;
        private IntPtr _instance;
        private ushort _overlayAtom;
        private ushort _flashAtom;
        private ushort _hostAtom;
        private Win32.WndProc _overlayProc;
        private Win32.WndProc _flashProc;
        private Win32.WndProc _hostProc;
        private string _lastError;

        private IntPtr _flashHwnd;
        private string _flashTitle;
        private string _flashSubtitle;
        private int _flashWidth;
        private int _flashHeight;

        public OverlayThread(OverlayBackend owner)
        {
            _owner = owner;
        }

        public int WindowCount
        {
            get
            {
                lock (_windows)
                {
                    return _windows.Count;
                }
            }
        }

        public bool Start()
        {
            if (_thread != null)
            {
                return _host != IntPtr.Zero;
            }
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "MITHOR-overlay";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.WaitOne(5000))
            {
                _lastError = "tiempo de espera agotado";
                return false;
            }
            return _host != IntPtr.Zero;
        }

        public void PostReconcile()
        {
            IntPtr host = _host;
            if (host != IntPtr.Zero)
            {
                Win32.PostMessage(host, WM_APP_RECONCILE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public void PostFlash()
        {
            IntPtr host = _host;
            if (host != IntPtr.Zero)
            {
                Win32.PostMessage(host, WM_APP_FLASH, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public void Dispose()
        {
            IntPtr host = _host;
            if (host != IntPtr.Zero)
            {
                Win32.PostMessage(host, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            Thread thread = _thread;
            if (thread != null && thread.IsAlive)
            {
                thread.Join(3000);
            }
            _thread = null;
        }

        private void Run()
        {
            try
            {
                _instance = Win32.GetModuleHandle(null);
                _overlayProc = OverlayWindowProc;
                _flashProc = FlashWindowProc;
                _hostProc = HostWindowProc;

                _overlayAtom = RegisterClass(OverlayClassName, _overlayProc);
                _flashAtom = RegisterClass(FlashClassName, _flashProc);
                _hostAtom = RegisterClass(HostClassName, _hostProc);
                if (_overlayAtom == 0 || _flashAtom == 0 || _hostAtom == 0)
                {
                    _lastError = "RegisterClassEx falló: " + Win32.LastErrorText();
                    Log.Error("Superposición: " + _lastError);
                    return;
                }

                _host = Win32.CreateWindowEx(0, HostClassName, "MITHOR host", 0,
                    0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, _instance, IntPtr.Zero);
                if (_host == IntPtr.Zero)
                {
                    _lastError = "CreateWindowEx (host) falló: " + Win32.LastErrorText();
                    Log.Error("Superposición: " + _lastError);
                    return;
                }

                _ready.Set();

                Win32.MSG msg;
                while (Win32.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Win32.TranslateMessage(ref msg);
                    Win32.DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Hilo de superposición", ex);
            }
            finally
            {
                _ready.Set();
                CleanupWindows();
            }
        }

        private ushort RegisterClass(string name, Win32.WndProc proc)
        {
            Win32.WNDCLASSEX wc = new Win32.WNDCLASSEX();
            wc.cbSize = Marshal.SizeOf(typeof(Win32.WNDCLASSEX));
            wc.style = Win32.CS_HREDRAW | Win32.CS_VREDRAW;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc);
            wc.hInstance = _instance;
            wc.hbrBackground = Win32.GetStockObject(Win32.BLACK_BRUSH);
            wc.lpszClassName = name;
            return Win32.RegisterClassEx(ref wc);
        }

        private IntPtr HostWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_APP_RECONCILE:
                    Reconcile();
                    return IntPtr.Zero;
                case WM_APP_FLASH:
                    ShowFlash();
                    return IntPtr.Zero;
                case Win32.WM_CLOSE:
                    Win32.DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case Win32.WM_DESTROY:
                    Win32.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private IntPtr OverlayWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32.WM_NCHITTEST)
            {
                return new IntPtr(Win32.HTTRANSPARENT);
            }
            return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private IntPtr FlashWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Win32.WM_PAINT:
                    PaintFlash(hwnd);
                    return IntPtr.Zero;
                case Win32.WM_ERASEBKGND:
                    return new IntPtr(1);
                case Win32.WM_TIMER:
                    Win32.KillTimer(hwnd, new IntPtr(1));
                    Win32.DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case Win32.WM_NCHITTEST:
                    return new IntPtr(Win32.HTTRANSPARENT);
            }
            return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void Reconcile()
        {
            List<OverlaySpec> specs = _owner.SnapshotSpecs();
            PixelRect? exclusion = _owner.Exclusion;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < specs.Count; i++)
            {
                OverlaySpec spec = specs[i];
                if (spec.Alpha <= 0 || spec.Bounds.IsEmpty)
                {
                    WindowState hidden;
                    if (_windows.TryGetValue(spec.Id, out hidden))
                    {
                        Hide(hidden);
                    }
                    continue;
                }

                WindowState state;
                if (!_windows.TryGetValue(spec.Id, out state))
                {
                    state = CreateOverlayWindow(spec);
                    if (state == null)
                    {
                        continue;
                    }
                    _windows[spec.Id] = state;
                }

                if (!state.Bounds.Equals(spec.Bounds))
                {
                    state.Bounds = spec.Bounds;
                    Win32.SetWindowPos(state.Hwnd, Win32.HWND_TOPMOST,
                        spec.Bounds.X, spec.Bounds.Y, spec.Bounds.Width, spec.Bounds.Height,
                        Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
                    state.AppliedExclusion = null;
                }

                if (state.Alpha != spec.Alpha)
                {
                    state.Alpha = spec.Alpha;
                    Win32.SetLayeredWindowAttributes(state.Hwnd, 0, (byte)spec.Alpha, Win32.LWA_ALPHA);
                }

                ApplyExclusion(state, spec.Bounds, exclusion);

                if (!state.Visible)
                {
                    Win32.ShowWindow(state.Hwnd, Win32.SW_SHOWNOACTIVATE);
                    state.Visible = true;
                }
                seen.Add(spec.Id);
            }

            List<string> removed = new List<string>();
            foreach (KeyValuePair<string, WindowState> pair in _windows)
            {
                if (!seen.Contains(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }
            for (int i = 0; i < removed.Count; i++)
            {
                WindowState state = _windows[removed[i]];
                _windows.Remove(removed[i]);
                DestroyWindow(state);
            }
        }

        private WindowState CreateOverlayWindow(OverlaySpec spec)
        {
            IntPtr hwnd = Win32.CreateWindowEx(
                Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW
                    | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE,
                OverlayClassName,
                "MITHOR",
                Win32.WS_POPUP,
                spec.Bounds.X, spec.Bounds.Y, spec.Bounds.Width, spec.Bounds.Height,
                IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                Log.Warn("No se pudo crear la ventana de superposición: " + Win32.LastErrorText());
                return null;
            }

            Win32.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Max(0, Math.Min(255, spec.Alpha)), Win32.LWA_ALPHA);
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST,
                spec.Bounds.X, spec.Bounds.Y, spec.Bounds.Width, spec.Bounds.Height,
                Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

            WindowState state = new WindowState();
            state.Hwnd = hwnd;
            state.Bounds = spec.Bounds;
            state.Alpha = spec.Alpha;
            state.Visible = true;
            return state;
        }

        private static void Hide(WindowState state)
        {
            if (state.Visible)
            {
                Win32.ShowWindow(state.Hwnd, Win32.SW_HIDE);
                state.Visible = false;
            }
        }

        private static void ApplyExclusion(WindowState state, PixelRect bounds, PixelRect? exclusion)
        {
            PixelRect? applied = state.AppliedExclusion;
            bool same = applied.HasValue == exclusion.HasValue;
            if (same && applied.HasValue && exclusion.HasValue)
            {
                same = applied.Value.Equals(exclusion.Value);
            }
            if (same)
            {
                return;
            }

            if (!exclusion.HasValue)
            {
                Win32.SetWindowRgn(state.Hwnd, IntPtr.Zero, true);
                state.AppliedExclusion = null;
                return;
            }

            PixelRect global = exclusion.Value.Intersect(bounds);
            if (global.IsEmpty)
            {
                Win32.SetWindowRgn(state.Hwnd, IntPtr.Zero, true);
                state.AppliedExclusion = exclusion;
                return;
            }

            PixelRect local = new PixelRect(
                global.X - bounds.X, global.Y - bounds.Y, global.Width, global.Height);

            IntPtr full = Win32.CreateRectRgn(0, 0, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
            IntPtr hole = Win32.CreateRectRgn(local.X, local.Y, local.Right, local.Bottom);
            IntPtr result = Win32.CreateRectRgn(0, 0, 0, 0);
            try
            {
                Win32.CombineRgn(result, full, hole, Win32.RGN_DIFF);
                if (Win32.SetWindowRgn(state.Hwnd, result, true) == 0)
                {
                    Win32.DeleteObject(result);
                }
                else
                {
                    result = IntPtr.Zero;
                }
            }
            finally
            {
                Win32.DeleteObject(full);
                Win32.DeleteObject(hole);
                if (result != IntPtr.Zero)
                {
                    Win32.DeleteObject(result);
                }
            }
            state.AppliedExclusion = exclusion;
        }

        private void ShowFlash()
        {
            FlashRequest request = _owner.TakeFlashRequest();
            if (request == null || request.Bounds.IsEmpty)
            {
                return;
            }

            if (_flashHwnd != IntPtr.Zero)
            {
                Win32.DestroyWindow(_flashHwnd);
                _flashHwnd = IntPtr.Zero;
            }

            _flashTitle = request.Title;
            _flashSubtitle = request.Subtitle;
            _flashWidth = request.Bounds.Width;
            _flashHeight = request.Bounds.Height;

            int width = Math.Max(1, request.Bounds.Width);
            int height = Math.Max(1, request.Bounds.Height);
            _flashHwnd = Win32.CreateWindowEx(
                Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW
                    | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE,
                FlashClassName,
                "MITHOR identificación",
                Win32.WS_POPUP,
                request.Bounds.X, request.Bounds.Y, width, height,
                IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);

            if (_flashHwnd == IntPtr.Zero)
            {
                Log.Warn("No se pudo crear la ventana de identificación: " + Win32.LastErrorText());
                return;
            }

            Win32.SetLayeredWindowAttributes(_flashHwnd, 0, 200, Win32.LWA_ALPHA);
            Win32.SetWindowPos(_flashHwnd, Win32.HWND_TOPMOST,
                request.Bounds.X, request.Bounds.Y, width, height,
                Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
            Win32.SetTimer(_flashHwnd, new IntPtr(1), (uint)request.DurationMs, IntPtr.Zero);
            Win32.InvalidateRect(_flashHwnd, IntPtr.Zero, true);
        }

        private void PaintFlash(IntPtr hwnd)
        {
            Win32.PAINTSTRUCT paint;
            IntPtr hdc = Win32.BeginPaint(hwnd, out paint);
            if (hdc == IntPtr.Zero)
            {
                return;
            }
            try
            {
                int width = Math.Max(1, _flashWidth);
                int height = Math.Max(1, _flashHeight);
                using (Graphics graphics = Graphics.FromHdc(hdc))
                {
                    graphics.Clear(Color.FromArgb(14, 17, 23));

                    int bigSize = Math.Max(48, Math.Min(width, height) / 3);
                    using (Font big = new Font("Segoe UI", bigSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Font small = new Font("Segoe UI", Math.Max(18, bigSize / 5), FontStyle.Regular, GraphicsUnit.Pixel))
                    using (Pen border = new Pen(Color.FromArgb(240, 164, 58), 4f))
                    {
                        Rectangle titleRect = new Rectangle(0, height / 2 - bigSize, width, (int)(bigSize * 1.6));
                        TextRenderer.DrawText(graphics, _flashTitle, big, titleRect, Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                        Rectangle subtitleRect = new Rectangle(0, height / 2 + (int)(bigSize * 0.65), width, bigSize / 2);
                        TextRenderer.DrawText(graphics, _flashSubtitle, small, subtitleRect,
                            Color.FromArgb(214, 222, 233),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                        graphics.DrawRectangle(border, new Rectangle(6, 6, width - 12, height - 12));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("PaintFlash: " + Log.Describe(ex));
            }
            finally
            {
                Win32.EndPaint(hwnd, ref paint);
            }
        }

        private void CleanupWindows()
        {
            List<WindowState> all = new List<WindowState>(_windows.Values);
            _windows.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                DestroyWindow(all[i]);
            }
            if (_flashHwnd != IntPtr.Zero)
            {
                Win32.DestroyWindow(_flashHwnd);
                _flashHwnd = IntPtr.Zero;
            }
            if (_instance != IntPtr.Zero)
            {
                if (_overlayAtom != 0) { Win32.UnregisterClass(OverlayClassName, _instance); }
                if (_flashAtom != 0) { Win32.UnregisterClass(FlashClassName, _instance); }
                if (_hostAtom != 0) { Win32.UnregisterClass(HostClassName, _instance); }
            }
        }

        private static void DestroyWindow(WindowState state)
        {
            if (state == null || state.Hwnd == IntPtr.Zero)
            {
                return;
            }
            Win32.DestroyWindow(state.Hwnd);
            state.Hwnd = IntPtr.Zero;
        }
    }
}
