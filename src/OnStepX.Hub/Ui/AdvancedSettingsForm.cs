using System;
using System.Drawing;
using System.Windows.Forms;
using ASCOM.OnStepX.Config;
using ASCOM.OnStepX.Hardware;

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
        private CheckBox _pauseAtHomeBox;
        private Button _applyBtn, _okBtn, _cancelBtn;
        private Label _statusLabel;

        public AdvancedSettingsForm(MountSession mount)
        {
            _mount = mount;
            Text = "Advanced Mount Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 180);
            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            var pierGroup = new GroupBox { Text = "Pier / Meridian Policy", Left = 10, Top = 10, Width = 400, Height = 120 };
            pierGroup.Controls.Add(new Label { Text = "Preferred pier side:", Left = 10, Top = 28, Width = 140 });
            _preferredPierBox = new ComboBox { Left = 160, Top = 24, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _preferredPierBox.Items.AddRange(new object[] {
                "Best (stay on current side)",
                "East",
                "West",
                "Auto",
            });
            pierGroup.Controls.Add(_preferredPierBox);
            pierGroup.Controls.Add(new Label {
                Text = "Recommended: Best. Prevents spurious flips from plate-solve\ncorrections near the meridian.",
                Left = 10, Top = 54, Width = 380, Height = 34, ForeColor = SystemColors.GrayText,
            });

            _pauseAtHomeBox = new CheckBox { Text = "Pause at home on meridian flip", Left = 10, Top = 92, Width = 300 };
            pierGroup.Controls.Add(_pauseAtHomeBox);

            _statusLabel = new Label { Left = 10, Top = 148, Width = 220, Height = 20, ForeColor = SystemColors.GrayText };
            _applyBtn = new Button { Text = "Apply", Left = 220, Top = 144, Width = 60, DialogResult = DialogResult.None };
            _okBtn = new Button { Text = "OK", Left = 286, Top = 144, Width = 60, DialogResult = DialogResult.OK };
            _cancelBtn = new Button { Text = "Cancel", Left = 352, Top = 144, Width = 60, DialogResult = DialogResult.Cancel };
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
