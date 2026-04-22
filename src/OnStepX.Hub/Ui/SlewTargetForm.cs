using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ASCOM.OnStepX.Astronomy;
using ASCOM.OnStepX.Hardware;

namespace ASCOM.OnStepX.Ui
{
    internal sealed class SlewTargetForm : Form
    {
        private readonly MountSession _mount;
        private ComboBox _catalogCombo;
        private TextBox _filterBox;
        private ListView _list;
        private Label _selectedLabel, _coordsLabel;
        private Button _slewBtn, _refreshBtn, _closeBtn;
        private IReadOnlyList<CelestialTarget> _currentCatalog;
        private string _listStatus = "";

        public SlewTargetForm(MountSession mount)
        {
            _mount = mount;
            Text = "Slew to Target";
            MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterParent;
            BuildUi();
            _catalogCombo.SelectedIndex = 0;
            RefreshList();
        }

        private void BuildUi()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(8) };
            top.Controls.Add(new Label { Text = "Catalog:", Left = 10, Top = 10, Width = 60 });
            _catalogCombo = new ComboBox { Left = 70, Top = 6, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            _catalogCombo.Items.AddRange(new object[] { "Planets", "Messier", "NGC", "IC", "SH2", "LDN" });
            _catalogCombo.SelectedIndexChanged += (s, e) => RefreshList();
            top.Controls.Add(_catalogCombo);

            top.Controls.Add(new Label { Text = "Filter:", Left = 230, Top = 10, Width = 50 });
            _filterBox = new TextBox { Left = 280, Top = 6, Width = 200 };
            _filterBox.TextChanged += (s, e) => ApplyFilter();
            top.Controls.Add(_filterBox);

            _refreshBtn = new Button { Text = "Refresh", Left = 490, Top = 4, Width = 80 };
            _refreshBtn.Click += (s, e) => RefreshList();
            top.Controls.Add(_refreshBtn);

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, GridLines = true };
            _list.Columns.Add("ID", 60);
            _list.Columns.Add("Name", 220);
            _list.Columns.Add("Type", 110);
            _list.Columns.Add("RA (hh:mm)", 90);
            _list.Columns.Add("Dec (±dd:mm)", 90);
            _list.SelectedIndexChanged += (s, e) => UpdateSelection();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(8) };
            _selectedLabel = new Label { Left = 10, Top = 8, Width = 580, Height = 20, Text = "No target selected", Font = new Font(Font, FontStyle.Bold) };
            _coordsLabel = new Label { Left = 10, Top = 34, Width = 580, Height = 20, Text = "" };
            _slewBtn = new Button { Text = "Slew to Target", Left = 10, Top = 68, Width = 150, Height = 32, Enabled = false };
            _slewBtn.Click += (s, e) => DoSlew();
            _closeBtn = new Button { Text = "Close", Left = 170, Top = 68, Width = 90, Height = 32 };
            _closeBtn.Click += (s, e) => Close();
            bottom.Controls.Add(_selectedLabel);
            bottom.Controls.Add(_coordsLabel);
            bottom.Controls.Add(_slewBtn);
            bottom.Controls.Add(_closeBtn);

            Controls.Add(_list);
            Controls.Add(bottom);
            Controls.Add(top);
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
