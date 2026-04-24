using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ASCOM.OnStepX.Ui.Theming;

namespace ASCOM.OnStepX.Ui
{
    internal sealed class SlewPadControl : UserControl
    {
        public event Action<string> DirectionPressed;   // "N","NE","E","SE","S","SW","W","NW"
        public event Action<string> DirectionReleased;
        public event Action Stop;

        private readonly TableLayoutPanel _grid;

        public SlewPadControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                BackColor = Color.Transparent,
            };
            for (int i = 0; i < 3; i++)
            {
                _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            }
            Controls.Add(_grid);

            AddBtn(0, 0, "NW", "\u2196");
            AddBtn(1, 0, "N",  "\u2191");
            AddBtn(2, 0, "NE", "\u2197");
            AddBtn(0, 1, "W",  "\u2190");
            AddStopBtn(1, 1);
            AddBtn(2, 1, "E",  "\u2192");
            AddBtn(0, 2, "SW", "\u2199");
            AddBtn(1, 2, "S",  "\u2193");
            AddBtn(2, 2, "SE", "\u2198");

            Theme.Changed += (s, e) => Invalidate(true);
        }

        private void AddBtn(int col, int row, string dir, string arrow)
        {
            var b = new SlewButton(dir, arrow, isStop: false);
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(3);
            b.Pressed += () => DirectionPressed?.Invoke(dir);
            b.Released += () => DirectionReleased?.Invoke(dir);
            _grid.Controls.Add(b, col, row);
        }

        private void AddStopBtn(int col, int row)
        {
            var b = new SlewButton("STOP", "", isStop: true);
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(3);
            b.Clicked += () => Stop?.Invoke();
            _grid.Controls.Add(b, col, row);
        }

        internal sealed class SlewButton : Control
        {
            public event Action Pressed;
            public event Action Released;
            public event Action Clicked;

            private readonly string _label;
            private readonly string _arrow;
            private readonly bool _stop;
            private bool _hover, _down;

            public SlewButton(string label, string arrow, bool isStop)
            {
                _label = label;
                _arrow = arrow;
                _stop = isStop;
                SetStyle(ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.ResizeRedraw
                       | ControlStyles.UserPaint
                       | ControlStyles.SupportsTransparentBackColor, true);
                Cursor = Cursors.Hand;
                BackColor = Color.Transparent;
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_down && !_stop) { _down = false; Released?.Invoke(); }
                _hover = false;
                Invalidate();
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                _down = true; Invalidate();
                if (!_stop) Pressed?.Invoke();
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                bool was = _down;
                _down = false; Invalidate();
                if (_stop) { if (was) Clicked?.Invoke(); }
                else { Released?.Invoke(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                GdiExt.SmoothText(g);
                var p = Theme.P;

                var rect = new Rectangle(1, 1, Width - 2, Height - 2);
                Color bg, border, fg;
                if (_stop)
                {
                    bg = _down ? ControlPaint.Dark(p.Accent, 0.08f) : _hover ? ControlPaint.Light(p.Accent, 0.08f) : p.Accent;
                    border = p.Accent;
                    fg = p.AccentInk;
                }
                else
                {
                    if (_down)
                    {
                        bg = Color.FromArgb(46, p.Accent);
                        border = p.Accent;
                    }
                    else if (_hover)
                    {
                        bg = p.BtnBgHover;
                        border = p.InputBorderHover;
                    }
                    else
                    {
                        bg = p.BtnBg;
                        border = p.InputBorder;
                    }
                    fg = Enabled ? p.Text : p.TextFaint;
                }

                if (!Enabled)
                {
                    bg = Color.FromArgb(115, bg);
                    fg = p.TextFaint;
                }

                using (var path = GdiExt.Rounded(rect, 6))
                using (var br = new SolidBrush(bg))
                    g.FillPath(br, path);
                using (var path = GdiExt.Rounded(rect, 6))
                using (var pen = new Pen(border, 1))
                    g.DrawPath(pen, path);

                if (!string.IsNullOrEmpty(_arrow))
                {
                    using (var f = new Font("Consolas", 8.5f))
                    using (var br = new SolidBrush(_stop ? p.AccentInk : p.TextFaint))
                        g.DrawString(_arrow, f, br, 6, 4);
                }

                float emphasis = _stop ? 15f : 13f;
                using (var big = new Font("Segoe UI", emphasis, FontStyle.Bold))
                using (var br = new SolidBrush(fg))
                {
                    var sz = g.MeasureString(_label, big);
                    g.DrawString(_label, big, br,
                        (Width - sz.Width) / 2f,
                        (Height - sz.Height) / 2f);
                }
            }
        }
    }
}
