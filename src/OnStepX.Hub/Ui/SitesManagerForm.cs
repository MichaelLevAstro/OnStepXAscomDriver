using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Ui.Theming;

namespace ASCOM.OnStepX.Ui
{
    // PC-local site list (not OnStep's 3-slot firmware list). Stored as JSON in
    // %APPDATA%\OnStepX\sites.json so it can be exported to another machine.
    // On Apply, writes selected site's lat/lon/elev to the mount via the same
    // LX200 path as the Site group's "Write to mount" button.
    internal sealed class SitesManagerForm : Form
    {
        private readonly MountSession _mount;
        private readonly bool _connected;
        private List<Site> _sites;

        private ListView _list;
        private TextBox _nameBox, _latBox, _lonBox, _eleBox;
        private FlatButton _addBtn, _updateBtn, _removeBtn;
        private FlatButton _importBtn, _exportBtn;
        private FlatButton _loadCurrentBtn;
        private FlatButton _applyBtn, _closeBtn;
        private Label _status;

        public Site AppliedSite { get; private set; }

        public SitesManagerForm(MountSession mount, bool connected)
        {
            _mount = mount;
            _connected = connected;
            _sites = SiteStore.Load();
            Text = "Sites";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(640, 440);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 460);
            BackColor = Theme.P.Bg;
            ForeColor = Theme.P.Text;
            Font = new Font("Segoe UI", 8.75f);
            try { Icon = AppIcons.App; } catch { }
            Theme.Changed += (s, e) => ApplyTheme();
            BuildUi();
            ApplyTheme();
            RefreshList();
        }

