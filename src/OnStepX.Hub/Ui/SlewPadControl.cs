using System;
using System.Drawing;
using System.Windows.Forms;

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
            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3
            };
            for (int i = 0; i < 3; i++)
            {
                _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            }
            Controls.Add(_grid);

            AddBtn(0, 0, "NW"); AddBtn(1, 0, "N"); AddBtn(2, 0, "NE");
            AddBtn(0, 1, "W");  AddStopBtn(1, 1); AddBtn(2, 1, "E");
            AddBtn(0, 2, "SW"); AddBtn(1, 2, "S"); AddBtn(2, 2, "SE");
        }

        private void AddBtn(int col, int row, string dir)
        {
            var b = new Button { Text = dir, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
            b.MouseDown += (s, e) => DirectionPressed?.Invoke(dir);
            b.MouseUp   += (s, e) => DirectionReleased?.Invoke(dir);
            b.MouseLeave+= (s, e) => DirectionReleased?.Invoke(dir);
            _grid.Controls.Add(b, col, row);
        }

        private void AddStopBtn(int col, int row)
        {
            var b = new Button { Text = "STOP", Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.Firebrick, Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
            b.Click += (s, e) => Stop?.Invoke();
            _grid.Controls.Add(b, col, row);
        }
    }
}
