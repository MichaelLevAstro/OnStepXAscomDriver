using System;
using System.Drawing;
using System.Windows.Forms;

namespace ASCOM.OnStepX.Ui
{
    // MessageBox replacement whose body is a selectable, read-only TextBox so users can
    // highlight and copy raw error text (e.g. mount replies) instead of retyping it.
    internal static class CopyableMessage
    {
        public static void Show(IWin32Window owner, string title, string body)
        {
            using (var f = new Form())
            {
                f.Text = title ?? "";
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.FormBorderStyle = FormBorderStyle.Sizable;
                f.Width = 520;
                f.Height = 260;
                f.MinimumSize = new Size(360, 180);

                var text = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    WordWrap = true,
                    Dock = DockStyle.Fill,
                    Text = body ?? "",
                    BackColor = SystemColors.Window
                };

                var bottom = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 40,
                    Padding = new Padding(8)
                };
                var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
                var copy = new Button { Text = "Copy", Width = 90 };
                copy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(text.Text ?? ""); } catch { }
                };
                bottom.Controls.Add(ok);
                bottom.Controls.Add(copy);

                f.Controls.Add(text);
                f.Controls.Add(bottom);
                f.AcceptButton = ok;
                f.CancelButton = ok;

                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
            }
        }
    }
}