        private void BuildUi()
        {
            _list = new ListView {
                Left = 12, Top = 12, Width = 420, Height = 400,
                View = View.Details, FullRowSelect = true, MultiSelect = false,
                GridLines = false, HideSelection = false,
                OwnerDraw = true,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
            };
            _list.Columns.Add("", 22);           // current marker
            _list.Columns.Add("Name", 160);
            _list.Columns.Add("Latitude", 80);
            _list.Columns.Add("Longitude", 90);
            _list.Columns.Add("Elev (m)", 60);
            _list.SelectedIndexChanged += (s, e) => PopulateFieldsFromSelection();
            _list.DrawColumnHeader += DrawColHeader;
            _list.DrawItem         += DrawItem;
            _list.DrawSubItem      += DrawSubItem;
            Theming.DarkScroll.Apply(_list);
            Controls.Add(_list);

            var editAnchor = AnchorStyles.Top | AnchorStyles.Right;
            int x = 444;
            // AutoSize labels so the default 23 px label height doesn't overlap
            // the TextBox below them (what looked like a "background clipping").
            Controls.Add(Lbl("Name:", x, 14, editAnchor));
            _nameBox = NewInput(x, 32, 240, editAnchor);
            Controls.Add(_nameBox);

            Controls.Add(Lbl("Latitude (DMS or decimal):", x, 62, editAnchor));
            _latBox = NewInput(x, 80, 240, editAnchor);
            Controls.Add(_latBox);

            Controls.Add(Lbl("Longitude (west-positive, W is +):", x, 110, editAnchor));
            _lonBox = NewInput(x, 128, 240, editAnchor);
            Controls.Add(_lonBox);

            Controls.Add(Lbl("Elevation (m):", x, 158, editAnchor));
            _eleBox = NewInput(x, 176, 120, editAnchor);
            Controls.Add(_eleBox);

            _loadCurrentBtn = new FlatButton { Text = "Load from current", Left = x, Top = 208, Width = 240, Anchor = editAnchor };
            _loadCurrentBtn.Click += (s, e) => OnLoadFromCurrent();
            Controls.Add(_loadCurrentBtn);

            _addBtn    = new FlatButton { Text = "Add",    Left = x,       Top = 244, Width = 76, Anchor = editAnchor };
            _updateBtn = new FlatButton { Text = "Update", Left = x + 82,  Top = 244, Width = 76, Anchor = editAnchor };
            _removeBtn = new FlatButton { Text = "Remove", Left = x + 164, Top = 244, Width = 76, Anchor = editAnchor, Kind = FlatButton.Variant.Danger };
            _addBtn.Click    += (s, e) => OnAdd();
            _updateBtn.Click += (s, e) => OnUpdate();
            _removeBtn.Click += (s, e) => OnRemove();
            Controls.Add(_addBtn);
            Controls.Add(_updateBtn);
            Controls.Add(_removeBtn);

            _importBtn = new FlatButton { Text = "Import...", Left = x,       Top = 280, Width = 118, Anchor = editAnchor };
            _exportBtn = new FlatButton { Text = "Export...", Left = x + 122, Top = 280, Width = 118, Anchor = editAnchor };
            _importBtn.Click += (s, e) => OnImport();
            _exportBtn.Click += (s, e) => OnExport();
            Controls.Add(_importBtn);
            Controls.Add(_exportBtn);

            var bottomAnchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _status = new Label { Left = 12, Top = 424, Width = 420, Height = 20, ForeColor = Theme.P.TextFaint,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            _applyBtn = new FlatButton { Text = "Apply to mount", Left = 520, Top = 420, Width = 130, Anchor = bottomAnchor, Kind = FlatButton.Variant.Primary };
            _closeBtn = new FlatButton { Text = "Close", Left = 656, Top = 420, Width = 52, Anchor = bottomAnchor, DialogResult = DialogResult.Cancel };
            _applyBtn.Click += (s, e) => OnApply();
            Controls.Add(_status);
            Controls.Add(_applyBtn);
            Controls.Add(_closeBtn);
            CancelButton = _closeBtn;

            if (!_connected)
            {
                _applyBtn.Enabled = false;
                _applyBtn.Text = "Apply (not connected)";
            }
        }

        private static Label Lbl(string text, int x, int y, AnchorStyles anchor)
        {
            return new Label { Text = text, Left = x, Top = y, AutoSize = true, Anchor = anchor, BackColor = Color.Transparent };
        }

        private static TextBox NewInput(int x, int y, int w, AnchorStyles anchor)
        {
            return new TextBox
            {
                Left = x, Top = y, Width = w, Anchor = anchor,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.P.InputBg,
                ForeColor = Theme.P.Text,
            };
        }

        private void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            ForeColor = p.Text;
            foreach (Control c in Controls)
            {
                switch (c)
                {
                    case FlatButton _: break;
                    case ListView lv: lv.BackColor = p.Panel; lv.ForeColor = p.Text; break;
                    case TextBox tb: tb.BackColor = p.InputBg; tb.ForeColor = p.Text; break;
                    case Label lb:
                        lb.BackColor = Color.Transparent;
                        if (lb == _status) lb.ForeColor = p.TextFaint;
                        else lb.ForeColor = p.Text;
                        break;
                }
            }
        }

        // ---------- list owner-draw ----------
        private void DrawColHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            var p = Theme.P;
            using (var br = new SolidBrush(p.Panel2)) e.Graphics.FillRectangle(br, e.Bounds);
            using (var pen = new Pen(p.Border, 1)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", new Font("Segoe UI", 8.25f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                p.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            var p = Theme.P;
            var site = e.Item.Tag as Site;
            bool isCurrent = site != null && IsCurrentSite(site);
            bool sel = e.Item.Selected;

            // Row background
            Color bg = p.Panel;
            if (sel) bg = p.AccentSoft;
            else if (isCurrent) bg = Color.FromArgb(22, p.Ok);
            using (var br = new SolidBrush(bg)) e.Graphics.FillRectangle(br, e.Bounds);

            // Accent left-bar on selected row
            if (sel)
            {
                using (var br = new SolidBrush(p.Accent))
                    e.Graphics.FillRectangle(br, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            }
            // Current-site marker: small filled dot in first column
            if (isCurrent)
            {
                int cx = e.Bounds.X + 12, cy = e.Bounds.Y + e.Bounds.Height / 2;
                var prev = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(p.Ok))
                    e.Graphics.FillEllipse(br, cx - 4, cy - 4, 8, 8);
                e.Graphics.SmoothingMode = prev;
            }
            // Subitems drawn by DrawSubItem.
            e.DrawDefault = false;
        }

