using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace BP3D
{
    /* Links que não são conteúdo do navegador (créditos, por exemplo) vão para o
       navegador padrão do PC, nunca para uma aba do nosso. */
    internal static class Shell
    {
        public static void OpenUrl(string url)
        {
            string u = (url ?? "").Trim();
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Log.W("shell: link recusado (" + u + ")");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(u) { UseShellExecute = true });
                Log.W("shell: aberto no navegador padrão -> " + u);
            }
            catch (Exception ex)
            {
                Log.W("shell: falhou ao abrir " + u + " (" + ex.Message + ")");
            }
        }
    }

    /* Abertura do app: cartão com a marca, créditos e um acorde curto.
       Fecha sozinho, no clique ou em qualquer tecla. Clique no nome abre o
       Facebook numa aba do próprio navegador (não no navegador do sistema). */
    internal sealed class SplashForm : Form
    {
        public const string CreditUrl = "https://www.facebook.com/gmtiranosconquer";

        private const string CreditLead = "desenvolvido por ";
        private const string CreditName = "Galber Bynario";

        private readonly Timer _life;
        private readonly Font _title;
        private readonly Font _sub;
        private readonly Font _credit;
        private readonly Font _hint;
        private Image _logo;
        private MemoryStream _audio;
        private SoundPlayer _player;
        private Rectangle _creditBox;
        private bool _creditHover;

        public static void ShowSplash()
        {
            try
            {
                using (SplashForm s = new SplashForm()) s.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.W("splash: falhou (" + ex.Message + ")");
            }
        }

        public SplashForm()
        {
            Text = "3D Browser Pro";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(470, 300);
            BackColor = Color.FromArgb(10, 10, 18);
            DoubleBuffered = true;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;

            _title = new Font("Segoe UI Semibold", 23f, FontStyle.Bold, GraphicsUnit.Pixel);
            _sub = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            _credit = new Font("Segoe UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            _hint = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);

            try { _logo = Image.FromFile(Paths.In("assets/icon256.png")); }
            catch (Exception) { _logo = null; }

            _life = new Timer();
            _life.Interval = 3200;
            _life.Tick += delegate { Close(); };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), 18))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch (Exception) { /* sem cantos arredondados, segue */ }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PlaySound();
            _life.Start();
            Log.W("splash: aberto · som " + FutureTone().Length / 2 + " amostras");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle r = ClientRectangle;
            using (LinearGradientBrush bg = new LinearGradientBrush(r, Color.FromArgb(18, 18, 30), Color.FromArgb(9, 9, 15), 90f))
                g.FillRectangle(bg, r);

            /* brilho violeta suave no topo, o "futuro" do cartão */
            using (GraphicsPath glow = new GraphicsPath())
            {
                glow.AddEllipse(r.Width / 2 - 190, -170, 380, 300);
                using (PathGradientBrush pb = new PathGradientBrush(glow))
                {
                    pb.CenterColor = Color.FromArgb(64, 124, 58, 237);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 124, 58, 237) };
                    g.FillPath(pb, glow);
                }
            }

            using (Pen border = new Pen(Color.FromArgb(55, 255, 255, 255)))
                g.DrawRectangle(border, 0, 0, r.Width - 1, r.Height - 1);

            if (_logo != null)
            {
                int side = 92;
                g.DrawImage(_logo, new Rectangle((r.Width - side) / 2, 34, side, side));
            }

            using (StringFormat center = new StringFormat())
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Near;

                using (SolidBrush ink = new SolidBrush(Color.FromArgb(245, 244, 247)))
                    g.DrawString("3D Browser Pro", _title, ink, new RectangleF(0, 140, r.Width, 34), center);

                using (SolidBrush dim = new SolidBrush(Color.FromArgb(143, 143, 168)))
                    g.DrawString("capturar · deduzir esqueleto · baixar", _sub, dim, new RectangleF(0, 174, r.Width, 20), center);
            }

            DrawCredit(g, r);

            using (SolidBrush faint = new SolidBrush(Color.FromArgb(92, 92, 120)))
            using (StringFormat center = new StringFormat())
            {
                center.Alignment = StringAlignment.Center;
                g.DrawString("clique para continuar", _hint, faint, new RectangleF(0, 264, r.Width, 18), center);
            }
        }

        private void DrawCredit(Graphics g, Rectangle r)
        {
            string lead = CreditLead;
            string name = CreditName;

            SizeF wa = g.MeasureString(lead, _credit);
            SizeF wb = g.MeasureString(name, _credit);
            float x = (r.Width - (wa.Width + wb.Width)) / 2f;
            float y = 214f;

            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 150, 172)))
                g.DrawString(lead, _credit, dim, x, y);

            Color hi = _creditHover ? Color.FromArgb(205, 182, 255) : Color.FromArgb(169, 139, 255);
            using (SolidBrush accent = new SolidBrush(hi))
                g.DrawString(name, _credit, accent, x + wa.Width, y);

            _creditBox = new Rectangle((int)(x + wa.Width), (int)y + 1, (int)wb.Width + 2, (int)wb.Height);

            if (_creditHover)
            {
                using (Pen underline = new Pen(hi, 1f))
                    g.DrawLine(underline, x + wa.Width, y + wb.Height - 2, x + wa.Width + wb.Width, y + wb.Height - 2);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = _creditBox.Contains(e.Location);
            if (over == _creditHover) return;
            _creditHover = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_creditBox.Contains(e.Location)) Shell.OpenUrl(CreditUrl);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _life.Stop();
            _life.Dispose();
            if (_player != null) { _player.Dispose(); _player = null; }
            if (_audio != null) { _audio.Dispose(); _audio = null; }
            if (_logo != null) { _logo.Dispose(); _logo = null; }
            _title.Dispose();
            _sub.Dispose();
            _credit.Dispose();
            _hint.Dispose();
            base.OnFormClosed(e);
        }

        private void PlaySound()
        {
            try
            {
                _audio = new MemoryStream(FutureTone());
                _player = new SoundPlayer(_audio);
                _player.Play();
            }
            catch (Exception ex)
            {
                Log.W("splash: som indisponível (" + ex.Message + ")");
            }
        }

        /* Acorde ascendente curto, sintetizado aqui mesmo (sem arquivo de áudio):
           quatro notas com queda exponencial + um brilho agudo e tremido por cima. */
        public static byte[] FutureTone()
        {
            const int rate = 44100;
            const double dur = 1.7;
            int n = (int)(rate * dur);
            short[] pcm = new short[n];
            double[] notes = { 523.25, 659.25, 783.99, 1046.50 };

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double v = 0;

                for (int k = 0; k < notes.Length; k++)
                {
                    double lt = t - 0.13 * k;
                    if (lt <= 0) continue;
                    double env = Math.Min(1.0, lt / 0.012) * Math.Exp(-lt * 2.1);
                    double f = notes[k];
                    v += env * (Math.Sin(2 * Math.PI * f * lt) + 0.22 * Math.Sin(2 * Math.PI * f * 3 * lt));
                }

                double air = 0.05 * Math.Sin(2 * Math.PI * 2093.0 * t) * Math.Exp(-t * 2.6);
                air *= 0.65 + 0.35 * Math.Sin(2 * Math.PI * 6.0 * t);

                v = (v + air) * Math.Min(1.0, t / 0.02) * Math.Min(1.0, (dur - t) / 0.4);
                if (v > 1) v = 1; else if (v < -1) v = -1;
                pcm[i] = (short)(v * 0.30 * 32767);
            }

            return Wav(pcm, rate);
        }

        /* WAV PCM 16 bits mono, cabeçalho canônico de 44 bytes */
        private static byte[] Wav(short[] pcm, int rate)
        {
            int data = pcm.Length * 2;
            using (MemoryStream ms = new MemoryStream(44 + data))
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(new char[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + data);
                w.Write(new char[] { 'W', 'A', 'V', 'E' });
                w.Write(new char[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new char[] { 'd', 'a', 't', 'a' });
                w.Write(data);
                for (int i = 0; i < pcm.Length; i++) w.Write(pcm[i]);
                w.Flush();
                return ms.ToArray();
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
