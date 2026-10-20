using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;
using Mithor.App.Service;
using Mithor.Core.Config;
using Mithor.Core.Model;
using Mithor.Core.Services;
using Mithor.Core.Util;
using Mithor.Platform.Windows;
using Mithor.Platform.Windows.Native;
using Mithor.Platform.Windows.SystemIntegration;

namespace Mithor.App.Ui
{
    /// <summary>Ventana principal de MITHOR.</summary>
    internal sealed class MainForm : Form
    {
        private const int HotkeyStepUp = 0x4D01;
        private const int HotkeyStepDown = 0x4D02;
        private const int HotkeyReset = 0x4D03;
        private static readonly int TaskbarCreatedMessage = (int)Win32.RegisterWindowMessage("TaskbarCreated");

        private readonly BrightnessController _controller;
        private readonly WindowsBackendProvider _provider;
        private readonly AppSettings _settings;
        private readonly FileSettingsStore _store;
        private readonly SingleInstance _singleInstance;
        private readonly bool _startMinimized;
        private readonly Dictionary<string, MonitorCard> _cards =
            new Dictionary<string, MonitorCard>(StringComparer.OrdinalIgnoreCase);

        private Panel _header;
        private Label _title;
        private Label _subtitle;
        private Label _masterCaption;
        private Label _masterPercent;
        private Label _sectionLabel;
        private Label _countLabel;
        private Label _statusLabel;
        private Label _versionLabel;
        private BrightnessSlider _master;
        private Button _buttonMax;
        private Button _buttonRefresh;
        private Button _buttonSettings;
        private FlowLayoutPanel _list;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private System.Windows.Forms.Timer _debounce;
        private System.Windows.Forms.Timer _exclusion;
        private System.Windows.Forms.Timer _identifyTimer;
        private List<DisplayStatus> _identifyQueue;
        private int _identifyIndex;
        private bool _syncing;
        private bool _exitRequested;
        private bool _balloonShown;
        private bool _subscribedSystemEvents;

        public MainForm(
            BrightnessController controller,
            WindowsBackendProvider provider,
            AppSettings settings,
            FileSettingsStore store,
            SingleInstance singleInstance,
            bool startMinimized)
        {
            _controller = controller;
            _provider = provider;
            _settings = settings;
            _store = store;
            _singleInstance = singleInstance;
            _startMinimized = startMinimized;

            Text = "MITHOR · Control de brillo";
            ClientSize = new Size(920, 660);
            MinimumSize = new Size(780, 540);
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Icon = Theme.AppIcon;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            DoubleBuffered = true;
            KeyPreview = true;
            if (startMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }

            // El orden de alta determina el orden de acoplamiento: el control Fill
            // debe ser el primero (se acopla al final y recibe el espacio restante).
            BuildContent();
            BuildFooter();
            BuildHeader();
            BuildTray();
            BuildTimers();
            WireController();
            WireSingleInstance();
        }

        // ------------------------------------------------------------------
        //  Construcción de la interfaz
        // ------------------------------------------------------------------

        private void BuildHeader()
        {
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = Theme.Scale(this, 158);
            _header.BackColor = Theme.Background;
            _header.Paint += PaintHeader;

            _title = MakeLabel("MITHOR", Theme.Title, Theme.Text);
            _subtitle = MakeLabel("Control de brillo universal · Windows 10 / 11", Theme.Small, Theme.Muted);
            _masterCaption = MakeLabel("BRILLO GENERAL", Theme.Eyebrow, Theme.Muted);

            _masterPercent = new Label();
            _masterPercent.AutoSize = false;
            _masterPercent.BackColor = Color.Transparent;
            _masterPercent.Font = Theme.Big;
            _masterPercent.ForeColor = Theme.Text;
            _masterPercent.TextAlign = ContentAlignment.MiddleRight;
            _masterPercent.Text = FormatPercent(100);

            _master = new BrightnessSlider();
            _master.BackColor = Theme.Background;
            _master.Step = Math.Max(1, _settings.StepPercent);
            _master.ValueChanged += delegate
            {
                if (_syncing)
                {
                    return;
                }
                int value = _master.Value;
                _masterPercent.Text = FormatPercent(value);
                _masterPercent.ForeColor = value < 0 ? Theme.CoolHot : Theme.Text;
                _controller.SetAll(value);
                UpdateTrayText(value);
            };

            _buttonMax = MakeButton("100 %");
            _buttonRefresh = MakeButton("Actualizar");
            _buttonSettings = MakeButton("Ajustes");

            _buttonMax.Click += delegate { _controller.SetAll(100); SetStatus("Brillo general al 100 %."); };
            _buttonRefresh.Click += delegate
            {
                _controller.RequestReadAll();
                SetStatus("Leyendo el brillo real de cada pantalla…");
            };
            _buttonSettings.Click += delegate { OpenSettings(); };

            _header.Controls.Add(_title);
            _header.Controls.Add(_subtitle);
            _header.Controls.Add(_masterCaption);
            _header.Controls.Add(_masterPercent);
            _header.Controls.Add(_master);
            _header.Controls.Add(_buttonMax);
            _header.Controls.Add(_buttonRefresh);
            _header.Controls.Add(_buttonSettings);
            Controls.Add(_header);

            _header.Resize += delegate { LayoutHeader(); };
            LayoutHeader();
        }

