using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ASCOM.OnStepX.Astronomy;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Ui.Theming;

namespace ASCOM.OnStepX.Ui
{
    internal sealed class SlewTargetForm : Form
    {
        private readonly MountSession _mount;
        private ComboBox _catalogCombo;
        private TextBox _filterBox;
        private ListView _list;
        private Label _selectedLabel, _coordsLabel;
        private FlatButton _slewBtn, _refreshBtn, _closeBtn;
        private IReadOnlyList<CelestialTarget> _currentCatalog;
        private string _listStatus = "";
        private Panel _topPanel, _bottomPanel;

        public SlewTargetForm(MountSession mount)
        {
            _mount = mount;
            Text = "Slew to Target";
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.P.Bg;
            ForeColor = Theme.P.Text;
            Font = new Font("Segoe UI", 8.75f);
            try { Icon = AppIcons.App; } catch { }
            Theme.Changed += (s, e) => ApplyTheme();
            BuildUi();
            ApplyTheme();
            _catalogCombo.SelectedIndex = 0;
            RefreshList();
        }

        private void BuildUi()
        {
            var p = Theme.P;
            _topPanel = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(8), BackColor = p.Panel2 };
            var top = _topPanel;
            top.Controls.Add(new Label { Text = "Catalog:", Left = 10, Top = 14, Width = 60, BackColor = Color.Transparent });
            _catalogCombo = new ComboBox { Left = 70, Top = 10, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = p.InputBg, ForeColor = p.Text };
            _catalogCombo.Items.AddRange(new object[] { "Planets", "Messier", "NGC", "IC", "SH2", "LDN" });
            _catalogCombo.SelectedIndexChanged += (s, e) => RefreshList();
            top.Controls.Add(_catalogCombo);

            top.Controls.Add(new Label { Text = "Filter:", Left = 230, Top = 14, Width = 50, BackColor = Color.Transparent });
            _filterBox = new TextBox { Left = 280, Top = 10, Width = 200, BorderStyle = BorderStyle.FixedSingle,
                BackColor = p.InputBg, ForeColor = p.Text };
            _filterBox.TextChanged += (s, e) => ApplyFilter();
            top.Controls.Add(_filterBox);

            _refreshBtn = new FlatButton { Text = "Refresh", Left = 490, Top = 8, Width = 80 };
            _refreshBtn.Click += (s, e) => RefreshList();
            top.Controls.Add(_refreshBtn);

            _list = new ListView {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                GridLines = false, OwnerDraw = true, BorderStyle = BorderStyle.FixedSingle,
                BackColor = p.Panel, ForeColor = p.Text,
            };
            _list.Columns.Add("ID", 70);
            _list.Columns.Add("Name", 240);
            _list.Columns.Add("Type", 130);
            _list.Columns.Add("RA (hh:mm)", 100);
            _list.Columns.Add("Dec (\u00B1dd:mm)", 100);
            _list.SelectedIndexChanged += (s, e) => UpdateSelection();
            _list.DrawColumnHeader += (s, e) =>
            {
                var pal = Theme.P;
                using (var br = new SolidBrush(pal.Panel2)) e.Graphics.FillRectangle(br, e.Bounds);
                using (var pen = new Pen(pal.Border, 1)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                    pal.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            _list.DrawItem += (s, e) => e.DrawDefault = false;
            _list.DrawSubItem += (s, e) =>
            {
                var pal = Theme.P;
                bool sel = e.Item.Selected;
                // Blend AccentSoft (12% accent) over Panel for selection highlight.
                // Compute as solid color so GDI FillRectangle is unambiguous.
                Color bg = sel
                    ? Color.FromArgb(
                        (int)Math.Round(pal.Panel.R + (pal.Accent.R - pal.Panel.R) * (31 / 255.0)),
                        (int)Math.Round(pal.Panel.G + (pal.Accent.G - pal.Panel.G) * (31 / 255.0)),
                        (int)Math.Round(pal.Panel.B + (pal.Accent.B - pal.Panel.B) * (31 / 255.0)))
                    : pal.Panel;
                using (var br = new SolidBrush(bg))
                    e.Graphics.FillRectangle(br, e.Bounds);
                if (sel && e.ColumnIndex == 0)
                    using (var br = new SolidBrush(pal.Accent))
                        e.Graphics.FillRectangle(br, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _list.Font,
                    new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                    pal.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };

            _bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(8), BackColor = p.Panel2 };
            var bottom = _bottomPanel;
            _selectedLabel = new Label { Left = 10, Top = 12, Width = 640, Height = 20, Text = "No target selected",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), BackColor = Color.Transparent };
            _coordsLabel = new Label { Left = 10, Top = 38, Width = 640, Height = 20, Text = "",
                BackColor = Color.Transparent, ForeColor = p.TextDim };
            _slewBtn = new FlatButton { Text = "Slew to Target", Left = 10, Top = 72, Width = 160, Height = 32, Enabled = false, Kind = FlatButton.Variant.Primary };
            _slewBtn.Click += (s, e) => DoSlew();
            _closeBtn = new FlatButton { Text = "Close", Left = 180, Top = 72, Width = 90, Height = 32 };
            _closeBtn.Click += (s, e) => Close();
            bottom.Controls.Add(_selectedLabel);
            bottom.Controls.Add(_coordsLabel);
            bottom.Controls.Add(_slewBtn);
            bottom.Controls.Add(_closeBtn);

            Theming.DarkScroll.Apply(_list);

            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(top);
        }

