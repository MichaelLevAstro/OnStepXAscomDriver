using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

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
        private Button _addBtn, _updateBtn, _removeBtn;
        private Button _importBtn, _exportBtn;
        private Button _applyBtn, _closeBtn;
        private Label _status;

        public Site AppliedSite { get; private set; }

        public SitesManagerForm(MountSession mount, bool connected)
        {
            _mount = mount;
            _connected = connected;
            _sites = SiteStore.Load();
            Text = "Sites";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 440);
            BuildUi();
            RefreshList();
        }

        private void BuildUi()
        {
            _list = new ListView {
                Left = 10, Top = 10, Width = 400, Height = 360,
                View = View.Details, FullRowSelect = true, MultiSelect = false,
                GridLines = true, HideSelection = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
            };
            _list.Columns.Add("Name", 160);
            _list.Columns.Add("Latitude", 80);
            _list.Columns.Add("Longitude", 90);
            _list.Columns.Add("Elev (m)", 60);
            _list.SelectedIndexChanged += (s, e) => PopulateFieldsFromSelection();
            Controls.Add(_list);

            var editAnchor = AnchorStyles.Top | AnchorStyles.Right;
            int x = 420;
            Controls.Add(new Label { Text = "Name:", Left = x, Top = 14, Width = 80, Anchor = editAnchor });
            _nameBox = new TextBox { Left = x, Top = 32, Width = 240, Anchor = editAnchor };
            Controls.Add(_nameBox);

            Controls.Add(new Label { Text = "Latitude (DMS or decimal):", Left = x, Top = 62, Width = 240, Anchor = editAnchor });
            _latBox = new TextBox { Left = x, Top = 80, Width = 240, Anchor = editAnchor };
            Controls.Add(_latBox);

            Controls.Add(new Label { Text = "Longitude (east-positive):", Left = x, Top = 110, Width = 240, Anchor = editAnchor });
            _lonBox = new TextBox { Left = x, Top = 128, Width = 240, Anchor = editAnchor };
            Controls.Add(_lonBox);

            Controls.Add(new Label { Text = "Elevation (m):", Left = x, Top = 158, Width = 240, Anchor = editAnchor });
            _eleBox = new TextBox { Left = x, Top = 176, Width = 120, Anchor = editAnchor };
            Controls.Add(_eleBox);

            _addBtn    = new Button { Text = "Add",    Left = x,       Top = 212, Width = 76, Anchor = editAnchor };
            _updateBtn = new Button { Text = "Update", Left = x + 82,  Top = 212, Width = 76, Anchor = editAnchor };
            _removeBtn = new Button { Text = "Remove", Left = x + 164, Top = 212, Width = 76, Anchor = editAnchor };
            _addBtn.Click    += (s, e) => OnAdd();
            _updateBtn.Click += (s, e) => OnUpdate();
            _removeBtn.Click += (s, e) => OnRemove();
            Controls.Add(_addBtn);
            Controls.Add(_updateBtn);
            Controls.Add(_removeBtn);

            _importBtn = new Button { Text = "Import...", Left = x,      Top = 252, Width = 118, Anchor = editAnchor };
            _exportBtn = new Button { Text = "Export...", Left = x + 122, Top = 252, Width = 118, Anchor = editAnchor };
            _importBtn.Click += (s, e) => OnImport();
            _exportBtn.Click += (s, e) => OnExport();
            Controls.Add(_importBtn);
            Controls.Add(_exportBtn);

            var bottomAnchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _status = new Label { Left = 10, Top = 388, Width = 400, Height = 20, ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            _applyBtn = new Button { Text = "Apply to mount", Left = 480, Top = 384, Width = 120, Anchor = bottomAnchor };
            _closeBtn = new Button { Text = "Close", Left = 606, Top = 384, Width = 60, Anchor = bottomAnchor, DialogResult = DialogResult.Cancel };
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

        private void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var s in _sites.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var lvi = new ListViewItem(s.Name ?? "");
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
