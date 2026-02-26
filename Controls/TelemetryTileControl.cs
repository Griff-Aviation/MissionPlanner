using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.MavlinkDashboard;

namespace MissionPlanner.Controls
{
    public class TelemetryTileControl : UserControl
    {
        private readonly Label labelName = new Label();
        private readonly Label labelValue = new Label();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private Color stateBorderColor = SystemColors.ActiveBorder;

        public TelemetryTileControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(170, 90);
            Margin = new Padding(6);
            Padding = new Padding(8);
            BackColor = SystemColors.ControlLightLight;

            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.Dock = DockStyle.Fill;
            layout.BackColor = Color.Transparent;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            labelName.Dock = DockStyle.Fill;
            labelName.TextAlign = ContentAlignment.MiddleLeft;
            labelName.AutoEllipsis = true;

            labelValue.Dock = DockStyle.Fill;
            labelValue.TextAlign = ContentAlignment.MiddleLeft;
            labelValue.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold, GraphicsUnit.Point);

            layout.Controls.Add(labelName, 0, 0);
            layout.Controls.Add(labelValue, 0, 1);
            Controls.Add(layout);
        }

        public void SetFieldValue(FieldValue fieldValue)
        {
            if (fieldValue == null)
            {
                labelName.Text = string.Empty;
                labelValue.Text = "-";
                SetStateVisual(SystemColors.ControlDarkDark, SystemColors.ActiveBorder);
                return;
            }

            labelName.Text = fieldValue.Label ?? string.Empty;
            labelValue.Text = string.IsNullOrEmpty(fieldValue.Units)
                ? (fieldValue.FormattedValue ?? string.Empty)
                : (fieldValue.FormattedValue ?? string.Empty) + " " + fieldValue.Units;

            switch (fieldValue.State)
            {
                case FieldState.Warning:
                    SetStateVisual(Color.FromArgb(70, 70, 20), Color.Goldenrod);
                    break;
                case FieldState.Critical:
                    SetStateVisual(Color.FromArgb(70, 30, 30), Color.IndianRed);
                    break;
                case FieldState.Inactive:
                    SetStateVisual(SystemColors.ControlDarkDark, SystemColors.GrayText);
                    break;
                case FieldState.Selected:
                    SetStateVisual(Color.FromArgb(35, 50, 80), Color.CornflowerBlue);
                    break;
                default:
                    SetStateVisual(SystemColors.ControlDarkDark, SystemColors.ActiveBorder);
                    break;
            }
        }

        public Color StateBorderColor
        {
            get => stateBorderColor;
            set
            {
                stateBorderColor = value;
                Invalidate();
            }
        }

        public void SetStateVisual(Color backgroundColor, Color borderColor)
        {
            BackColor = backgroundColor;
            StateBorderColor = borderColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (var pen = new Pen(stateBorderColor))
            {
                var border = ClientRectangle;
                border.Width -= 1;
                border.Height -= 1;
                e.Graphics.DrawRectangle(pen, border);
            }
        }
    }
}
