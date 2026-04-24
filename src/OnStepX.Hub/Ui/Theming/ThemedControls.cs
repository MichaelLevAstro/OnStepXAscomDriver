using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ASCOM.OnStepX.Ui.Theming
{
    // Apply the DarkMode_Explorer Windows theme to a control's HWND so OS-rendered
    // scrollbars (on Panel, RichTextBox, ListView, etc.) pick up dark colors on
    // Windows 10 1809+. No-op on older Windows — uxtheme returns non-zero and the
    // control silently falls back to the light theme. Must be called after the
    // handle is created; the helper auto-subscribes HandleCreated so callers can
    // invoke it at construction time.
    internal static class DarkScroll
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static void Apply(Control c)
        {
            if (c == null) return;
            void apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) apply();
            c.HandleCreated += (s, e) => apply();
        }
    }

    // Rounded-rect helper.
    internal static class GdiExt
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width)  d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void SmoothText(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }
    }

    // Flat, themed button. Inherits Control (not Button) so native Windows visual
    // styling never paints text/glyphs on top of our custom render — fixes duplicate
    // text artifacts when Enabled toggles under Visual Styles.
    // Implements IButtonControl so Form.AcceptButton/CancelButton + DialogResult
    // auto-close semantics still work in dialogs.
    internal class FlatButton : Control, IButtonControl
    {
        public enum Variant { Default, Primary, Danger, Ghost }
        public enum ButtonSize { Normal, Small }

        private Variant _variant = Variant.Default;
        private ButtonSize _size = ButtonSize.Normal;
        private bool _hover, _down;
        private DialogResult _dialogResult = DialogResult.None;

        public Variant Kind
        {
            get => _variant;
            set { _variant = value; Invalidate(); }
        }

        public ButtonSize Sz
        {
            get => _size;
            set { _size = value; ApplySize(); Invalidate(); }
        }

        public DialogResult DialogResult
        {
            get => _dialogResult;
            set => _dialogResult = value;
        }

        public void NotifyDefault(bool value) { }

        public void PerformClick()
        {
            if (CanSelect) OnClick(EventArgs.Empty);
        }

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.Selectable
                   | ControlStyles.StandardClick
                   | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = true;
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            ApplySize();
            Theme.Changed += (s, e) => Invalidate();
        }

        private void ApplySize()
        {
            int h = _size == ButtonSize.Small ? 24 : 28;
            if (Height != h) Height = h;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { Focus(); _down = true; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { _down = true; Invalidate(); }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && _down) { _down = false; Invalidate(); OnClick(EventArgs.Empty); }
            else if (e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e)
        {
            if (_dialogResult != DialogResult.None)
            {
                var form = FindForm();
                if (form != null) form.DialogResult = _dialogResult;
            }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GdiExt.SmoothText(g);

            Color bg, border, fg;
            bool enabled = Enabled;
            switch (_variant)
            {
                case Variant.Primary:
                    bg = _down ? ControlPaint.Dark(p.Accent, 0.05f)
                       : _hover ? ControlPaint.Light(p.Accent, 0.1f)
                       : p.Accent;
                    border = p.Accent;
                    fg = p.AccentInk;
                    break;
                case Variant.Danger:
                    bg = _down ? ControlPaint.Dark(p.Danger, 0.05f)
                       : _hover ? ControlPaint.Light(p.Danger, 0.1f)
                       : p.Danger;
                    border = p.Danger;
                    fg = p.AccentInk;
                    break;
                case Variant.Ghost:
                    bg = _down ? p.BtnBgActive : _hover ? p.BtnBgHover : Color.Transparent;
                    border = Color.Transparent;
                    fg = p.Text;
                    break;
                default:
                    bg = _down ? p.BtnBgActive : _hover ? p.BtnBgHover : p.BtnBg;
                    border = _hover ? p.InputBorderHover : p.InputBorder;
                    fg = p.Text;
                    break;
            }
            if (!enabled)
            {
                bg = Color.FromArgb(115, bg);
                fg = Color.FromArgb(115, fg);
            }

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = GdiExt.Rounded(rect, 4))
            using (var br = new SolidBrush(bg))
                g.FillPath(br, path);

            if (border.A > 0)
            {
                using (var path = GdiExt.Rounded(rect, 4))
                using (var pen = new Pen(border, 1))
                    g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical),
                fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && enabled)
            {
                var fr = new Rectangle(2, 2, Width - 5, Height - 5);
                using (var pen = new Pen(Color.FromArgb(80, p.Accent), 1) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, fr);
            }
        }
    }

    // Section panel: header with uppercase title, chevron, collapsible body. Replaces GroupBox.
    // Children added via Controls.Add(x) get auto-reparented to Body, so legacy GroupBox
    // code that does `g.Controls.Add(...)` keeps working.
    internal class SectionPanel : Panel
    {
        private const int HeaderHeight = 32;
        private readonly Panel _header;
        private readonly Panel _body;
        private readonly Label _titleLabel;
        private readonly Label _chev;
        private int _expandedHeight;
        private bool _collapsed;
        private bool _builtinsAdded;

        public Panel Body => _body;
        public Control HeaderRight { get; set; }
        private Control _headerRightHost;

        public string Title
        {
            get => _titleLabel.Text;
            set => _titleLabel.Text = (value ?? "").ToUpperInvariant();
        }

        public SectionPanel()
        {
            DoubleBuffered = true;
            Padding = new Padding(1);
            Margin = new Padding(0, 0, 0, 8);

            _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, Cursor = Cursors.Hand };
            _header.Paint += PaintHeader;
            _header.Click += (s, e) => Toggle();

            _titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Text = "",
                UseMnemonic = false,
            };
            _titleLabel.Click += (s, e) => Toggle();

            _chev = new Label
            {
                Text = "\u25BC",
                AutoSize = false,
                Dock = DockStyle.Right,
                Width = 26,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8f),
            };
            _chev.Click += (s, e) => Toggle();

            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_chev);

            _body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };

            Controls.Add(_body);
            Controls.Add(_header);
            _builtinsAdded = true;

            Theme.Changed += (s, e) => { ApplyTheme(); Invalidate(); };
            ApplyTheme();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            // After the header+body are in place, any further child added to `this`
            // gets reparented to Body — preserves legacy `g.Controls.Add(x)` usage.
            if (!_builtinsAdded) return;
            var c = e.Control;
            if (c == null || c == _header || c == _body) return;
            if (c.Parent == this)
            {
                this.Controls.Remove(c);
                _body.Controls.Add(c);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (HeaderRight != null && _headerRightHost == null)
            {
                _headerRightHost = HeaderRight;
                HeaderRight.Parent = _header;
                HeaderRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                PositionHeaderRight();
            }
            else if (HeaderRight != null)
            {
                PositionHeaderRight();
            }
        }

        private void PositionHeaderRight()
        {
            if (_headerRightHost == null) return;
            _headerRightHost.Top = (HeaderHeight - _headerRightHost.Height) / 2;
            _headerRightHost.Left = _chev.Left - _headerRightHost.Width - 4;
        }

        private void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Panel2;
            _header.BackColor = p.Panel2;
            _body.BackColor = p.Panel2;
            _titleLabel.ForeColor = p.Text;
            _titleLabel.BackColor = p.Panel2;
            _chev.ForeColor = p.TextFaint;
            _chev.BackColor = p.Panel2;
        }

        private void PaintHeader(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            // Border bottom for header when expanded
            if (!_collapsed)
            {
                using (var pen = new Pen(p.Border, 1))
                    g.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var p = Theme.P;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var pen = new Pen(p.Border, 1))
                g.DrawRectangle(pen, rect);
        }

        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                if (value == _collapsed) return;
                if (value) Collapse(); else Expand();
            }
        }

        public void Toggle()
        {
            if (_collapsed) Expand(); else Collapse();
        }

        public void Collapse()
        {
            if (_collapsed) return;
            if (Height > HeaderHeight) _expandedHeight = Height;
            _body.Visible = false;
            _collapsed = true;
            _chev.Text = "\u25B6";
            Height = HeaderHeight + 2;
            Invalidate();
        }

        public void Expand()
        {
            if (!_collapsed) return;
            _body.Visible = true;
            _collapsed = false;
            _chev.Text = "\u25BC";
            Height = _expandedHeight > HeaderHeight ? _expandedHeight : 120;
            Invalidate();
        }
    }

    // Status dot with optional pulsing ring animation. Reuses a single app-wide Timer.
    internal class PulseDot : Control
    {
        public enum StatusKind { Neutral, Ok, Warn, Err, Info }
        private StatusKind _kind = StatusKind.Neutral;
        private bool _pulse;
        private float _phase;
        private static readonly Timer _tick = new Timer { Interval = 40 };
        private static readonly HashSet<PulseDot> _active = new HashSet<PulseDot>();

        public StatusKind Kind
        {
            get => _kind;
            set { _kind = value; UpdatePulseRegistration(); Invalidate(); }
        }

        public bool Pulsing
        {
            get => _pulse;
            set { _pulse = value; UpdatePulseRegistration(); Invalidate(); }
        }

        static PulseDot()
        {
            _tick.Tick += (s, e) =>
            {
                foreach (var d in _active)
                {
                    d._phase += 0.03f;
                    if (d._phase > 1f) d._phase -= 1f;
                    try { d.Invalidate(); } catch { }
                }
            };
        }

        public PulseDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            Width = 16;
            Height = 16;
            Theme.Changed += (s, e) => Invalidate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _active.Remove(this);
            if (_active.Count == 0) _tick.Stop();
            base.OnHandleDestroyed(e);
        }

        private void UpdatePulseRegistration()
        {
            bool shouldPulse = _pulse && _kind == StatusKind.Ok;
            if (shouldPulse)
            {
                if (_active.Add(this) && _active.Count == 1) _tick.Start();
            }
            else
            {
                if (_active.Remove(this) && _active.Count == 0) _tick.Stop();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = Theme.P;
            Color c;
            switch (_kind)
            {
                case StatusKind.Ok:   c = p.Ok; break;
                case StatusKind.Warn: c = p.Warn; break;
                case StatusKind.Err:  c = p.Danger; break;
                case StatusKind.Info: c = p.Info; break;
                default:              c = p.TextFaint; break;
            }
            int cx = Width / 2, cy = Height / 2;
            // Outer ring (either static halo or animated pulse).
            if (_pulse && _kind == StatusKind.Ok)
            {
                float ph = _phase;
                float haloR = 3 + ph * 5;
                int alpha = (int)(90 * (1f - ph));
                using (var br = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c)))
                    g.FillEllipse(br, cx - haloR, cy - haloR, haloR * 2, haloR * 2);
            }
            else
            {
                using (var br = new SolidBrush(Color.FromArgb(80, c)))
                    g.FillEllipse(br, cx - 6, cy - 6, 12, 12);
            }
            using (var br = new SolidBrush(c))
                g.FillEllipse(br, cx - 4, cy - 4, 8, 8);
        }
    }

    // Status row: pulse dot + text label. StatusLabel used anywhere a label+dot appears.
    internal sealed class StatusLabel : Control
    {
        private readonly PulseDot _dot;
        private readonly Label _text;

        public PulseDot.StatusKind Kind
        {
            get => _dot.Kind;
            set
            {
                _dot.Kind = value;
                var p = Theme.P;
                Color c;
                switch (value)
                {
                    case PulseDot.StatusKind.Ok:   c = p.Ok; break;
                    case PulseDot.StatusKind.Warn: c = p.Warn; break;
                    case PulseDot.StatusKind.Err:  c = p.Danger; break;
                    case PulseDot.StatusKind.Info: c = p.Info; break;
                    default:                        c = p.TextFaint; break;
                }
                _text.ForeColor = c;
            }
        }

        public bool Pulsing { get => _dot.Pulsing; set => _dot.Pulsing = value; }

        public override string Text
        {
            get => _text?.Text ?? "";
            set { if (_text != null) _text.Text = value; }
        }

        public StatusLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            _dot = new PulseDot { Left = 0, Top = 4, Width = 16, Height = 16 };
            _text = new Label
            {
                AutoSize = true,
                Left = 18, Top = 2,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                BackColor = Color.Transparent,
            };
            Controls.Add(_dot);
            Controls.Add(_text);
            Height = 20;
            Theme.Changed += (s, e) => Kind = _dot.Kind;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _dot.Top = (Height - _dot.Height) / 2;
            _text.Top = (Height - _text.Height) / 2;
        }
    }

    // Sun/moon theme toggle (two buttons in a rounded chip).
    internal sealed class ThemeToggle : Control
    {
        private readonly SideButton _dark, _light;

        public ThemeToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            Height = 28;
            Width = 60;

            _dark = new SideButton(isDark: true) { Dock = DockStyle.Left, Width = 28 };
            _light = new SideButton(isDark: false) { Dock = DockStyle.Right, Width = 28 };
            _dark.Click += (s, e) => Set(ThemeMode.Dark);
            _light.Click += (s, e) => Set(ThemeMode.Light);
            Controls.Add(_light);
            Controls.Add(_dark);

            Theme.Changed += (s, e) => { Reflect(); Invalidate(); };
            Reflect();
        }

        private void Set(ThemeMode m)
        {
            if (Theme.Mode == m) return;
            Theme.SetMode(m);
        }

        private void Reflect()
        {
            _dark.Active = Theme.Mode == ThemeMode.Dark;
            _light.Active = Theme.Mode == ThemeMode.Light;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = Theme.P;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = GdiExt.Rounded(rect, 6))
            using (var br = new SolidBrush(p.PanelInset))
                g.FillPath(br, path);
            using (var path = GdiExt.Rounded(rect, 6))
            using (var pen = new Pen(p.Border, 1))
                g.DrawPath(pen, path);
        }

        private sealed class SideButton : Control
        {
            private readonly bool _isDark;
            private bool _active;
            private bool _hover;

            public bool Active { get => _active; set { _active = value; Invalidate(); } }

            public SideButton(bool isDark)
            {
                _isDark = isDark;
                SetStyle(ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.ResizeRedraw
                       | ControlStyles.UserPaint
                       | ControlStyles.SupportsTransparentBackColor, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var p = Theme.P;
                var inner = new Rectangle(2, 3, Width - 4, Height - 6);

                if (_active || _hover)
                {
                    using (var path = GdiExt.Rounded(inner, 4))
                    using (var br = new SolidBrush(_active ? p.BtnBgActive : Color.FromArgb(60, p.BtnBgHover)))
                        g.FillPath(br, path);
                }

                var fg = _active ? p.Text : p.TextDim;
                int cx = Width / 2, cy = Height / 2;
                using (var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (_isDark)
                    {
                        // crescent moon
                        var path = new GraphicsPath();
                        path.AddEllipse(cx - 6, cy - 6, 12, 12);
                        using (var clip = new GraphicsPath())
                        {
                            clip.AddEllipse(cx - 3, cy - 8, 12, 12);
                            using (var brc = new SolidBrush(fg))
                                g.FillPath(brc, path);
                            var prev = g.Clip;
                            var state = g.Save();
                            using (var region = new Region(clip))
                            {
                                g.SetClip(region, CombineMode.Replace);
                                using (var brBg = new SolidBrush(_active ? p.BtnBgActive : (_hover ? Color.FromArgb(60, p.BtnBgHover) : p.PanelInset)))
                                    g.FillPath(brBg, path);
                            }
                            g.Restore(state);
                        }
                    }
                    else
                    {
                        // sun: center circle + rays
                        g.DrawEllipse(pen, cx - 3, cy - 3, 6, 6);
                        g.DrawLine(pen, cx, cy - 6, cx, cy - 4);
                        g.DrawLine(pen, cx, cy + 4, cx, cy + 6);
                        g.DrawLine(pen, cx - 6, cy, cx - 4, cy);
                        g.DrawLine(pen, cx + 4, cy, cx + 6, cy);
                        g.DrawLine(pen, cx - 4, cy - 4, cx - 3, cy - 3);
                        g.DrawLine(pen, cx + 3, cy + 3, cx + 4, cy + 4);
                        g.DrawLine(pen, cx - 4, cy + 4, cx - 3, cy + 3);
                        g.DrawLine(pen, cx + 3, cy - 3, cx + 4, cy - 4);
                    }
                }
            }
        }
    }

    // Animated slewing pill: "Slewing…" with two expanding rings.
    internal sealed class SlewingBadge : Control
    {
        private float _phaseA, _phaseB = 0.5f;
        private static readonly Timer _tick = new Timer { Interval = 40 };
        private static readonly HashSet<SlewingBadge> _active = new HashSet<SlewingBadge>();
        private string _coord = "";

        public string Coord
        {
            get => _coord;
            set { _coord = value ?? ""; Invalidate(); }
        }

        static SlewingBadge()
        {
            _tick.Tick += (s, e) =>
            {
                foreach (var b in _active)
                {
                    b._phaseA += 0.025f; if (b._phaseA > 1) b._phaseA -= 1f;
                    b._phaseB += 0.025f; if (b._phaseB > 1) b._phaseB -= 1f;
                    try { b.Invalidate(); } catch { }
                }
            };
        }

        public SlewingBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            Height = 24;
            Width = 140;
            Theme.Changed += (s, e) => Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                if (_active.Add(this) && _active.Count == 1) _tick.Start();
            }
            else
            {
                if (_active.Remove(this) && _active.Count == 0) _tick.Stop();
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _active.Remove(this);
            if (_active.Count == 0) _tick.Stop();
            base.OnHandleDestroyed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GdiExt.SmoothText(g);
            var p = Theme.P;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = GdiExt.Rounded(rect, Height / 2))
            using (var br = new SolidBrush(Color.FromArgb(36, p.Accent)))
                g.FillPath(br, path);
            using (var path = GdiExt.Rounded(rect, Height / 2))
            using (var pen = new Pen(Color.FromArgb(102, p.Accent), 1))
                g.DrawPath(pen, path);

            int radarCx = 14, radarCy = Height / 2;
            DrawRing(g, p.Accent, radarCx, radarCy, _phaseA);
            DrawRing(g, p.Accent, radarCx, radarCy, _phaseB);

            using (var br = new SolidBrush(p.Text))
            {
                var f1 = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                g.DrawString("Slewing", f1, br, 26, 3);
                if (!string.IsNullOrEmpty(_coord))
                {
                    var f2 = new Font("Consolas", 8f);
                    using (var brc = new SolidBrush(p.TextDim))
                        g.DrawString(_coord, f2, brc, 78, 4);
                    f2.Dispose();
                }
                f1.Dispose();
            }
        }

        private static void DrawRing(Graphics g, Color c, int cx, int cy, float phase)
        {
            float r = 2 + phase * 7f;
            int alpha = (int)(220 * (1f - phase));
            using (var pen = new Pen(Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c), 1.4f))
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }
    }

    // Themed checkbox: custom box + label. Inherits Control (not CheckBox) so native
    // Visual Styles never double-paint text/glyphs on top of our custom render — fixes
    // duplicate-text artifacts seen on toggle labels under Visual Styles.
    internal class ThemedCheckBox : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.Selectable
                   | ControlStyles.StandardClick
                   | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = true;
            Height = 20;
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 8.75f);
            Cursor = Cursors.Hand;
            Theme.Changed += (s, e) => Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Focus();
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !_checked;
            base.OnClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) { Checked = !_checked; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            GdiExt.SmoothText(g);
            var p = Theme.P;

            int bx = 0, by = (Height - 14) / 2;
            var box = new Rectangle(bx, by, 14, 14);
            Color fill = _checked ? p.Accent : p.InputBg;
            Color border = _checked ? p.Accent : (_hover ? p.InputBorderHover : (Enabled ? p.InputBorder : p.Border));
            using (var path = GdiExt.Rounded(box, 3))
            using (var br = new SolidBrush(fill))
                g.FillPath(br, path);
            using (var path = GdiExt.Rounded(box, 3))
            using (var pen = new Pen(border, 1.2f))
                g.DrawPath(pen, path);
            if (_checked)
            {
                using (var pen = new Pen(p.AccentInk, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(bx + 3, by + 7),
                        new PointF(bx + 6, by + 10),
                        new PointF(bx + 11, by + 4),
                    });
                }
            }

            var textRect = new Rectangle(bx + 14 + 6, 0, Width - (bx + 14 + 6), Height);
            TextRenderer.DrawText(g, Text, Font, textRect,
                Enabled ? p.Text : p.TextFaint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            if (Focused && Enabled)
            {
                var fr = new Rectangle(bx - 1, by - 1, 16, 16);
                using (var pen = new Pen(Color.FromArgb(90, p.Accent), 1) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, fr);
            }
        }
    }

    // Dark-themed TextBox host. The underlying TextBox is Borderless + set colors.
    // We host it in a Panel so we can draw a rounded themed border around it.
    internal class DarkTextBox : Panel
    {
        public TextBox Inner { get; }
        public override string Text { get => Inner.Text; set => Inner.Text = value; }

        public DarkTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint, true);
            Height = 28;
            Inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9f),
                Left = 8, Top = 0,
            };
            Inner.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
            Controls.Add(Inner);
            Theme.Changed += (s, e) => { ApplyTheme(); Invalidate(); };
            ApplyTheme();
        }

        public event EventHandler InnerTextChanged
        {
            add => Inner.TextChanged += value;
            remove => Inner.TextChanged -= value;
        }

        private void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.InputBg;
            Inner.BackColor = p.InputBg;
            Inner.ForeColor = p.Text;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Inner.Width = Math.Max(10, Width - 16);
            Inner.Top = (Height - Inner.Height) / 2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = Theme.P;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = GdiExt.Rounded(rect, 4))
            using (var br = new SolidBrush(p.InputBg))
                g.FillPath(br, path);
            using (var path = GdiExt.Rounded(rect, 4))
            using (var pen = new Pen(Inner.Focused ? p.Accent : p.InputBorder, 1))
                g.DrawPath(pen, path);
        }
    }

    // Console RichTextBox with dark-friendly palette. Used in place of the raw RichTextBox.
    internal sealed class ConsoleLogBox : RichTextBox
    {
        public ConsoleLogBox()
        {
            BorderStyle = BorderStyle.None;
            ReadOnly = true;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            WordWrap = false;
            DetectUrls = false;
            Font = new Font("Consolas", 9f);
            Theme.Changed += (s, e) => ApplyTheme();
            ApplyTheme();
            DarkScroll.Apply(this);
        }

        private void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.ConsoleBg;
            ForeColor = p.ColResp;
        }
    }
}
