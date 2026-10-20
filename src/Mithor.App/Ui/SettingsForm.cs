using System;
using System.Drawing;
using System.Windows.Forms;
using Mithor.Core.Config;
using Mithor.Core.Util;
using Mithor.Platform.Windows;

namespace Mithor.App.Ui
{
    internal sealed class BackendOption
    {
        public string Text { get; private set; }
        public string Id { get; private set; }

        public BackendOption(string text, string id)
        {
            Text = text;
            Id = id;
        }

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>Diálogo de ajustes de MITHOR.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly FileSettingsStore _store;
        private readonly CheckBox _restore;
        private readonly CheckBox _autostart;
        private readonly CheckBox _minimized;
        private readonly CheckBox _hotkeys;
        private readonly CheckBox _trayIcon;
        private readonly CheckBox _verbose;
        private readonly NumericUpDown _step;
        private readonly ComboBox _backend;
        private readonly BrightnessSlider _opacity;
        private readonly Label _opacityValue;
        private int _y;

        public bool BackendChanged { get; private set; }

        public SettingsForm(AppSettings settings, FileSettingsStore store, WindowsBackendProvider provider, Form owner)
        {
            _settings = settings;
            _store = store;

            Text = "Ajustes · MITHOR";
            ClientSize = new Size(620, 690);
            MinimumSize = new Size(560, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Icon = Theme.AppIcon;

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.AutoScroll = false;
            content.BackColor = Theme.Background;
            content.Padding = new Padding(Theme.Scale(this, 26), Theme.Scale(this, 16),
                Theme.Scale(this, 26), Theme.Scale(this, 8));

            _y = 0;
            AddHeading(content, "Ajustes", "Comportamiento y método de control");

            AddSection(content, "COMPORTAMIENTO");
            _restore = AddCheck(content, "Restaurar el último brillo al iniciar", _settings.RestoreOnStartup);
            _autostart = AddCheck(content, "Iniciar con Windows (HKCU\\...\\Run)", _settings.StartWithWindows);
            _minimized = AddCheck(content, "Iniciar minimizado en la bandeja", _settings.StartMinimized);
            _hotkeys = AddCheck(content, "Atajos globales Ctrl+Alt+↑ / ↓ / 1", _settings.HotkeysEnabled);
            _trayIcon = AddCheck(content, "Mostrar icono en la bandeja del sistema", _settings.ShowTrayIcon);
            _verbose = AddCheck(content, "Registro detallado para diagnóstico", _settings.VerboseLog);

            _y += Theme.Scale(this, 6);
            AddSection(content, "CONTROL");
            AddLabel(content, "Método por defecto", Theme.Muted);
            _backend = new ComboBox();
            _backend.DropDownStyle = ComboBoxStyle.DropDownList;
            _backend.FlatStyle = FlatStyle.Flat;
            _backend.BackColor = Theme.SurfaceAlt;
            _backend.ForeColor = Theme.Text;
            _backend.Font = Theme.Body;
            _backend.DrawMode = DrawMode.OwnerDrawFixed;
            _backend.ItemHeight = Theme.Scale(this, 22);
            _backend.Width = Theme.Scale(this, 330);
            _backend.Location = new Point(0, _y);
            _backend.DrawItem += DrawBackendItem;
            _backend.Items.Add(new BackendOption("Automático (recomendado)", "auto"));
            _backend.Items.Add(new BackendOption("DDC/CI (hardware)", "ddc"));
            _backend.Items.Add(new BackendOption("Panel interno (WMI)", "wmi"));
            _backend.Items.Add(new BackendOption("Gamma (software)", "gamma"));
            _backend.Items.Add(new BackendOption("Superposición (universal)", "overlay"));
            SelectBackendOption(_settings.DefaultBackend);
            content.Controls.Add(_backend);
            _y += Theme.Scale(this, 36);

            _step = new NumericUpDown();
            _step.Minimum = 1;
            _step.Maximum = 50;
            _step.Value = Math.Max(1, Math.Min(50, _settings.StepPercent));
            _step.Width = Theme.Scale(this, 80);
            _step.Location = new Point(0, _y);
            _step.BackColor = Theme.SurfaceAlt;
            _step.ForeColor = Theme.Text;
            _step.BorderStyle = BorderStyle.FixedSingle;
            _step.Font = Theme.Body;
            content.Controls.Add(_step);
            AddInline(content, "porcentaje por paso de los atajos", _y + Theme.Scale(this, 4));
            _y += Theme.Scale(this, 38);

            AddLabel(content, "Atenuación máxima de la superposición", Theme.Muted);
            _opacity = new BrightnessSlider();
            _opacity.Step = 1;
            _opacity.Minimum = 0;
            _opacity.Maximum = 100;
            _opacity.BackColor = Theme.Background;
            _opacity.Value = (int)Math.Round(_settings.OverlayMaxOpacity * 100.0);
            _opacity.Width = Theme.Scale(this, 330);
            _opacity.Location = new Point(0, _y);
            _opacity.ValueChanged += delegate { UpdateOpacityLabel(); };
            content.Controls.Add(_opacity);
            _opacityValue = AddInline(content, string.Empty, _y + Theme.Scale(this, 2));
            _opacityValue.Left = Theme.Scale(this, 340);
            UpdateOpacityLabel();
            _y += Theme.Scale(this, 42);

            _y += Theme.Scale(this, 4);
            AddSection(content, "DATOS");
            AddPathLabel(content, "Configuración: " + AppPaths.SettingsFile);
            AddPathLabel(content, "Registro: " + (Log.CurrentFile ?? AppPaths.LogDirectory));
            AddPathLabel(content, "Servicio de Windows: " + Mithor.App.Service.ServiceStatus.Describe());
            _y += Theme.Scale(this, 10);

            Button openData = MakeButton("Abrir carpeta de datos");
            openData.Location = new Point(0, _y);
            openData.Width = Theme.Scale(this, 180);
            openData.Click += delegate { AppPaths.OpenInExplorer(AppPaths.DataDirectory); };
            content.Controls.Add(openData);

            Button openLogs = MakeButton("Abrir registros");
            openLogs.Location = new Point(Theme.Scale(this, 190), _y);
            openLogs.Width = Theme.Scale(this, 160);
            openLogs.Click += delegate { AppPaths.OpenInExplorer(AppPaths.LogDirectory); };
            content.Controls.Add(openLogs);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = Theme.Scale(this, 60);
            buttons.Padding = new Padding(0, Theme.Scale(this, 12), Theme.Scale(this, 26), 0);
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.BackColor = Theme.Surface;
            buttons.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(pen, 0, 0, buttons.Width, 0);
                }
            };

