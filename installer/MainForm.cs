using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DpsMetersInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    /// <summary>Couleurs, polices et petites aides de dessin (sombre, accent braise).</summary>
    internal static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(14, 15, 19);
        public static readonly Color Surface = Color.FromArgb(24, 26, 33);
        public static readonly Color SurfaceHover = Color.FromArgb(30, 33, 42);
        public static readonly Color Line = Color.FromArgb(40, 44, 55);
        public static readonly Color Text = Color.FromArgb(238, 240, 245);
        public static readonly Color Muted = Color.FromArgb(150, 156, 172);
        public static readonly Color Accent = Color.FromArgb(255, 122, 61);
        public static readonly Color Accent2 = Color.FromArgb(232, 54, 107);
        public static readonly Color Ok = Color.FromArgb(61, 220, 132);
        public static readonly Color Warn = Color.FromArgb(255, 196, 77);
        public static readonly Color Err = Color.FromArgb(255, 99, 99);

        public static readonly Font Title = new Font("Segoe UI Semibold", 20f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 12.5f);
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.75f);
        public static readonly Font Button = new Font("Segoe UI Semibold", 10f);

        /// <summary>Échelle de l'écran (1 à 96 dpi).</summary>
        public static float S = 1f;
        public static int D(float v) => (int)Math.Round(v * S);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF r, bool ellipsis = true, StringAlignment align = StringAlignment.Near)
        {
            using (var b = new SolidBrush(color))
            using (var f = new StringFormat { Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None, Alignment = align, LineAlignment = StringAlignment.Near })
            {
                if (ellipsis) f.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text ?? "", font, b, r, f);
            }
        }

        public static SizeF Measure(Graphics g, string text, Font font) => g.MeasureString(text ?? "", font);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Barre de titre sombre (Windows 10 20H1+ et 11).</summary>
        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch (Exception)
            {
                // vieux Windows : barre claire
            }
        }
    }

    /// <summary>Bouton arrondi dessiné à la main : plein (dégradé) ou discret.</summary>
    internal sealed class FlatButton : Control
    {
        public bool Primary;
        private bool _hover, _down;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = Ui.Button;
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Prepare(g);
            g.Clear(Parent?.BackColor ?? Ui.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(r, Ui.D(10)))
            {
                if (Primary)
                {
                    Color a = Enabled ? Ui.Accent : Color.FromArgb(70, 72, 82), b = Enabled ? Ui.Accent2 : Color.FromArgb(60, 62, 72);
                    using (var br = new LinearGradientBrush(r, a, b, LinearGradientMode.Horizontal)) g.FillPath(br, path);
                    if (Enabled && (_hover || _down))
                        using (var veil = new SolidBrush(Color.FromArgb(_down ? 40 : 25, _down ? Color.Black : Color.White))) g.FillPath(veil, path);
                }
                else
                {
                    using (var br = new SolidBrush(_hover ? Ui.SurfaceHover : Ui.Surface)) g.FillPath(br, path);
                    using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, path);
                }
            }
            Ui.DrawText(g, Text, Font, Enabled ? (Primary ? Color.White : Ui.Text) : Ui.Muted,
                new RectangleF(0, (Height - Ui.Measure(g, "Ag", Font).Height) / 2f - 1, Width, Height), true, StringAlignment.Center);
        }
    }

    /// <summary>Une carte par DPS meter : image du jeu, description, état et interrupteur.</summary>
    internal sealed class MeterCard : Control
    {
        public readonly Meter Meter;
        public Image Cover;
        public bool Busy;
        public event Action<MeterCard> ChooseFolder, Uninstall, Changed;

        private bool _hover;
        private RectangleF _toggle, _link1, _link2;
        private string _link1Text;

        public MeterCard(Meter meter)
        {
            Meter = meter;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Ui.D(124);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hand = !Busy && (Hit(_link1, e) || Hit(_link2, e) || (Meter.Found && (Hit(_toggle, e) || e.X < _toggle.Left)));
            Cursor = hand ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (Busy || e.Button != MouseButtons.Left) return;
            if (Hit(_link1, e))
            {
                if (_link1Text == LinkChoose) ChooseFolder?.Invoke(this);
                else if (_link1Text == LinkUninstall) Uninstall?.Invoke(this);
                return;
            }
            if (Hit(_link2, e)) { ChooseFolder?.Invoke(this); return; }
            if (!Meter.Found) { ChooseFolder?.Invoke(this); return; }
            Meter.Selected = !Meter.Selected;
            if (Meter.State != MeterState.Working) { Meter.State = MeterState.Idle; Meter.Message = null; }
            Invalidate();
            Changed?.Invoke(this);
        }

        private static bool Hit(RectangleF r, MouseEventArgs e) => !r.IsEmpty && r.Contains(e.Location);

        private const string LinkChoose = "Choisir le dossier du jeu…";
        private const string LinkUninstall = "Désinstaller";

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Prepare(g);
            g.Clear(Parent?.BackColor ?? Ui.Bg);
            var m = Meter;
            float pad = Ui.D(12);

            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(card, Ui.D(14)))
            {
                using (var br = new SolidBrush(_hover && !Busy ? Ui.SurfaceHover : Ui.Surface)) g.FillPath(br, path);
                Color border = m.Selected ? Color.FromArgb(150, Ui.Accent) : Ui.Line;
                using (var pen = new Pen(border, m.Selected ? 1.5f : 1f)) g.DrawPath(pen, path);
            }

            // Image du jeu (en-tête Steam 460x215), ou un dégradé avec l'initiale
            float ih = Height - 2 * pad, iw = ih * 460f / 215f;
            var img = new RectangleF(pad, pad, iw, ih);
            using (var clip = Ui.Round(img, Ui.D(9)))
            {
                var state = g.Save();
                g.SetClip(clip);
                if (Cover != null) g.DrawImage(Cover, img);
                else
                {
                    using (var br = new LinearGradientBrush(img, Color.FromArgb(60, Ui.Accent), Color.FromArgb(60, Ui.Accent2), 45f)) g.FillRectangle(br, img);
                    Ui.DrawText(g, m.Game, Ui.H2, Ui.Text, new RectangleF(img.X, img.Y + img.Height / 2 - Ui.D(12), img.Width, Ui.D(30)), true, StringAlignment.Center);
                }
                g.Restore(state);
            }

            // Interrupteur
            float tw = Ui.D(44), th = Ui.D(24);
            _toggle = new RectangleF(Width - pad - Ui.D(6) - tw, (Height - th) / 2f, tw, th);
            if (m.Found)
            {
                using (var path = Ui.Round(_toggle, th / 2))
                {
                    if (m.Selected)
                        using (var br = new LinearGradientBrush(_toggle, Ui.Accent, Ui.Accent2, LinearGradientMode.Horizontal)) g.FillPath(br, path);
                    else
                        using (var br = new SolidBrush(Ui.Line)) g.FillPath(br, path);
                }
                float k = th - Ui.D(6);
                float kx = m.Selected ? _toggle.Right - Ui.D(3) - k : _toggle.X + Ui.D(3);
                using (var br = new SolidBrush(m.Selected ? Color.White : Ui.Muted)) g.FillEllipse(br, kx, _toggle.Y + Ui.D(3), k, k);
            }
            else _toggle = RectangleF.Empty;

            // Textes
            float x = img.Right + Ui.D(18), w = (m.Found ? _toggle.Left : Width - pad) - x - Ui.D(14);
            float y = pad + Ui.D(2);
            Ui.DrawText(g, m.Name, Ui.H2, Ui.Text, new RectangleF(x, y, w, Ui.D(26)));
            y += Ui.D(25);
            Ui.DrawText(g, "pour " + m.Game, Ui.Small, Ui.Muted, new RectangleF(x, y, w, Ui.D(18)));
            y += Ui.D(22);

            bool done = m.State == MeterState.Done;
            string line = done ? m.After : m.Tagline;
            using (var f = new StringFormat { Trimming = StringTrimming.EllipsisWord })
            using (var br = new SolidBrush(done ? Color.FromArgb(200, Ui.Text) : Ui.Text))
                g.DrawString(line ?? "", Ui.Body, br, new RectangleF(x, y, w, Ui.D(38)), f);

            // Ligne d'état
            float sy = Height - pad - Ui.D(18);
            Color dot;
            string status;
            _link1Text = null;
            string link2 = null;
            switch (m.State)
            {
                case MeterState.Working: dot = Ui.Accent; status = m.Message ?? "Installation…"; break;
                case MeterState.Done: dot = Ui.Ok; status = "Installé" + (m.Latest != null ? " · v" + m.Latest : ""); break;
                case MeterState.Error: dot = Ui.Err; status = m.Message; break;
                default:
                    if (!m.Found) { dot = Ui.Warn; status = "Jeu non trouvé"; _link1Text = LinkChoose; }
                    else if (m.Installed)
                    {
                        dot = Ui.Ok;
                        status = "Installé · mises à jour automatiques";
                        _link1Text = LinkUninstall;
                    }
                    else
                    {
                        dot = Ui.Muted;
                        status = "Jeu trouvé" + (m.Latest != null ? " · v" + m.Latest + " disponible" : "");
                        link2 = "Autre dossier…";
                    }
                    break;
            }
            float ds = Ui.D(8);
            using (var br = new SolidBrush(dot)) g.FillEllipse(br, x, sy + Ui.D(5), ds, ds);
            float tx = x + ds + Ui.D(8);
            Color sc = m.State == MeterState.Error ? Ui.Err : Ui.Muted;
            float maxStatus = w - (tx - x);
            float statusW = Math.Min(Ui.Measure(g, status, Ui.Small).Width + Ui.D(4), maxStatus);
            Ui.DrawText(g, status, Ui.Small, sc, new RectangleF(tx, sy, statusW, Ui.D(18)));
            float lx = tx + statusW + Ui.D(10);

            _link1 = _link2 = RectangleF.Empty;
            if (!Busy && _link1Text != null)
            {
                float lw = Ui.Measure(g, _link1Text, Ui.SmallBold).Width + Ui.D(4);
                _link1 = new RectangleF(lx, sy, lw, Ui.D(18));
                Ui.DrawText(g, _link1Text, Ui.SmallBold, _link1Text == LinkUninstall ? Ui.Muted : Ui.Accent, _link1);
            }
            else if (!Busy && link2 != null && lx + Ui.D(90) < x + w)
            {
                float lw = Ui.Measure(g, link2, Ui.Small).Width + Ui.D(4);
                _link2 = new RectangleF(lx, sy, lw, Ui.D(18));
                Ui.DrawText(g, link2, Ui.Small, Color.FromArgb(170, Ui.Accent), _link2);
            }
        }
    }

    /// <summary>Barre de progression fine, indéterminée (balayage) pendant l'installation.</summary>
    internal sealed class Progress : Control
    {
        private readonly Timer _timer = new Timer { Interval = 16 };
        private float _t;
        public bool Running { get => _timer.Enabled; set { _timer.Enabled = value; Invalidate(); } }

        public Progress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _timer.Tick += (s, e) => { _t = (_t + 0.012f) % 1.4f; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Prepare(g);
            g.Clear(Parent?.BackColor ?? Ui.Bg);
            if (!Running) return;
            var track = new RectangleF(0, 0, Width, Height);
            using (var p = Ui.Round(track, Height / 2f)) using (var br = new SolidBrush(Ui.Line)) g.FillPath(br, p);
            float seg = Width * 0.35f, x = (_t - 0.2f) * Width;
            var r = RectangleF.Intersect(track, new RectangleF(x, 0, seg, Height));
            if (r.Width < 1) return;
            using (var p = Ui.Round(r, Height / 2f))
            using (var br = new LinearGradientBrush(new RectangleF(x - 1, 0, seg + 2, Height), Ui.Accent, Ui.Accent2, LinearGradientMode.Horizontal))
                g.FillPath(br, p);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Panel _list = new Panel { AutoScroll = true, BackColor = Ui.Bg };
        private readonly FlatButton _install = new FlatButton { Primary = true, Text = "Installer" };
        private readonly FlatButton _close = new FlatButton { Text = "Fermer" };
        private readonly Progress _progress = new Progress();
        private readonly Label _status = new Label { ForeColor = Ui.Muted, Font = Ui.Small, AutoSize = false, BackColor = Ui.Bg, TextAlign = ContentAlignment.MiddleLeft };
        private readonly LinkLabel _sig = new LinkLabel { Text = "un projet du bored dev playground", Font = Ui.Small, AutoSize = true, BackColor = Ui.Bg };
        private readonly List<MeterCard> _cards = new List<MeterCard>();
        private Image _logo;
        private bool _busy;

        public MainForm()
        {
            using (var g = CreateGraphics()) Ui.S = g.DpiX / 96f;
            Text = "DPS meters";
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = Ui.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Ui.D(780), Ui.D(560));
            DoubleBuffered = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                _logo = new Icon(Icon, Ui.D(48), Ui.D(48)).ToBitmap();
            }
            catch (Exception)
            {
                // icône par défaut
            }

            _sig.LinkColor = _sig.ActiveLinkColor = _sig.VisitedLinkColor = Color.FromArgb(120, 126, 142);
            _sig.LinkBehavior = LinkBehavior.HoverUnderline;
            _sig.LinkClicked += (s, e) => Setup.Open(Repo.Playground);
            _install.Click += async (s, e) => await InstallSelected();
            _close.Click += (s, e) => Close();
            _install.Enabled = false;

            Controls.AddRange(new Control[] { _list, _install, _close, _progress, _status, _sig });
            Layout += (s, e) => DoLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.DarkTitleBar(Handle);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await LoadCatalog();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                SetStatus("Attends la fin de l'installation…", Ui.Warn);
            }
            base.OnFormClosing(e);
        }

        private const int HeaderH = 132, FooterH = 92, Side = 28;

        private void DoLayout()
        {
            int m = Ui.D(Side);
            int footerTop = ClientSize.Height - Ui.D(FooterH);
            _list.SetBounds(m, Ui.D(HeaderH), ClientSize.Width - 2 * m, footerTop - Ui.D(HeaderH) - Ui.D(8));
            LayoutCards();
            int bh = Ui.D(42), by = ClientSize.Height - m - bh + Ui.D(4);
            _install.SetBounds(ClientSize.Width - m - Ui.D(170), by, Ui.D(170), bh);
            _close.SetBounds(_install.Left - Ui.D(12) - Ui.D(110), by, Ui.D(110), bh);
            _progress.SetBounds(m, footerTop + Ui.D(6), ClientSize.Width - 2 * m, Ui.D(4));
            _status.SetBounds(m, by - Ui.D(2), _close.Left - m - Ui.D(16), Ui.D(24));
            _sig.Location = new Point(m - Ui.D(2), by + Ui.D(24));
        }

        private void LayoutCards()
        {
            int y = 0, gap = Ui.D(12);
            bool scroll = _cards.Sum(c => c.Height + gap) > _list.ClientSize.Height;
            int w = _list.ClientSize.Width - (scroll ? Ui.D(6) : 0);
            foreach (var c in _cards)
            {
                c.SetBounds(0, y + _list.AutoScrollPosition.Y, w, c.Height);
                y += c.Height + gap;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Prepare(g);
            int m = Ui.D(Side);

            // Halo braise discret derrière le titre
            var halo = new RectangleF(-Ui.D(120), -Ui.D(160), Ui.D(560), Ui.D(320));
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(halo);
                using (var br = new PathGradientBrush(p) { CenterColor = Color.FromArgb(38, Ui.Accent), SurroundColors = new[] { Color.FromArgb(0, Ui.Bg) } })
                    g.FillEllipse(br, halo);
            }

            int ls = Ui.D(48);
            if (_logo != null) g.DrawImage(_logo, m, Ui.D(34), ls, ls);
            float tx = m + ls + Ui.D(16);
            Ui.DrawText(g, "DPS meters", Ui.Title, Ui.Text, new RectangleF(tx, Ui.D(26), Ui.D(400), Ui.D(44)));
            Ui.DrawText(g, "Choisis tes jeux puis clique sur Installer. Les mises à jour se feront ensuite toutes seules.",
                Ui.Body, Ui.Muted, new RectangleF(tx, Ui.D(70), ClientSize.Width - tx - m, Ui.D(22)));
            using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, m, Ui.D(HeaderH) - Ui.D(16), ClientSize.Width - m, Ui.D(HeaderH) - Ui.D(16));
        }

        private void SetStatus(string text, Color? color = null)
        {
            _status.Text = text;
            _status.ForeColor = color ?? Ui.Muted;
        }

        private async Task LoadCatalog()
        {
            _progress.Running = true;
            SetStatus("Recherche des jeux…");
            List<Meter> meters;
            try
            {
                meters = await Task.Run(() => Catalog.Load());
            }
            catch (Exception ex)
            {
                _progress.Running = false;
                SetStatus("Impossible de joindre GitHub (" + ex.Message + "). Vérifie ta connexion puis relance l'installeur.", Ui.Err);
                return;
            }
            _progress.Running = false;

            foreach (var m in meters)
            {
                var card = new MeterCard(m);
                card.Changed += c => RefreshButton();
                card.ChooseFolder += ChooseFolder;
                card.Uninstall += async c => await UninstallMeter(c);
                _cards.Add(card);
                _list.Controls.Add(card);
                LoadCover(card);
            }
            LayoutCards();
            RefreshButton();
            int found = meters.Count(x => x.Found);
            SetStatus(found == 0 ? "Aucun jeu trouvé automatiquement : choisis le dossier du jeu sur sa carte."
                : found + (found > 1 ? " jeux trouvés." : " jeu trouvé."));
        }

        private async void LoadCover(MeterCard card)
        {
            if (card.Meter.SteamAppId <= 0) return;
            try
            {
                byte[] data = await Task.Run(() => Repo.Get("https://cdn.cloudflare.steamstatic.com/steam/apps/" + card.Meter.SteamAppId + "/header.jpg"));
                card.Cover = Image.FromStream(new MemoryStream(data));
                card.Invalidate();
            }
            catch (Exception)
            {
                // pas d'image : la carte garde son dégradé
            }
        }

        private void RefreshButton()
        {
            int n = _cards.Count(c => c.Meter.Selected && c.Meter.Found);
            bool update = n > 0 && _cards.Where(c => c.Meter.Selected && c.Meter.Found).All(c => c.Meter.Installed);
            _install.Enabled = n > 0 && !_busy;
            _install.Text = n == 0 ? "Installer" : (update ? "Réinstaller" : "Installer") + (n > 1 ? " (" + n + ")" : "");
        }

        private void ChooseFolder(MeterCard card)
        {
            var m = card.Meter;
            using (var dlg = new FolderBrowserDialog { Description = "Dossier de " + m.Game + " (celui qui contient " + m.Exe + ")", ShowNewFolderButton = false })
            {
                if (m.Found) dlg.SelectedPath = m.GamePath;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(dlg.SelectedPath, m.Exe)))
                {
                    SetStatus(m.Exe + " n'est pas dans ce dossier : choisis le dossier du jeu.", Ui.Warn);
                    return;
                }
                m.GamePath = dlg.SelectedPath;
                m.Selected = !m.Installed;
                m.State = MeterState.Idle;
                SetStatus(m.Game + " : dossier choisi.");
                card.Invalidate();
                RefreshButton();
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (var c in _cards) { c.Busy = busy; c.Invalidate(); }
            _progress.Running = busy;
            _close.Enabled = !busy;
            RefreshButton();
        }

        private async Task InstallSelected()
        {
            var todo = _cards.Where(c => c.Meter.Selected && c.Meter.Found).ToList();
            if (todo.Count == 0) return;
            SetBusy(true);
            int ok = 0;
            foreach (var card in todo)
            {
                var m = card.Meter;
                m.State = MeterState.Working;
                m.Message = "Installation…";
                card.Invalidate();
                SetStatus(m.Name + " : installation…");
                try
                {
                    await Task.Run(() => Setup.Install(m, msg => BeginInvoke(new Action(() => { m.Message = msg; card.Invalidate(); }))));
                    m.State = MeterState.Done;
                    m.Selected = false;
                    ok++;
                }
                catch (Exception ex)
                {
                    m.State = MeterState.Error;
                    m.Message = ex is UserError ? ex.Message : "Échec : " + ex.Message;
                }
                card.Invalidate();
            }
            SetBusy(false);
            if (ok == todo.Count) SetStatus(ok > 1 ? "C'est installé ! Lis les cartes pour la dernière étape." : "C'est installé ! Lis la carte pour la dernière étape.", Ui.Ok);
            else SetStatus("Il y a eu un souci : le détail est sur la carte concernée.", Ui.Err);
        }

        private async Task UninstallMeter(MeterCard card)
        {
            var m = card.Meter;
            if (MessageBox.Show(this, "Désinstaller " + m.Name + " de " + m.Game + " ?", "DPS meters", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true);
            try
            {
                await Task.Run(() => Setup.Uninstall(m));
                m.State = MeterState.Idle;
                m.Selected = false;
                SetStatus(m.Name + " désinstallé.");
            }
            catch (Exception ex)
            {
                m.State = MeterState.Error;
                m.Message = ex is UserError ? ex.Message : "Échec : " + ex.Message;
            }
            SetBusy(false);
            card.Invalidate();
        }
    }
}
