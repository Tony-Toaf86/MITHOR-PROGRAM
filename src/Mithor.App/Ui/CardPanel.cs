using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Mithor.App.Ui
{
    /// <summary>Panel con esquinas redondeadas y borde, base de las tarjetas de pantalla.</summary>
    internal class CardPanel : Panel
    {
        private bool _hovered;

        public Color FillColor { get; set; }
        public Color StrokeColor { get; set; }
        public float Radius { get; set; }

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            FillColor = Theme.Surface;
            StrokeColor = Theme.Border;
            Radius = 12f;
            BackColor = Theme.Background;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // El fondo se dibuja en OnPaint para evitar parpadeos.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(rect, Radius))
            {
                using (SolidBrush brush = new SolidBrush(FillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (Pen pen = new Pen(_hovered ? Theme.BorderHover : StrokeColor, 1f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }

        protected override void OnMouseEnter(System.EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(System.EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }
    }
}
