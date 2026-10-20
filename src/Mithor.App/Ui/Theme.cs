using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Mithor.Platform.Windows.Native;
using Mithor.Platform.Windows.SystemIntegration;

namespace Mithor.App.Ui
{
    /// <summary>Paleta, tipografías y utilidades de dibujo del tema oscuro de MITHOR.</summary>
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(13, 17, 23);
        public static readonly Color Surface = Color.FromArgb(22, 27, 34);
        public static readonly Color SurfaceAlt = Color.FromArgb(28, 34, 43);
        public static readonly Color SurfaceHover = Color.FromArgb(35, 42, 53);
        public static readonly Color Border = Color.FromArgb(38, 46, 60);
        public static readonly Color BorderHover = Color.FromArgb(62, 74, 92);
        public static readonly Color Text = Color.FromArgb(230, 237, 243);
        public static readonly Color Muted = Color.FromArgb(139, 148, 158);
        public static readonly Color Accent = Color.FromArgb(240, 164, 58);
        public static readonly Color AccentHot = Color.FromArgb(255, 198, 112);
        public static readonly Color Cool = Color.FromArgb(122, 138, 255);
        public static readonly Color CoolHot = Color.FromArgb(158, 172, 255);
        public static readonly Color Track = Color.FromArgb(43, 51, 65);
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);
        public static readonly Color Success = Color.FromArgb(110, 231, 168);
        public static readonly Color Warning = Color.FromArgb(245, 199, 126);

        private static readonly string Family = PickFamily(new string[]
        {
            "Segoe UI Variable Text", "Segoe UI", "Tahoma", "Arial"
        });

        public static readonly Font Title = new Font(Family, 19f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Big = new Font(Family, 24f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Percent = new Font(Family, 13f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Body = new Font(Family, 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Bold = new Font(Family, 10f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Small = new Font(Family, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Eyebrow = new Font(Family, 8f, FontStyle.Bold, GraphicsUnit.Point);

        private static Icon _appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    _appIcon = LoadIcon();
                }
                return _appIcon;
            }
        }

        private static string PickFamily(string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    using (FontFamily family = new FontFamily(candidates[i]))
                    {
                        return candidates[i];
                    }
                }
                catch
                {
                }
            }
            return FontFamily.GenericSansSerif.Name;
        }

        private static Icon LoadIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(AppInfo.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        public static int Scale(Control control, double value)
        {
            double dpi = 96.0;
            if (control != null)
            {
                dpi = control.DeviceDpi;
                if (dpi <= 0) { dpi = 96.0; }
            }
            return (int)Math.Round(value * dpi / 96.0);
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            if (diameter <= 1f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics graphics, RectangleF rect, float radius, Color fill)
        {
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                graphics.FillPath(brush, path);
            }
            graphics.SmoothingMode = previous;
        }

        public static void StrokeRounded(Graphics graphics, RectangleF rect, float radius, Color stroke, float width)
        {
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(rect, radius))
            using (Pen pen = new Pen(stroke, width))
            {
                graphics.DrawPath(pen, path);
            }
            graphics.SmoothingMode = previous;
        }

        /// <summary>Dibuja una "píldora" con texto (etiqueta de método de control).</summary>
        public static int DrawChip(Graphics graphics, Point rightTop, string text, Color background, Color foreground, Font font)
        {
            Size size = TextRenderer.MeasureText(text, font);
            int height = size.Height + 8;
            int width = size.Width + 18;
            Rectangle rect = new Rectangle(rightTop.X - width, rightTop.Y, width, height);
            FillRounded(graphics, rect, height / 2f, background);
            TextRenderer.DrawText(graphics, text, font, rect, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return width;
        }

        public static void StyleMenu(ContextMenuStrip menu)
        {
            if (menu == null)
            {
                return;
            }
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Font = Body;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Padding = new Padding(2);
        }

        public static ToolStripMenuItem MenuItem(string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Text;
            item.BackColor = Surface;
            if (onClick != null)
            {
                item.Click += onClick;
            }
            return item;
        }

        public static void EnableDarkTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated)
            {
                return;
            }
            try
            {
                int enabled = 1;
                if (Win32.DwmSetWindowAttribute(form.Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref enabled, sizeof(int)) != 0)
                {
                    Win32.DwmSetWindowAttribute(form.Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD,
                        ref enabled, sizeof(int));
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color ButtonSelectedHighlight { get { return Theme.SurfaceHover; } }
        public override Color ButtonSelectedBorder { get { return Theme.Border; } }
        public override Color CheckBackground { get { return Theme.SurfaceAlt; } }
        public override Color CheckSelectedBackground { get { return Theme.SurfaceHover; } }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Theme.Surface))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }
    }
}
