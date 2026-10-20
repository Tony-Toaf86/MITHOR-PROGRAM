using System;
using System.Drawing;
using System.Windows.Forms;
using Mithor.Core.Model;
using Mithor.Core.Services;

namespace Mithor.App.Ui
{
    internal sealed class BackendChosenEventArgs : EventArgs
    {
        public string BackendId { get; private set; }

        public BackendChosenEventArgs(string backendId)
        {
            BackendId = backendId;
        }
    }

    /// <summary>Tarjeta de una pantalla: nombre, estado, método de control y deslizador.</summary>
    internal sealed class MonitorCard : CardPanel
    {
        private readonly Label _name;
        private readonly Label _meta;
        private readonly Label _percent;
        private readonly BrightnessSlider _slider;
        private readonly Button _menu;
        private DisplayStatus _status;
        private string _preferredBackend;

        public event EventHandler ValueChanged;
        public event EventHandler IdentifyRequested;
        public event EventHandler<BackendChosenEventArgs> BackendChosen;

        public MonitorCard()
        {
            Height = Theme.Scale(this, 102);
            Margin = new Padding(0, 0, 0, 10);
            _preferredBackend = "auto";

            _name = new Label();
            _name.AutoSize = false;
            _name.AutoEllipsis = true;
            _name.Font = Theme.Bold;
            _name.ForeColor = Theme.Text;
            _name.BackColor = Theme.Surface;
            _name.Text = "Pantalla";

            _meta = new Label();
            _meta.AutoSize = false;
            _meta.AutoEllipsis = true;
            _meta.Font = Theme.Small;
            _meta.ForeColor = Theme.Muted;
            _meta.BackColor = Theme.Surface;

            _percent = new Label();
            _percent.Font = Theme.Percent;
            _percent.ForeColor = Theme.Text;
            _percent.BackColor = Theme.Surface;
            _percent.TextAlign = ContentAlignment.MiddleRight;
            _percent.Text = "100 %";

            _slider = new BrightnessSlider();
            _slider.BackColor = Theme.Surface;
            _slider.Step = 5;
            _slider.ValueChanged += delegate
            {
                EventHandler handler = ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            _menu = new Button();
            _menu.FlatStyle = FlatStyle.Flat;
            _menu.FlatAppearance.BorderSize = 0;
            _menu.FlatAppearance.MouseOverBackColor = Theme.SurfaceAlt;
            _menu.FlatAppearance.MouseDownBackColor = Theme.SurfaceHover;
            _menu.BackColor = Theme.Surface;
            _menu.ForeColor = Theme.Muted;
            _menu.Font = new Font(Theme.Body.FontFamily, 12f, FontStyle.Bold);
            _menu.Text = "\u22EE";
            _menu.TabStop = false;
            _menu.Cursor = Cursors.Hand;
            _menu.Click += delegate { ShowMenu(); };

            Controls.Add(_name);
            Controls.Add(_meta);
            Controls.Add(_percent);
            Controls.Add(_slider);
            Controls.Add(_menu);
        }

        public string DisplayId
        {
            get { return _status == null ? null : _status.Id; }
        }

        public int SliderValue
        {
            get { return _slider.Value; }
        }

        public void SetPreferredBackend(string backendId)
        {
            _preferredBackend = string.IsNullOrEmpty(backendId) ? "auto" : backendId;
        }

        public void UpdateFrom(DisplayStatus status)
        {
            _status = status;
            if (status == null || status.Display == null)
            {
                return;
            }

            _name.Text = status.Display.FriendlyName;

            string meta = status.Display.DisplaySize;
            if (!string.IsNullOrEmpty(status.Display.OutputTechnology))
            {
                meta += " · " + status.Display.OutputTechnology;
            }
            if (status.Display.RefreshHz > 1)
            {
                meta += " · " + status.Display.RefreshHz + " Hz";
            }
            if (status.Display.IsHdrActive)
            {
                meta += " · HDR";
            }
            if (status.EffectivePercent < 0)
            {
                meta += " · atenuación extra " + (-status.EffectivePercent) + " %";
            }
            if (status.Failed && !string.IsNullOrEmpty(status.FailureReason))
            {
                meta += " · " + status.FailureReason;
            }
            else if (status.Backend != null)
            {
                meta += status.HardwareBackend ? " · control por hardware" : " · control por software";
            }
            _meta.Text = meta;
            _meta.ForeColor = status.Failed ? Theme.Danger : Theme.Muted;

            _percent.Text = status.Backend == null
                ? "—"
                : BrightnessRange.Format(status.EffectivePercent) + " %";
            _percent.ForeColor = status.Backend == null
                ? Theme.Muted
                : (status.EffectivePercent < 0 ? Theme.CoolHot : Theme.Text);
            _slider.Enabled = status.Backend != null;
            _slider.SetValueSilently(status.EffectivePercent);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_name == null || _meta == null || _percent == null || _slider == null || _menu == null)
            {
                // El constructor ajusta Height antes de crear los controles hijos.
                return;
            }
            int padding = Theme.Scale(this, 16);
            int left = Theme.Scale(this, 58);
            int right = Width - padding;

            _name.Location = new Point(left, Theme.Scale(this, 12));
            _name.Size = new Size(Math.Max(80, right - left - Theme.Scale(this, 150)), Theme.Scale(this, 22));

            _meta.Location = new Point(left, Theme.Scale(this, 35));
            _meta.Size = new Size(Math.Max(80, right - left - Theme.Scale(this, 150)), Theme.Scale(this, 18));

            _menu.Location = new Point(right - Theme.Scale(this, 28), Theme.Scale(this, 10));
            _menu.Size = new Size(Theme.Scale(this, 28), Theme.Scale(this, 26));

            _percent.Location = new Point(right - Theme.Scale(this, 90), Theme.Scale(this, 55));
            _percent.Size = new Size(Theme.Scale(this, 90), Theme.Scale(this, 30));

            _slider.Location = new Point(left, Theme.Scale(this, 58));
            _slider.Size = new Size(Math.Max(80, right - Theme.Scale(this, 105) - left), Theme.Scale(this, 28));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;

            int diameter = Theme.Scale(this, 30);
            Rectangle circle = new Rectangle(Theme.Scale(this, 16), Theme.Scale(this, 17), diameter, diameter);
            using (SolidBrush brush = new SolidBrush(Theme.SurfaceAlt))
            {
                graphics.FillEllipse(brush, circle);
            }
            using (Pen pen = new Pen(Theme.Border))
            {
                graphics.DrawEllipse(pen, circle);
            }
            string index = _status != null && _status.Display != null
                ? _status.Display.MonitorIndex.ToString()
                : "?";
            TextRenderer.DrawText(graphics, index, Theme.Bold, circle, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Color chipBackground;
            Color chipForeground;
            string label = ChipLabel(_status, out chipBackground, out chipForeground);
            Theme.DrawChip(graphics, new Point(Width - Theme.Scale(this, 44), Theme.Scale(this, 12)),
                label, chipBackground, chipForeground, Theme.Small);
        }

        private static string ChipLabel(DisplayStatus status, out Color background, out Color foreground)
        {
            background = Color.FromArgb(50, 26, 26);
            foreground = Theme.Danger;
            if (status == null || status.Backend == null)
            {
                return "sin método";
            }
            if (status.HardwareBackend)
            {
                background = Color.FromArgb(22, 48, 40);
                foreground = Theme.Success;
            }
            else
            {
                background = Color.FromArgb(50, 40, 22);
                foreground = Theme.Warning;
            }
            switch (status.Backend.Id)
            {
                case "ddc": return "DDC/CI";
                case "wmi": return "WMI";
                case "gamma": return "Gamma";
                case "overlay": return "Superposición";
                case "sysfs": return "sysfs";
                case "ddcutil": return "ddcutil";
                case "randr": return "RandR";
                default: return status.Backend.Label;
            }
        }

        private void ShowMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);