        private void LayoutHeader()
        {
            if (_header == null)
            {
                return;
            }
            int pad = Theme.Scale(this, 24);
            int top = Theme.Scale(this, 13);
            _title.Location = new Point(pad, top);
            _subtitle.Location = new Point(pad + Theme.Scale(this, 2), top + Theme.Scale(this, 36));
            _masterCaption.Location = new Point(pad, Theme.Scale(this, 78));

            int buttonWidth = Theme.Scale(this, 88);
            int buttonHeight = Theme.Scale(this, 30);
            int gap = Theme.Scale(this, 8);
            int right = _header.ClientSize.Width - pad;
            _buttonSettings.Size = new Size(buttonWidth, buttonHeight);
            _buttonSettings.Location = new Point(right - buttonWidth, top);
            _buttonRefresh.Size = new Size(buttonWidth, buttonHeight);
            _buttonRefresh.Location = new Point(right - buttonWidth * 2 - gap, top);
            _buttonMax.Size = new Size(buttonWidth, buttonHeight);
            _buttonMax.Location = new Point(right - buttonWidth * 3 - gap * 2, top);

            _masterPercent.Size = new Size(Theme.Scale(this, 130), Theme.Scale(this, 42));
            _masterPercent.Location = new Point(_header.ClientSize.Width - pad - Theme.Scale(this, 130), Theme.Scale(this, 88));

            _master.Size = new Size(
                Math.Max(120, _header.ClientSize.Width - pad * 2 - Theme.Scale(this, 150)),
                Theme.Scale(this, 30));
            _master.Location = new Point(pad, Theme.Scale(this, 96));
        }

        private void PaintHeader(object sender, PaintEventArgs e)
        {
            Rectangle rect = _header.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, rect.Width, Math.Max(2, rect.Height)),
                Color.FromArgb(26, 33, 44), Theme.Background, LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
            using (Pen pen = new Pen(Color.FromArgb(70, Theme.Accent), 1f))
            {
                e.Graphics.DrawLine(pen, 0, rect.Height - 1, rect.Width, rect.Height - 1);
            }
        }

        private void BuildContent()
        {
            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Background;
            content.Padding = new Padding(Theme.Scale(this, 20), Theme.Scale(this, 8),
                Theme.Scale(this, 20), Theme.Scale(this, 4));

            Panel row = new Panel();
            row.Dock = DockStyle.Fill;
            row.Height = Theme.Scale(this, 30);
            row.BackColor = Theme.Background;

            _sectionLabel = MakeLabel("PANTALLAS", Theme.Eyebrow, Theme.Muted);
            _sectionLabel.Location = new Point(0, Theme.Scale(this, 8));

            _countLabel = new Label();
            _countLabel.AutoSize = false;
            _countLabel.Dock = DockStyle.Right;
            _countLabel.Width = Theme.Scale(this, 240);
            _countLabel.TextAlign = ContentAlignment.MiddleRight;
            _countLabel.Font = Theme.Small;
            _countLabel.ForeColor = Theme.Muted;
            _countLabel.BackColor = Theme.Background;
            _countLabel.Text = "detectando…";

            row.Controls.Add(_sectionLabel);
            row.Controls.Add(_countLabel);

            _list = new FlowLayoutPanel();
            _list.Dock = DockStyle.Fill;
            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.AutoScroll = true;
            _list.BackColor = Theme.Background;
            _list.Padding = new Padding(0, Theme.Scale(this, 2), 0, Theme.Scale(this, 2));
            _list.SizeChanged += delegate { ResizeCards(); };

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.BackColor = Theme.Background;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.Margin = Padding.Empty;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.Scale(this, 30)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.Controls.Add(row, 0, 0);
            layout.Controls.Add(_list, 0, 1);

            content.Controls.Add(layout);
            Controls.Add(content);
        }