        private void DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.ColumnIndex == 0) return; // column 0 is the marker dot, drawn in DrawItem
            var p = Theme.P;
            var site = e.Item.Tag as Site;
            bool isCurrent = site != null && IsCurrentSite(site);
            Color fg = e.Item.Selected ? p.Text : (isCurrent ? p.Ok : p.Text);
            var font = isCurrent ? new Font("Segoe UI", 8.75f, FontStyle.Bold) : Font;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (isCurrent) font.Dispose();
        }

        // Current-site heuristic: PC-stored hub site within 1 arcmin lat/lon and 10 m elevation.
        private static bool IsCurrentSite(Site s)
        {
            const double ll = 1.0 / 60.0;
            const double em = 10.0;
            return Math.Abs(s.Latitude - DriverSettings.SiteLatitude) < ll
                && Math.Abs(s.Longitude - DriverSettings.SiteLongitude) < ll
                && Math.Abs(s.Elevation - DriverSettings.SiteElevation) < em;
        }

        private void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var s in _sites.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var lvi = new ListViewItem(""); // column 0 (marker) drawn in DrawItem
                lvi.SubItems.Add(s.Name ?? "");
                lvi.SubItems.Add(CoordFormat.FormatLatitudeDms(s.Latitude));
                lvi.SubItems.Add(CoordFormat.FormatLongitudeDms(s.Longitude));
                lvi.SubItems.Add(s.Elevation.ToString("F1", CultureInfo.InvariantCulture));
                lvi.Tag = s;
                _list.Items.Add(lvi);
            }
            _list.EndUpdate();
        }

        private void PopulateFieldsFromSelection()
        {
            if (_list.SelectedItems.Count == 0) return;
            var s = (Site)_list.SelectedItems[0].Tag;
            _nameBox.Text = s.Name ?? "";
            _latBox.Text  = CoordFormat.FormatLatitudeDms(s.Latitude);
            _lonBox.Text  = CoordFormat.FormatLongitudeDms(s.Longitude);
            _eleBox.Text  = s.Elevation.ToString("F1", CultureInfo.InvariantCulture);
        }

        private bool ParseFields(out Site site, out string error)
        {
            site = null;
            error = null;
            var name = (_nameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) { error = "Name is required."; return false; }
            if (!CoordFormat.TryParseDegrees(_latBox.Text, out var lat)) { error = "Invalid latitude."; return false; }
            if (!CoordFormat.TryParseDegrees(_lonBox.Text, out var lon)) { error = "Invalid longitude."; return false; }
            if (!double.TryParse(_eleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ele))
            { error = "Invalid elevation (metres)."; return false; }
            site = new Site { Name = name, Latitude = lat, Longitude = lon, Elevation = ele };
            return true;
        }

        // Populate fields from the hub's current site (DriverSettings mirror is
        // the one reconciled with the mount on connect / after Apply). Leaves
        // Name blank so the user can type the new entry's label.
        private void OnLoadFromCurrent()
        {
            _latBox.Text = CoordFormat.FormatLatitudeDms(DriverSettings.SiteLatitude);
            _lonBox.Text = CoordFormat.FormatLongitudeDms(DriverSettings.SiteLongitude);
            _eleBox.Text = DriverSettings.SiteElevation.ToString("F1", CultureInfo.InvariantCulture);
            _status.Text = "Loaded current site into fields — enter a name and Add.";
        }

        private void OnAdd()
        {
            if (!ParseFields(out var site, out var err)) { _status.Text = err; return; }
            if (_sites.Any(s => string.Equals(s.Name, site.Name, StringComparison.OrdinalIgnoreCase)))
            { _status.Text = "A site named '" + site.Name + "' already exists — use Update."; return; }
            _sites.Add(site);
            SiteStore.Save(_sites);
            RefreshList();
            _status.Text = "Added '" + site.Name + "'.";
        }

        private void OnUpdate()
        {
            if (!ParseFields(out var site, out var err)) { _status.Text = err; return; }
            var existing = _sites.FirstOrDefault(s => string.Equals(s.Name, site.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { _status.Text = "No site named '" + site.Name + "' — use Add."; return; }
            existing.Latitude = site.Latitude;
            existing.Longitude = site.Longitude;
            existing.Elevation = site.Elevation;
            SiteStore.Save(_sites);
            RefreshList();
            _status.Text = "Updated '" + site.Name + "'.";
        }

        private void OnRemove()
        {
            if (_list.SelectedItems.Count == 0) { _status.Text = "Select a site to remove."; return; }
            var s = (Site)_list.SelectedItems[0].Tag;
            if (MessageBox.Show(this, "Remove site '" + s.Name + "'?", "Confirm",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            _sites.RemoveAll(x => string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase));
            SiteStore.Save(_sites);
            RefreshList();
            _status.Text = "Removed '" + s.Name + "'.";
        }

        private void OnImport()
        {
            using (var dlg = new OpenFileDialog {
                Filter = "OnStepX sites (*.json)|*.json|All files (*.*)|*.*",
                Title = "Import sites",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _sites = SiteStore.ImportFrom(dlg.FileName, _sites);
                    SiteStore.Save(_sites);
                    RefreshList();
                    _status.Text = "Imported from " + Path.GetFileName(dlg.FileName) + " (merged by name).";
                }
                catch (Exception ex)
                {
                    _status.Text = "Import failed: " + ex.Message;
                }
            }
        }

        private void OnExport()
        {
            using (var dlg = new SaveFileDialog {
                Filter = "OnStepX sites (*.json)|*.json",
                Title = "Export sites",
                FileName = "sites.json",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    SiteStore.ExportTo(dlg.FileName, _sites);
                    _status.Text = "Exported " + _sites.Count + " site(s) to " + Path.GetFileName(dlg.FileName) + ".";
                }
                catch (Exception ex)
                {
                    _status.Text = "Export failed: " + ex.Message;
                }
            }
        }

        private void OnApply()
        {
            if (_list.SelectedItems.Count == 0) { _status.Text = "Select a site to apply."; return; }
            var s = (Site)_list.SelectedItems[0].Tag;
            var msg = string.Format(CultureInfo.InvariantCulture,
                "Write '{0}' to the mount?\n\nLatitude  {1}\nLongitude {2}\nElevation {3:F1} m",
                s.Name,
                CoordFormat.FormatLatitudeDms(s.Latitude),
                CoordFormat.FormatLongitudeDms(s.Longitude),
                s.Elevation);
            if (MessageBox.Show(this, msg, "Confirm site write",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            try
            {
                bool latOk = _mount.Protocol.SetLatitude(s.Latitude);
                bool lonOk = _mount.Protocol.SetLongitude(s.Longitude);
                bool eleOk = _mount.Protocol.SetElevation(s.Elevation);
                if (!(latOk && lonOk && eleOk))
                {
                    _status.Text = "Mount rejected one or more fields (lat=" + latOk + " lon=" + lonOk + " ele=" + eleOk + ").";
                    return;
                }
                // Mirror into DriverSettings so the hub's reconcile-on-connect
                // logic stays consistent with what was just written.
                DriverSettings.SiteLatitude = s.Latitude;
                DriverSettings.SiteLongitude = s.Longitude;
                DriverSettings.SiteElevation = s.Elevation;
                AppliedSite = s;
                _status.Text = "Applied '" + s.Name + "' to mount.";
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                _status.Text = "Apply failed: " + ex.Message;
            }
        }
    }
}