            menu.Items.Add(Theme.MenuItem("Identificar pantalla", delegate
            {
                EventHandler handler = IdentifyRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }));
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem method = new ToolStripMenuItem("Método de control");
            method.ForeColor = Theme.Muted;
            method.Enabled = false;
            menu.Items.Add(method);

            AddBackendItem(menu, "Automático (recomendado)", "auto");
            AddBackendItem(menu, "DDC/CI (hardware)", "ddc");
            AddBackendItem(menu, "Panel interno (WMI)", "wmi");
            AddBackendItem(menu, "Gamma (software)", "gamma");
            AddBackendItem(menu, "Superposición (universal)", "overlay");

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Theme.MenuItem("Restablecer a 100 %", delegate
            {
                _slider.Value = 100;
                _slider.Focus();
            }));
            menu.Items.Add(Theme.MenuItem("Copiar diagnóstico", delegate { CopyDiagnostic(); }));

            menu.Closed += delegate { menu.Dispose(); };
            menu.Show(_menu, new Point(0, _menu.Height));
        }

        private void AddBackendItem(ContextMenuStrip menu, string text, string backendId)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Theme.Text;
            item.BackColor = Theme.Surface;
            item.Checked = string.Equals(_preferredBackend, backendId, StringComparison.OrdinalIgnoreCase);
            item.Click += delegate
            {
                EventHandler<BackendChosenEventArgs> handler = BackendChosen;
                if (handler != null)
                {
                    handler(this, new BackendChosenEventArgs(backendId));
                }
            };
            menu.Items.Add(item);
        }

        private void CopyDiagnostic()
        {
            try
            {
                Clipboard.SetText(Diagnostics.DescribeDisplay(_status));
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo copiar al portapapeles: " + ex.Message, "MITHOR",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
