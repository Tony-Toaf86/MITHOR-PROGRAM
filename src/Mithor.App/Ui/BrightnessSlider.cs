using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Mithor.Core.Model;

namespace Mithor.App.Ui
{
    /// <summary>
    /// Deslizador de brillo dibujado a mano. El rango es -100..100:
    /// por encima de 0 controla el monitor; por debajo aplica atenuación extra.
    /// </summary>
    internal sealed class BrightnessSlider : Control
    {
        private int _value = 100;
        private int _minimum = BrightnessRange.Minimum;
        private int _maximum = BrightnessRange.Maximum;
        private bool _dragging;
        private bool _hover;

        public event EventHandler ValueChanged;
        public event EventHandler ValueCommitted;

        public BrightnessSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            TabStop = true;
            Height = 28;
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
            Step = 5;
        }

        public int Step { get; set; }

        public int Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = Math.Min(value, _maximum - 1);
                SetValue(_value, false, false);
                Invalidate();
            }
        }

        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = Math.Max(value, _minimum + 1);
                SetValue(_value, false, false);
                Invalidate();
            }
        }

        public int Value
        {
            get { return _value; }
            set { SetValue(value, true, false); }
        }

        public void SetValueSilently(int value)
        {
            SetValue(value, false, false);
        }

        private void SetValue(int value, bool notify, bool commit)
        {
            value = Math.Max(_minimum, Math.Min(_maximum, value));
            if (value == _value)
            {
                if (commit)
                {
                    RaiseCommitted();
                }
                return;
            }
            _value = value;
            Invalidate();
            if (notify)
            {
                EventHandler handler = ValueChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            if (commit)
            {
                RaiseCommitted();
            }
        }

        private void RaiseCommitted()
        {
            EventHandler handler = ValueCommitted;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int padding = Theme.Scale(this, 9);
            int trackHeight = Theme.Scale(this, 8);
            float centerY = Height / 2f;
            RectangleF track = new RectangleF(padding, centerY - trackHeight / 2f,
                Math.Max(4, Width - padding * 2), trackHeight);

            Theme.FillRounded(graphics, track, trackHeight / 2f, Theme.Track);

            float range = _maximum - _minimum;
            float valueFraction = (_value - _minimum) / range;
            float zeroFraction = (0 - _minimum) / range;
            float thumbX = track.X + track.Width * valueFraction;
            float zeroX = track.X + track.Width * zeroFraction;

            bool negative = _value < 0;
            float from = Math.Min(thumbX, zeroX);
            float to = Math.Max(thumbX, zeroX);
            if (to - from > 0.5f)
            {
                RectangleF fill = new RectangleF(from, track.Y, Math.Max(trackHeight, to - from), track.Height);
                SmoothingMode previous = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Theme.RoundedRect(fill, trackHeight / 2f))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new RectangleF(fill.X, fill.Y, Math.Max(2f, fill.Width), fill.Height),
                    negative ? Theme.Cool : Theme.Accent,
                    negative ? Theme.CoolHot : Theme.AccentHot,
                    LinearGradientMode.Horizontal))
                {
                    graphics.FillPath(brush, path);
                }
                graphics.SmoothingMode = previous;
            }

            // Marca del punto 0: separa el brillo del monitor de la atenuación extra.
            using (Pen marker = new Pen(Color.FromArgb(120, Theme.Text), 1f))
            {
                float markerX = zeroX;
                graphics.DrawLine(marker, markerX, centerY - trackHeight, markerX, centerY + trackHeight);
            }

            float radius = Theme.Scale(this, 8);
            Color thumbColor = negative ? Theme.CoolHot : Theme.Accent;
            if (_hover || _dragging || Focused)
            {
                float glow = radius + Theme.Scale(this, 5);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(_dragging ? 64 : 40, thumbColor)))
                {
                    graphics.FillEllipse(brush, thumbX - glow, centerY - glow, glow * 2, glow * 2);
                }
            }
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(250, 252, 255)))
            {
                graphics.FillEllipse(brush, thumbX - radius, centerY - radius, radius * 2, radius * 2);
            }
            using (Pen pen = new Pen(thumbColor, Theme.Scale(this, 2)))
            {
                graphics.DrawEllipse(pen, thumbX - radius, centerY - radius, radius * 2, radius * 2);
            }

            if (Focused)
            {
                using (Pen pen = new Pen(Color.FromArgb(110, thumbColor), 1f))
                {
                    pen.DashStyle = DashStyle.Dot;
                    graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            Focus();
            _dragging = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                SetFromX(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || e.Button != MouseButtons.Left)
            {
                return;
            }
            SetFromX(e.X);
            _dragging = false;
            Capture = false;
            Invalidate();
            RaiseCommitted();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                Value = _maximum;
                RaiseCommitted();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int delta = e.Delta > 0 ? Math.Max(1, Step) : -Math.Max(1, Step);
            Value = _value + delta;
            RaiseCommitted();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down
                || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = Math.Max(1, Step);
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value = _value - 1;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value = _value + 1;
                    break;
                case Keys.PageDown:
                    Value = _value - step * 2;
                    break;
                case Keys.PageUp:
                    Value = _value + step * 2;
                    break;
                case Keys.Home:
                    Value = _minimum;
                    break;
                case Keys.End:
                    Value = _maximum;
                    break;
                default:
                    handled = false;
                    break;
            }
            if (handled)
            {
                RaiseCommitted();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        private void SetFromX(int x)
        {
            int padding = Theme.Scale(this, 9);
            float width = Math.Max(1, Width - padding * 2);
            float fraction = (x - padding) / width;
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            int value = _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
            SetValue(value, true, false);
        }
    }
}
