using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Mithor.Setup
{
    /// <summary>Interfaz del instalador: instalar, reparar o desinstalar MITHOR.</summary>
    internal sealed class SetupForm : Form
    {
        private const string FontFamily = "Segoe UI";

        private readonly bool _uninstall;
        private readonly bool _noDesktop;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Button _primary;
        private readonly Button _secondary;
        private readonly CheckBox _desktop;
        private bool _busy;
        private bool _finished;

        public SetupForm(bool uninstall, bool noDesktop)
        {
            _uninstall = uninstall;
            _noDesktop = noDesktop;

            Text = uninstall ? "Desinstalar MITHOR" : "Instalar MITHOR";
            ClientSize = new Size(620, 480);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(13, 17, 23);
            ForeColor = Color.FromArgb(230, 237, 243);
            Font = new Font(FontFamily, 9.75f);
            ShowIcon = false;

            Label title = new Label();
            title.Text = "MITHOR";
            title.Font = new Font(FontFamily, 24f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(240, 164, 58);
            title.AutoSize = true;
            title.Location = new Point(30, 22);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = uninstall ? "Desinstalador" : "Instalador · versión " + InstallEngine.Version;
            subtitle.Font = new Font(FontFamily, 9f);
            subtitle.ForeColor = Color.FromArgb(139, 148, 158);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(33, 68);
            Controls.Add(subtitle);

            Label description = new Label();
            description.AutoSize = false;
            description.Size = new Size(556, 164);
            description.Location = new Point(30, 100);
            description.Font = new Font(FontFamily, 9.75f);
            description.ForeColor = Color.FromArgb(214, 222, 233);
            description.Text = uninstall
                ? "Se detendrá el servicio de Windows, se cerrará MITHOR y se eliminarán "
                  + "los archivos, los accesos directos y el registro de la aplicación.\n\n"
                  + "Los ajustes de brillo guardados en tu perfil no se borran."
                : "MITHOR instalará el controlador de brillo y su servicio de Windows.\n\n"
                  + "  ·  El servicio arranca con Windows y mantiene MITHOR en segundo plano.\n"
                  + "  ·  El icono aparece en la bandeja del sistema (iconos ocultos).\n"
                  + "  ·  Al cerrar la ventana, MITHOR sigue funcionando.\n\n"
                  + "Se necesitan permisos de administrador para registrar el servicio.";
            Controls.Add(description);

            _desktop = new CheckBox();
            _desktop.Text = "Crear acceso directo en el escritorio";
            _desktop.AutoSize = true;
            _desktop.Location = new Point(30, 272);
            _desktop.ForeColor = Color.FromArgb(230, 237, 243);
            _desktop.BackColor = Color.FromArgb(13, 17, 23);
            _desktop.Checked = !uninstall && !noDesktop;
            _desktop.Visible = !uninstall;
            Controls.Add(_desktop);

            _status = new Label();
            _status.AutoSize = false;
            _status.Size = new Size(556, 66);
            _status.Location = new Point(30, 306);
            _status.Font = new Font(FontFamily, 9f);
            _status.ForeColor = Color.FromArgb(139, 148, 158);
            _status.Text = "Listo para comenzar.";
            Controls.Add(_status);

            _progress = new ProgressBar();
            _progress.Location = new Point(30, 386);
            _progress.Size = new Size(556, 6);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _progress.Visible = false;
            Controls.Add(_progress);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 78;
            footer.BackColor = Color.FromArgb(22, 27, 34);
            footer.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(38, 46, 60)))
                {
                    e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
                }
            };

            _primary = new Button();
            _primary.Text = uninstall ? "Desinstalar" : "Instalar";
            _primary.Font = new Font(FontFamily, 10f, FontStyle.Bold);
            _primary.FlatStyle = FlatStyle.Flat;
            _primary.FlatAppearance.BorderSize = 0;
            _primary.BackColor = Color.FromArgb(240, 164, 58);
            _primary.ForeColor = Color.FromArgb(24, 18, 8);
            _primary.Size = new Size(130, 36);
            _primary.Location = new Point(620 - 156, 24);
            _primary.Click += delegate { PrimaryClicked(); };

            _secondary = new Button();
            _secondary.Text = "Cancelar";
            _secondary.Font = new Font(FontFamily, 9.5f);
            _secondary.FlatStyle = FlatStyle.Flat;
            _secondary.FlatAppearance.BorderSize = 0;
            _secondary.BackColor = Color.FromArgb(28, 34, 43);
            _secondary.ForeColor = Color.FromArgb(230, 237, 243);
            _secondary.Size = new Size(110, 36);
            _secondary.Location = new Point(620 - 276, 24);
            _secondary.Click += delegate { Close(); };

            footer.Resize += delegate
            {
                _primary.Location = new Point(footer.Width - 156, 24);
                _secondary.Location = new Point(footer.Width - 276, 24);
            };

            footer.Controls.Add(_primary);
            footer.Controls.Add(_secondary);
            Controls.Add(footer);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
                }
            }
            catch
            {
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_uninstall)
            {
                DialogResult answer = MessageBox.Show(this,
                    "¿Desinstalar MITHOR y detener su servicio de Windows?",
                    "MITHOR", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    Close();
                }
            }
        }

        private void PrimaryClicked()
        {
            if (_finished)
            {
                Close();
                return;
            }
            if (_busy)
            {
                return;
            }
            StartWork();
        }

        private void StartWork()
        {
            _busy = true;
            _primary.Enabled = false;
            _secondary.Enabled = false;
            _desktop.Enabled = false;
            _progress.Visible = true;
            _status.ForeColor = Color.FromArgb(214, 222, 233);
            _status.Text = "Trabajando…";

            bool desktopShortcut = _desktop.Checked;
            bool uninstall = _uninstall;
            Thread worker = new Thread(delegate()
            {
                InstallResult result = uninstall
                    ? InstallEngine.Uninstall(delegate(string message) { Report(message); })
                    : InstallEngine.Install(desktopShortcut, delegate(string message) { Report(message); });
                Finish(result);
            });
            worker.IsBackground = true;
            worker.Name = "MITHOR-setup";
            worker.Start();
        }

        private void Report(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!IsDisposed)
                    {
                        _status.Text = message;
                    }
                });
            }
            catch
            {
            }
        }

        private void Finish(InstallResult result)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed)
                    {
                        return;
                    }
                    _busy = false;
                    _finished = true;
                    _progress.Visible = false;
                    _status.Text = result.Message;
                    _status.ForeColor = result.Success
                        ? Color.FromArgb(110, 231, 168)
                        : Color.FromArgb(248, 113, 113);
                    _primary.Text = "Cerrar";
                    _primary.Enabled = true;
                    _secondary.Visible = false;
                });
            }
            catch
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