            Button save = MakeButton("Guardar");
            save.BackColor = Theme.Accent;
            save.ForeColor = Color.FromArgb(24, 18, 8);
            save.Font = Theme.Bold;
            save.Width = Theme.Scale(this, 120);
            save.Height = Theme.Scale(this, 34);
            save.Margin = new Padding(0, 0, 0, 0);
            save.Click += delegate { Save(); };

            Button cancel = MakeButton("Cancelar");
            cancel.Width = Theme.Scale(this, 110);
            cancel.Height = Theme.Scale(this, 34);
            cancel.Margin = new Padding(0, 0, Theme.Scale(this, 10), 0);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);

            Controls.Add(content);
            Controls.Add(buttons);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.EnableDarkTitleBar(this);
        }

        private void DrawBackendItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= _backend.Items.Count)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush brush = new SolidBrush(selected ? Theme.SurfaceHover : Theme.SurfaceAlt))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            TextRenderer.DrawText(e.Graphics, _backend.Items[e.Index].ToString(), Theme.Body, e.Bounds,
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void SelectBackendOption(string backendId)
        {
            for (int i = 0; i < _backend.Items.Count; i++)
            {
                BackendOption option = _backend.Items[i] as BackendOption;
                if (option != null && string.Equals(option.Id, backendId, StringComparison.OrdinalIgnoreCase))
                {
                    _backend.SelectedIndex = i;
                    return;
                }
            }
            _backend.SelectedIndex = 0;
        }

        private void UpdateOpacityLabel()
        {
            if (_opacityValue != null)
            {
                _opacityValue.Text = _opacity.Value + " %";
            }
        }

        private void AddHeading(Panel parent, string title, string subtitle)
        {
            Label heading = new Label();
            heading.AutoSize = true;
            heading.Text = title;
            heading.Font = Theme.Title;
            heading.ForeColor = Theme.Text;
            heading.BackColor = Theme.Background;
            heading.Location = new Point(0, _y);
            parent.Controls.Add(heading);
            _y += Theme.Scale(this, 36);

            Label sub = new Label();
            sub.AutoSize = true;
            sub.Text = subtitle;
            sub.Font = Theme.Small;
            sub.ForeColor = Theme.Muted;
            sub.BackColor = Theme.Background;
            sub.Location = new Point(0, _y);
            parent.Controls.Add(sub);
            _y += Theme.Scale(this, 20);
        }

        private void AddSection(Panel parent, string title)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = title;
            label.Font = Theme.Eyebrow;
            label.ForeColor = Theme.Muted;
            label.BackColor = Theme.Background;
            label.Location = new Point(0, _y);
            parent.Controls.Add(label);
            _y += Theme.Scale(this, 26);
        }

        private void AddLabel(Panel parent, string text, Color color)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.Font = Theme.Body;
            label.ForeColor = color;
            label.BackColor = Theme.Background;
            label.Location = new Point(0, _y);
            parent.Controls.Add(label);
            _y += Theme.Scale(this, 24);
        }

        private void AddPathLabel(Panel parent, string text)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Text = text;
            label.Font = Theme.Small;
            label.ForeColor = Theme.Muted;
            label.BackColor = Theme.Background;
            label.Location = new Point(0, _y);
            label.Size = new Size(Theme.Scale(this, 540), Theme.Scale(this, 18));
            parent.Controls.Add(label);
            _y += Theme.Scale(this, 20);
        }

        private Label AddInline(Panel parent, string text, int y)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text = text;
            label.Font = Theme.Small;
            label.ForeColor = Theme.Muted;
            label.BackColor = Theme.Background;
            label.Location = new Point(Theme.Scale(this, 90), y);
            parent.Controls.Add(label);
            return label;
        }

        private CheckBox AddCheck(Panel parent, string text, bool value)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.Checked = value;
            box.AutoSize = false;
            box.Font = Theme.Body;
            box.ForeColor = Theme.Text;
            box.BackColor = Theme.Background;
            box.FlatStyle = FlatStyle.Standard;
            box.Location = new Point(0, _y);
            box.Size = new Size(Theme.Scale(this, 520), Theme.Scale(this, 24));
            parent.Controls.Add(box);
            _y += Theme.Scale(this, 30);
            return box;
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
            button.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = Theme.Border;
            button.Height = Theme.Scale(this, 32);
            return button;
        }

        private void Save()
        {
            BackendOption option = _backend.SelectedItem as BackendOption;
            string backend = option == null ? "auto" : option.Id;
            BackendChanged = !string.Equals(_settings.DefaultBackend, backend, StringComparison.OrdinalIgnoreCase);

            _settings.RestoreOnStartup = _restore.Checked;
            _settings.StartWithWindows = _autostart.Checked;
            _settings.StartMinimized = _minimized.Checked;
            _settings.HotkeysEnabled = _hotkeys.Checked;
            _settings.ShowTrayIcon = _trayIcon.Checked;
            _settings.VerboseLog = _verbose.Checked;
            _settings.StepPercent = (int)_step.Value;
            _settings.OverlayMaxOpacity = Math.Max(0.30, Math.Min(0.98, _opacity.Value / 100.0));
            _settings.DefaultBackend = backend;
            _settings.Save(_store);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