        private void BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = Theme.Scale(this, 30);
            footer.BackColor = Theme.Surface;
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
                }
            };

            _statusLabel = new Label();
            _statusLabel.AutoSize = false;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Padding = new Padding(Theme.Scale(this, 20), 0, 0, 0);
            _statusLabel.Font = Theme.Small;
            _statusLabel.ForeColor = Theme.Muted;
            _statusLabel.BackColor = Theme.Surface;
            _statusLabel.Text = "Iniciando MITHOR…";

            _versionLabel = new Label();
            _versionLabel.AutoSize = false;
            _versionLabel.Dock = DockStyle.Right;
            _versionLabel.Width = Theme.Scale(this, 120);
            _versionLabel.TextAlign = ContentAlignment.MiddleRight;
            _versionLabel.Padding = new Padding(0, 0, Theme.Scale(this, 20), 0);
            _versionLabel.Font = Theme.Small;
            _versionLabel.ForeColor = Theme.Muted;
            _versionLabel.BackColor = Theme.Surface;
            _versionLabel.Text = "v" + AppInfo.Version;

            footer.Controls.Add(_statusLabel);
            footer.Controls.Add(_versionLabel);
            Controls.Add(footer);
        }

        private Label MakeLabel(string text, Font font, Color color)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Theme.SurfaceAlt;
            button.ForeColor = Theme.Text;
            button.Font = Theme.Small;
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = Theme.Border;
            return button;
        }

        private void BuildTimers()
        {
            _debounce = new System.Windows.Forms.Timer();
            _debounce.Interval = 800;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                if (_exitRequested)
                {
                    return;
                }
                _controller.RequestRescan();
                SetStatus("Actualizando la lista de pantallas…");
            };

            _exclusion = new System.Windows.Forms.Timer();
            _exclusion.Interval = 350;
            _exclusion.Tick += delegate { UpdateExclusion(); };

            _identifyTimer = new System.Windows.Forms.Timer();
            _identifyTimer.Interval = 1600;
            _identifyTimer.Tick += delegate { IdentifyNext(); };
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = Theme.AppIcon;
            _tray.Text = "MITHOR";
            _tray.Visible = _settings.ShowTrayIcon;
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowFromTray();
                }
            };

            _trayMenu = new ContextMenuStrip();
            Theme.StyleMenu(_trayMenu);

            ToolStripMenuItem open = Theme.MenuItem("Abrir MITHOR", delegate { ShowFromTray(); });
            open.Font = new Font(Theme.Body, FontStyle.Bold);
            _trayMenu.Items.Add(open);
            _trayMenu.Items.Add(new ToolStripSeparator());

            int[] presets = new int[] { 100, 75, 50, 25, -25, -50 };
            for (int i = 0; i < presets.Length; i++)
            {
                int value = presets[i];
                string label = value < 0
                    ? "Extra oscuro (" + FormatPercent(value) + ")"
                    : "Brillo " + value + " %";
                _trayMenu.Items.Add(Theme.MenuItem(label, delegate
                {
                    _controller.SetAll(value);
                    SetStatus(value < 0
                        ? "Atenuación extra al " + (-value) + " %."
                        : "Brillo general al " + value + " %.");
                }));
            }
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Theme.MenuItem("Identificar pantallas", delegate { IdentifyAll(); }));
            _trayMenu.Items.Add(Theme.MenuItem("Diagnóstico…", delegate { ShowDiagnostics(); }));
            _trayMenu.Items.Add(Theme.MenuItem("Abrir carpeta de datos", delegate
            {
                AppPaths.OpenInExplorer(AppPaths.DataDirectory);
            }));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Theme.MenuItem("Salir de MITHOR…", delegate { ExitApplication(); }));

            _tray.ContextMenuStrip = _trayMenu;
        }

        private void WireController()
        {
            _controller.DisplaysChanged += OnDisplaysChanged;
            _controller.DisplayUpdated += OnDisplayUpdated;
            _controller.Notice += OnNotice;
        }

        private void WireSingleInstance()
        {
            if (_singleInstance == null)
            {
                return;
            }
            _singleInstance.ActivationRequested += delegate
            {
                if (IsDisposed)
                {
                    return;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ShowFromTray(); });
                }
                catch
                {
                }
            };
        }

        // ------------------------------------------------------------------
        //  Ciclo de vida
        // ------------------------------------------------------------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.EnableDarkTitleBar(this);
            RegisterHotkeys();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // CenterScreen usa el monitor activo; aquí se fuerza el monitor principal
            // para que la ventana aparezca siempre en el mismo sitio.
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                area.X + Math.Max(0, (area.Width - Width) / 2),
                area.Y + Math.Max(0, (area.Height - Height) / 2));
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Theme.EnableDarkTitleBar(this);
            SystemEvents.DisplaySettingsChanged += OnSystemDisplaySettingsChanged;
            _subscribedSystemEvents = true;
            _controller.Start();
            _exclusion.Start();
            if (_startMinimized)
            {
                HideToTray();
            }
            else
            {
                UpdateExclusion();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exitRequested
                && _settings.ShowTrayIcon
                && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_subscribedSystemEvents)
            {
                try { SystemEvents.DisplaySettingsChanged -= OnSystemDisplaySettingsChanged; }
                catch { }
                _subscribedSystemEvents = false;
            }
            if (_debounce != null) { _debounce.Stop(); _debounce.Dispose(); }
            if (_exclusion != null) { _exclusion.Stop(); _exclusion.Dispose(); }
            if (_identifyTimer != null) { _identifyTimer.Stop(); _identifyTimer.Dispose(); }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_trayMenu != null)
            {
                _trayMenu.Dispose();
                _trayMenu = null;
            }
            _controller.Shutdown();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TaskbarCreatedMessage)
            {
                RestoreTrayIcon();
                return;
            }
            if (m.Msg == Win32.WM_HOTKEY)
            {
                HandleHotkey(m.WParam.ToInt32());
                return;
            }
            if (m.Msg == Win32.WM_DISPLAYCHANGE || m.Msg == Win32.WM_DEVICECHANGE)
            {
                ScheduleRescan();
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutHeader();
            UpdateExclusion();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            UpdateExclusion();
        }

        // ------------------------------------------------------------------
        //  Eventos del controlador
        // ------------------------------------------------------------------

        private void OnDisplaysChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnDisplaysChanged(sender, e); }); }
                catch { }
                return;
            }
            RebuildCards();
        }

        private void OnDisplayUpdated(object sender, DisplayStatus status)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnDisplayUpdated(sender, status); }); }
                catch { }
                return;
            }
            MonitorCard card;
            if (status != null && status.Id != null && _cards.TryGetValue(status.Id, out card))
            {
                card.UpdateFrom(status);
            }
            UpdateMasterFromController();
        }

        private void OnNotice(object sender, string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnNotice(sender, message); }); }
                catch { }
                return;
            }
            SetStatus(message);
        }

        private void OnSystemDisplaySettingsChanged(object sender, EventArgs e)
        {
            ScheduleRescan();
        }

        private void RebuildCards()
        {
            IList<DisplayStatus> statuses = _controller.Snapshot();

            _syncing = true;
            List<MonitorCard> old = new List<MonitorCard>(_cards.Values);
            _cards.Clear();
            for (int i = 0; i < old.Count; i++)
            {
                _list.Controls.Remove(old[i]);
                old[i].Dispose();
            }

            for (int i = 0; i < statuses.Count; i++)
            {
                DisplayStatus status = statuses[i];
                MonitorCard card = new MonitorCard();
                card.SetPreferredBackend(_settings.ForDisplay(status.Id).Backend);
                card.UpdateFrom(status);
                card.Width = CardWidth();
                card.ValueChanged += OnCardValueChanged;
                card.IdentifyRequested += OnCardIdentifyRequested;
                card.BackendChosen += OnCardBackendChosen;
                _cards[status.Id] = card;
                _list.Controls.Add(card);
            }

            int count = statuses.Count;
            _countLabel.Text = count == 1 ? "1 pantalla" : count + " pantallas";
            UpdateMasterFromController();
            _syncing = false;

            _list.PerformLayout();
            Log.Debug(string.Format("UI: {0} tarjeta(s), lista {1}, scroll {2}",
                _list.Controls.Count, _list.ClientSize, _list.AutoScrollPosition));
            Log.Debug("UI geometría: lista=" + _list.PointToScreen(Point.Empty)
                + " cabecera=" + _header.Bounds + " cliente=" + ClientSize);
            for (int i = 0; i < _list.Controls.Count; i++)
            {
                Log.Debug(string.Format("UI tarjeta {0}: {1} · {2}", i,
                    _list.Controls[i].Bounds, ((MonitorCard)_list.Controls[i]).DisplayId));
            }

            if (count == 0)
            {
                SetStatus("No se detectaron pantallas. Pulsa Actualizar o revisa el registro.");
            }
            else
            {
                SetStatus(count == 1
                    ? "1 pantalla bajo control."
                    : count + " pantallas bajo control.");
            }
            UpdateExclusion();
        }

        private void ResizeCards()
        {
            if (_list == null)
            {
                return;
            }
            int width = CardWidth();
            for (int i = 0; i < _list.Controls.Count; i++)
            {
                MonitorCard card = _list.Controls[i] as MonitorCard;
                if (card != null && card.Width != width)
                {
                    card.Width = width;
                }
            }
        }

        private int CardWidth()
        {
            return Math.Max(Theme.Scale(this, 320), _list.ClientSize.Width - Theme.Scale(this, 20));
        }

        private void OnCardValueChanged(object sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }
            MonitorCard card = sender as MonitorCard;
            if (card == null || card.DisplayId == null)
            {
                return;
            }
            _controller.SetBrightness(card.DisplayId, card.SliderValue);
            UpdateMasterFromController();
        }

        private void OnCardIdentifyRequested(object sender, EventArgs e)
        {
            MonitorCard card = sender as MonitorCard;
            if (card == null)
            {
                return;
            }
            DisplayStatus status = _controller.Find(card.DisplayId);
            if (status == null || status.Display == null)
            {
                return;
            }
            _provider.Overlay.FlashIdentity(status.Display, status.Display.MonitorIndex, 1500);
            SetStatus("Identificando " + status.Display.FriendlyName + "…");
        }

        private void OnCardBackendChosen(object sender, BackendChosenEventArgs e)
        {
            MonitorCard card = sender as MonitorCard;
            if (card == null || card.DisplayId == null)
            {
                return;
            }
            card.SetPreferredBackend(e.BackendId);
            _controller.SelectBackend(card.DisplayId, e.BackendId);
            SetStatus("Método de control actualizado; reasignando…");
        }

        private void UpdateMasterFromController()
        {
            if (_master == null)
            {
                return;
            }
            _syncing = true;
            int value = _controller.MasterPercent;
            _master.SetValueSilently(value);
            _masterPercent.Text = FormatPercent(value);
            _masterPercent.ForeColor = value < 0 ? Theme.CoolHot : Theme.Text;
            _syncing = false;
            UpdateTrayText(value);
        }

        private static string FormatPercent(int value)
        {
            return BrightnessRange.Format(value) + " %";
        }

        private void UpdateTrayText(int? value)
        {
            if (_tray == null)
            {
                return;
            }
            int percent = value.HasValue ? value.Value : _controller.MasterPercent;
            string text = "MITHOR · " + FormatPercent(percent);
            if (text.Length > 63)
            {
                text = text.Substring(0, 63);
            }
            try { _tray.Text = text; }
            catch { }
        }

        private void SetStatus(string message)
        {
            if (_statusLabel == null || string.IsNullOrEmpty(message))
            {
                return;
            }
            _statusLabel.Text = message;
        }

        // ------------------------------------------------------------------
        //  Atajos, bandeja y ventana
        // ------------------------------------------------------------------

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            if (!_settings.HotkeysEnabled || !IsHandleCreated)
            {
                return;
            }
            uint modifiers = Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT;
            bool ok = Win32.RegisterHotKey(Handle, HotkeyStepUp, modifiers, Win32.VK_UP);
            ok &= Win32.RegisterHotKey(Handle, HotkeyStepDown, modifiers, Win32.VK_DOWN);
            ok &= Win32.RegisterHotKey(Handle, HotkeyReset, modifiers, Win32.VK_1);
            if (!ok)
            {
                Log.Warn("No se pudieron registrar todos los atajos globales (Ctrl+Alt+↑/↓/1).");
            }
        }

        private void UnregisterHotkeys()
        {
            if (!IsHandleCreated)
            {
                return;
            }
            try
            {
                Win32.UnregisterHotKey(Handle, HotkeyStepUp);
                Win32.UnregisterHotKey(Handle, HotkeyStepDown);
                Win32.UnregisterHotKey(Handle, HotkeyReset);
            }
            catch
            {
            }
        }

        private void HandleHotkey(int id)
        {
            int step = Math.Max(1, _settings.StepPercent);
            switch (id)
            {
                case HotkeyStepUp:
                    _controller.Step(step);
                    SetStatus("Brillo +" + step + " %");
                    break;
                case HotkeyStepDown:
                    _controller.Step(-step);
                    SetStatus("Brillo −" + step + " %");
                    break;
                case HotkeyReset:
                    _controller.SetAll(100);
                    SetStatus("Brillo general al 100 %");
                    break;
            }
        }

        private void ScheduleRescan()
        {
            if (IsDisposed || !IsHandleCreated || _debounce == null)
            {
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private void UpdateExclusion()
        {
            if (_provider == null)
            {
                return;
            }
            if (!Visible || WindowState == FormWindowState.Minimized || IsDisposed)
            {
                _provider.Overlay.SetExclusion(null);
                return;
            }
            Rectangle bounds = Bounds;
            _provider.Overlay.SetExclusion(new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        private void HideToTray()
        {
            if (!_settings.ShowTrayIcon)
            {
                WindowState = FormWindowState.Minimized;
                return;
            }
            _provider.Overlay.SetExclusion(null);
            Hide();
            ShowInTaskbar = false;
            if (!_balloonShown && _tray != null)
            {
                _balloonShown = true;
                try
                {
                    _tray.ShowBalloonTip(2500, "MITHOR sigue activo",
                        "Doble clic en el icono de la bandeja para volver a abrir la ventana.",
                        ToolTipIcon.Info);
                }
                catch
                {
                }
            }
        }

        private void RestoreTrayIcon()
        {
            if (_tray == null)
            {
                return;
            }
            try
            {
                _tray.Visible = false;
                _tray.Visible = _settings.ShowTrayIcon;
                UpdateTrayText(null);
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo restaurar el icono de la bandeja: " + Log.Describe(ex));
            }
        }

        private void ShowFromTray()
        {
            if (IsDisposed)
            {
                return;
            }
            ShowInTaskbar = true;
            if (!Visible)
            {
                Show();
            }
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            UpdateExclusion();
        }

        private void IdentifyAll()
        {
            _identifyQueue = new List<DisplayStatus>(_controller.Snapshot());
            _identifyIndex = 0;
            if (_identifyQueue.Count == 0)
            {
                return;
            }
            SetStatus("Identificando cada pantalla…");
            _identifyTimer.Start();
        }

        private void IdentifyNext()
        {
            if (_identifyQueue == null || _identifyIndex >= _identifyQueue.Count)
            {
                _identifyTimer.Stop();
                _identifyQueue = null;
                SetStatus("Identificación completada.");
                return;
            }
            DisplayStatus status = _identifyQueue[_identifyIndex];
            _identifyIndex++;
            if (status.Display != null)
            {
                _provider.Overlay.FlashIdentity(status.Display, status.Display.MonitorIndex, 1400);
                SetStatus(string.Format("Pantalla {0} de {1}: {2}",
                    status.Display.MonitorIndex, _identifyQueue.Count, status.Display.FriendlyName));
            }
        }

        private void OpenSettings()
        {
            using (SettingsForm dialog = new SettingsForm(_settings, _store, _provider, this))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ApplySettings(dialog.BackendChanged);
                }
            }
        }

        private void ApplySettings(bool backendChanged)
        {
            StartupManager.SetEnabled(_settings.StartWithWindows, AppInfo.ExecutablePath);
            _settings.Save(_store);
            if (_tray != null)
            {
                _tray.Visible = _settings.ShowTrayIcon;
            }
            _provider.Overlay.SetMaxOpacity(_settings.OverlayMaxOpacity);
            Log.Init(AppPaths.LogDirectory, _settings.VerboseLog);
            RegisterHotkeys();
            _master.Step = Math.Max(1, _settings.StepPercent);

            if (backendChanged)
            {
                IList<DisplayStatus> statuses = _controller.Snapshot();
                for (int i = 0; i < statuses.Count; i++)
                {
                    if (statuses[i].Display != null)
                    {
                        _controller.SelectBackend(statuses[i].Display.Id, "auto");
                    }
                }
                SetStatus("Método de control global actualizado.");
            }
            else
            {
                SetStatus("Ajustes guardados.");
            }
        }

        private void ShowDiagnostics()
        {
            string text = Diagnostics.Build(_controller, _settings, _provider);
            using (Form dialog = new Form())
            {
                dialog.Text = "Diagnóstico de MITHOR";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(760, 560);
                dialog.MinimumSize = new Size(560, 400);
                dialog.BackColor = Theme.Background;
                dialog.ForeColor = Theme.Text;
                dialog.Font = Theme.Body;
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.AutoScaleDimensions = new SizeF(96f, 96f);
                dialog.ShowIcon = false;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                Theme.EnableDarkTitleBar(dialog);

                TextBox box = new TextBox();
                box.Multiline = true;
                box.ReadOnly = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Font = new Font("Consolas", 9f, FontStyle.Regular);
                box.BackColor = Theme.Surface;
                box.ForeColor = Theme.Text;
                box.BorderStyle = BorderStyle.None;
                box.Dock = DockStyle.Fill;
                box.Text = text.Replace("\n", Environment.NewLine);
                box.Select(0, 0);

                Panel buttons = new Panel();
                buttons.Dock = DockStyle.Bottom;
                buttons.Height = Theme.Scale(this, 46);
                buttons.BackColor = Theme.Background;
                buttons.Padding = new Padding(0, Theme.Scale(this, 8), 0, 0);

                Button copy = MakeButton("Copiar");
                copy.Dock = DockStyle.Right;
                copy.Width = Theme.Scale(this, 100);
                copy.Click += delegate
                {
                    try { Clipboard.SetText(text); SetStatus("Diagnóstico copiado al portapapeles."); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "No se pudo copiar: " + ex.Message, "MITHOR",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };

                Button close = MakeButton("Cerrar");
                close.Dock = DockStyle.Right;
                close.Width = Theme.Scale(this, 100);
                close.Margin = new Padding(Theme.Scale(this, 8), 0, 0, 0);
                close.Click += delegate { dialog.Close(); };

                buttons.Controls.Add(copy);
                buttons.Controls.Add(close);
                dialog.Controls.Add(box);
                dialog.Controls.Add(buttons);
                dialog.ShowDialog(this);
            }
        }

        private void ExitApplication()
        {
            if (ServiceStatus.IsInstalled())
            {
                if (ServiceStatus.IsRunning())
                {
                    DialogResult answer = MessageBox.Show(this,
                        "MITHOR se está ejecutando en segundo plano mediante el servicio de Windows.\n\n"
                        + "Si sales ahora, el servicio volverá a iniciar la aplicación.\n\n"
                        + "¿Detener el servicio y salir? Se pedirán permisos de administrador.",
                        "MITHOR", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }
                    SetStatus("Deteniendo el servicio de MITHOR…");
                    if (!ServiceStatus.StopElevated() || ServiceStatus.IsRunning())
                    {
                        SetStatus("No se pudo detener el servicio. MITHOR sigue en segundo plano.");
                        return;
                    }
                }
            }

            _exitRequested = true;
            _settings.Save(_store);
            _controller.Shutdown();
            if (_tray != null)
            {
                _tray.Visible = false;
            }
            Close();
        }
    }
}
