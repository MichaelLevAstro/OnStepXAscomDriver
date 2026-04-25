using System;
using System.Drawing;
using System.Windows.Forms;
using ASCOM.OnStepX.Ui.Theming;

namespace ASCOM.OnStepX.Ui
{
    // MessageBox replacement whose body is a selectable, read-only TextBox so users can
    // highlight and copy raw error text (e.g. mount replies) instead of retyping it.
    internal static class CopyableMessage
    {
        public static void Show(IWin32Window owner, string title, string body)
        {
            var p = Theme.P;
            using (var f = new Form())
            {
                f.Text = title ?? "";
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.FormBorderStyle = FormBorderStyle.Sizable;
                f.Width = 540;
                f.Height = 280;
                f.MinimumSize = new Size(360, 180);
                f.BackColor = p.Bg;
                f.ForeColor = p.Text;
                f.Font = new Font("Segoe UI", 8.75f);

                var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = p.Bg };
                var text = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    WordWrap = true,
                    Dock = DockStyle.Fill,
                    Text = body ?? "",
                    BackColor = p.InputBg,
                    ForeColor = p.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 9f),
                };
                textHost.Controls.Add(text);

                var bottom = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 46,
                    Padding = new Padding(10),
                    BackColor = p.Panel2,
                };
                var ok = new FlatButton { Text = "OK", Width = 90, DialogResult = DialogResult.OK, Kind = FlatButton.Variant.Primary };
                var copy = new FlatButton { Text = "Copy", Width = 90 };
                copy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(text.Text ?? ""); } catch { }
                };
                bottom.Controls.Add(ok);
                bottom.Controls.Add(copy);

                f.Controls.Add(textHost);
                f.Controls.Add(bottom);
                f.AcceptButton = ok;
                f.CancelButton = ok;

                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
            }
        }
    }
}
