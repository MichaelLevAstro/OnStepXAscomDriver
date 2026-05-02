using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ASCOM.OnStepX.Driver
{
    // Post-split setup dialog: driver owns no config, so there is nothing to
    // configure here. All transport settings (serial port, TCP host, baud,
    // auto-detect) live in OnStepX.Hub. This dialog just points the user at
    // the hub and offers a one-click launch.
    internal sealed class SetupDialogForm : Form
    {
        public SetupDialogForm()
        {
            Text = "OnStepX ASCOM Driver — Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 180);

            var lbl = new Label
            {
                Left = 16, Top = 16, Width = 428, Height = 100,
                Text =
                    "OnStepX driver v0.4.0 is an in-process shim. All transport settings " +
                    "(serial port, TCP host, baud, auto-detect) are now configured in the " +
                    "OnStepX.Hub application.\r\n\r\n" +
                    "Open OnStepX.Hub, connect to your mount, then click Connect in your " +
                    "ASCOM client."
            };

            var openHub = new Button { Text = "Open OnStepX.Hub", Left = 16, Top = 130, Width = 160 };
            openHub.Click += (s, e) =>
            {
                // Prefer the new WPF hub; fall back to the legacy WinForms one
                // if the WPF exe isn't on PATH (mixed-version install).
                string lastErr = null;
                foreach (var exe in new[] { "OnStepX.Hub.Wpf.exe", "OnStepX.Hub.exe" })
                {
                    try { Process.Start(exe); return; }
                    catch (Exception ex) { lastErr = ex.Message; }
                }
                MessageBox.Show(this,
                    "Could not launch OnStepX hub: " + lastErr + "\r\n\r\n" +
                    "Launch OnStepX.Hub.Wpf (or OnStepX.Hub) manually from the Start menu.",
                    "OnStepX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            var ok = new Button { Text = "Close", Left = 360, Top = 130, Width = 84, DialogResult = DialogResult.OK };
            AcceptButton = ok; CancelButton = ok;

            Controls.AddRange(new Control[] { lbl, openHub, ok });
        }
    }
}
