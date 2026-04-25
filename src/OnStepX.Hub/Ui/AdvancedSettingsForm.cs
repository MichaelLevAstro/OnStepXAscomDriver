using System;
using System.Drawing;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;
using ASCOM.OnStepX.Ui.Theming;

namespace ASCOM.OnStepX.Ui
{
    // Advanced pier/flip settings exposed over LX200 (:SX96 preferred pier,
    // :SX98 pause-at-home-on-flip). Button is gated on mount connection in
    // HubForm, so this dialog only opens while connected and always pushes
    // writes to the wire on Apply.
    internal sealed class AdvancedSettingsForm : Form
    {
        private readonly MountSession _mount;
        private ComboBox _preferredPierBox;
        private ThemedCheckBox _pauseAtHomeBox;
        private FlatButton _applyBtn, _okBtn, _cancelBtn;
        private Label _statusLabel;

        public AdvancedSettingsForm(MountSession mount)
        {
            _mount = mount;
            Text = "Advanced Mount Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 220);
            BackColor = Theme.P.Bg;
            ForeColor = Theme.P.Text;
            Font = new Font("Segoe UI", 8.75f);
            try { Icon = AppIcons.App; } catch { }
            Theme.Changed += (s, e) => ApplyTheme();
            BuildUi();
            LoadValues();
            ApplyTheme();
        }

        private void BuildUi()
        {
            var pierGroup = new SectionPanel { Title = "Pier / Meridian Policy", Left = 10, Top = 10, Width = 420, Height = 158 };
            pierGroup.Controls.Add(new Label { Text = "Preferred pier side:", Left = 10, Top = 14, Width = 140, BackColor = Color.Transparent });
            _preferredPierBox = new ComboBox { Left = 160, Top = 10, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.P.InputBg, ForeColor = Theme.P.Text };
            _preferredPierBox.Items.AddRange(new object[] {
                "Best (stay on current side)",
                "East",
                "West",
                "Auto",
            });
            pierGroup.Controls.Add(_preferredPierBox);
            pierGroup.Controls.Add(new Label {
                Text = "Recommended: Best. Prevents spurious flips from plate-solve\ncorrections near the meridian.",
                Left = 10, Top = 42, Width = 400, Height = 34, ForeColor = Theme.P.TextFaint, BackColor = Color.Transparent,
            });

            _pauseAtHomeBox = new ThemedCheckBox { Text = "Pause at home on meridian flip", Left = 10, Top = 86, Width = 320 };
            pierGroup.Controls.Add(_pauseAtHomeBox);

            _statusLabel = new Label { Left = 10, Top = 184, Width = 240, Height = 20, ForeColor = Theme.P.TextFaint, BackColor = Color.Transparent };
            _applyBtn = new FlatButton { Text = "Apply", Left = 236, Top = 180, Width = 60, DialogResult = DialogResult.None };
            _okBtn = new FlatButton { Text = "OK", Left = 302, Top = 180, Width = 60, DialogResult = DialogResult.OK, Kind = FlatButton.Variant.Primary };
            _cancelBtn = new FlatButton { Text = "Cancel", Left = 368, Top = 180, Width = 62, DialogResult = DialogResult.Cancel };
            _applyBtn.Click += (s, e) => Apply();
            _okBtn.Click += (s, e) => { if (Apply()) DialogResult = DialogResult.OK; };

            Controls.Add(pierGroup);
            Controls.Add(_statusLabel);
            Controls.Add(_applyBtn);
            Controls.Add(_okBtn);
            Controls.Add(_cancelBtn);
            AcceptButton = _okBtn;
            CancelButton = _cancelBtn;
        }

        private void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            ForeColor = p.Text;
            if (_preferredPierBox != null) { _preferredPierBox.BackColor = p.InputBg; _preferredPierBox.ForeColor = p.Text; }
            if (_statusLabel != null) _statusLabel.ForeColor = p.TextFaint;
        }

        private void LoadValues()
        {
            // Prefer mount's live :GX96 over last stored value.
            int pierIdx = SettingToIndex(DriverSettings.PreferredPierSide);
            try { pierIdx = (int)_mount.Protocol.GetPreferredPierSide(); }
            catch { /* fall back to stored setting */ }
            _preferredPierBox.SelectedIndex = pierIdx;
            _pauseAtHomeBox.Checked = DriverSettings.PauseAtHomeOnFlip;
        }

        private bool Apply()
        {
            DriverSettings.PreferredPierSide = IndexToSetting(_preferredPierBox.SelectedIndex);
            DriverSettings.PauseAtHomeOnFlip = _pauseAtHomeBox.Checked;

            try
            {
                var pierEnum = (LX200Protocol.PreferredPier)_preferredPierBox.SelectedIndex;
                bool ok1 = _mount.Protocol.SetPreferredPierSide(pierEnum);
                bool ok2 = _mount.Protocol.SetPauseAtHomeOnFlip(_pauseAtHomeBox.Checked);
                _statusLabel.Text = (ok1 && ok2) ? "Applied to mount." : "Some commands rejected.";
                return true;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Mount error: " + ex.Message;
                return false;
            }
        }

        private static int SettingToIndex(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            switch (char.ToUpperInvariant(s[0]))
            {
                case 'E': return 1;
                case 'W': return 2;
                case 'A': return 3;
                default:  return 0;
            }
        }

        private static string IndexToSetting(int i)
        {
            switch (i)
            {
                case 1: return "E";
                case 2: return "W";
                case 3: return "A";
                default: return "B";
            }
        }
    }
}