        private void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            ForeColor = p.Text;
            if (_topPanel != null) _topPanel.BackColor = p.Panel2;
            if (_bottomPanel != null) _bottomPanel.BackColor = p.Panel2;
            if (_list != null) { _list.BackColor = p.Panel; _list.ForeColor = p.Text; _list.Invalidate(); }
            if (_catalogCombo != null) { _catalogCombo.BackColor = p.InputBg; _catalogCombo.ForeColor = p.Text; }
            if (_filterBox != null) { _filterBox.BackColor = p.InputBg; _filterBox.ForeColor = p.Text; }
            if (_coordsLabel != null) _coordsLabel.ForeColor = p.TextDim;
            if (_selectedLabel != null) _selectedLabel.ForeColor = p.Text;
        }

        private void RefreshList()
        {
            string sel = (string)_catalogCombo.SelectedItem;
            UseWaitCursor = true;
            try
            {
                switch (sel)
                {
                    case "Planets": _currentCatalog = PlanetCatalog.All; break;
                    case "Messier": _currentCatalog = MessierCatalog.All; break;
                    case "NGC":     _currentCatalog = DeepSkyCatalog.Load("ngc.txt"); break;
                    case "IC":      _currentCatalog = DeepSkyCatalog.Load("ic.txt"); break;
                    case "SH2":     _currentCatalog = DeepSkyCatalog.Load("sh2.txt"); break;
                    case "LDN":     _currentCatalog = DeepSkyCatalog.Load("ldn.txt"); break;
                    default:        _currentCatalog = new CelestialTarget[0]; break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Catalog load failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _currentCatalog = new CelestialTarget[0];
            }
            finally { UseWaitCursor = false; }
            ApplyFilter();
        }

        private const int MaxDisplayRows = 3000;

        private void ApplyFilter()
        {
            string f = _filterBox.Text?.Trim() ?? "";
            _list.BeginUpdate();
            int shown = 0, matched = 0;
            try
            {
                _list.Items.Clear();
                var utc = DateTime.UtcNow;
                var buf = new List<ListViewItem>(Math.Min(MaxDisplayRows, _currentCatalog.Count));
                foreach (var t in _currentCatalog)
                {
                    if (f.Length > 0)
                    {
                        if (t.Id.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                            t.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                            (t.Kind ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                            (t.Constellation ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }
                    matched++;
                    if (shown >= MaxDisplayRows) continue;
                    var (ra, dec) = t.Coords(utc);
                    var li = new ListViewItem(t.Id);
                    li.SubItems.Add(t.Name);
                    li.SubItems.Add(string.IsNullOrEmpty(t.Constellation) ? t.Kind : t.Kind + " (" + t.Constellation + ")");
                    li.SubItems.Add(FormatRaShort(ra));
                    li.SubItems.Add(FormatDecShort(dec));
                    li.Tag = t;
                    buf.Add(li);
                    shown++;
                }
                _list.Items.AddRange(buf.ToArray());
            }
            finally { _list.EndUpdate(); }
            _listStatus = matched > shown
                ? string.Format("Showing {0} of {1} matches — refine filter to narrow.", shown, matched)
                : string.Format("{0} entries", matched);
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (_list.SelectedItems.Count == 0)
            {
                _selectedLabel.Text = string.IsNullOrEmpty(_listStatus) ? "No target selected" : _listStatus;
                _coordsLabel.Text = "";
                _slewBtn.Enabled = false;
                return;
            }
            var t = (CelestialTarget)_list.SelectedItems[0].Tag;
            var (ra, dec) = t.Coords(DateTime.UtcNow);
            _selectedLabel.Text = t.Name + (string.IsNullOrEmpty(t.Constellation) ? "" : " · " + t.Constellation);
            _coordsLabel.Text = string.Format(CultureInfo.InvariantCulture,
                "RA {0}  Dec {1}  ({2})",
                CoordFormat.FormatHoursHighPrec(ra),
                CoordFormat.FormatDegreesHighPrec(dec),
                t.Kind);
            _slewBtn.Enabled = _mount.IsOpen;
        }

        private void DoSlew()
        {
            if (_list.SelectedItems.Count == 0) return;
            if (!_mount.IsOpen) { MessageBox.Show(this, "Mount not connected."); return; }
            var t = (CelestialTarget)_list.SelectedItems[0].Tag;
            var (ra, dec) = t.Coords(DateTime.UtcNow);

            var confirm = MessageBox.Show(this,
                string.Format(CultureInfo.InvariantCulture,
                    "Slew mount to {0}?\r\nRA {1}\r\nDec {2}",
                    t.Name,
                    CoordFormat.FormatHoursHighPrec(ra),
                    CoordFormat.FormatDegreesHighPrec(dec)),
                "Confirm slew", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            try
            {
                if (!_mount.Protocol.SetTargetRA(ra))  throw new Exception("Mount rejected RA");
                if (!_mount.Protocol.SetTargetDec(dec)) throw new Exception("Mount rejected Dec");
                int rc = _mount.Protocol.SlewToTarget();
                if (rc != 0)
                {
                    string msg;
                    switch (rc)
                    {
                        case 1: msg = "Below horizon"; break;
                        case 2: msg = "Above overhead limit"; break;
                        default: msg = "Slew rejected (code " + rc + ")"; break;
                    }
                    MessageBox.Show(this, msg, "Slew error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Slew failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FormatRaShort(double hours)
        {
            hours = ((hours % 24) + 24) % 24;
            int h = (int)hours;
            double rem = (hours - h) * 60.0;
            int m = (int)Math.Round(rem);
            if (m == 60) { m = 0; h = (h + 1) % 24; }
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", h, m);
        }

        private static string FormatDecShort(double deg)
        {
            char sign = deg < 0 ? '-' : '+';
            deg = Math.Abs(deg);
            int d = (int)deg;
            double rem = (deg - d) * 60.0;
            int m = (int)Math.Round(rem);
            if (m == 60) { m = 0; d++; }
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}", sign, d, m);
        }
    }
}
