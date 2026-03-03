using System.Drawing;
using System.Windows.Forms;

using MissionPlanner.MavlinkDashboard;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public class TelemetryTileControl : UserControl
    {
        public static Color NormalColor { get; set; } = Color.FromArgb(108, 181, 80);
        public static Color WarningColor { get; set; } = Color.FromArgb(214, 184, 65);
        public static Color CriticalColor { get; set; } = Color.FromArgb(198, 88, 88);
        public static Color InactiveColor { get; set; } = Color.FromArgb(132, 132, 132);
        public static Color SelectedColor { get; set; } = Color.FromArgb(92, 149, 255);

        private readonly Label labelName = new Label();
        private readonly Label labelValue = new Label();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private Color stateBorderColor = SystemColors.ActiveBorder;
        private FieldState telemetryState = FieldState.Normal;
        private bool isSelected;
        private bool isDropTarget;

        public TelemetryTileControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Size = new Size(170, 90);
            Margin = new Padding(6);
            Padding = new Padding(8);
            TabStop = true;
            BackColor = ThemeManager.ControlBGColor;

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

            layout.MouseDown += Tile_MouseDown;
            labelName.MouseDown += Tile_MouseDown;
            labelValue.MouseDown += Tile_MouseDown;

            layout.Controls.Add(labelName, 0, 0);
            layout.Controls.Add(labelValue, 0, 1);
            Controls.Add(layout);
            ApplyStateVisual();
        }

        public void SetFieldValue(FieldValue fieldValue)
        {
            if (fieldValue == null)
            {
                labelName.Text = string.Empty;
                labelValue.Text = "-";
                telemetryState = FieldState.Inactive;
                ApplyStateVisual();
                return;
            }

            labelName.Text = fieldValue.Label ?? string.Empty;
            if (string.IsNullOrEmpty(fieldValue.Units))
            {
                labelName.Text = fieldValue.Label ?? string.Empty;
            }
            else
            {
                labelName.Text = (fieldValue.Label ?? string.Empty) + " (" + fieldValue.Units + ")";
            }

            labelValue.Text = fieldValue.FormattedValue ?? string.Empty;
            telemetryState = fieldValue.State == FieldState.Selected ? FieldState.Normal : fieldValue.State;
            ApplyStateVisual();
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

        public bool IsDropTarget
        {
            get => isDropTarget;
            set
            {
                if (isDropTarget == value)
                {
                    return;
                }

                isDropTarget = value;
                Invalidate();
            }
        }

        protected override void OnEnter(System.EventArgs e)
        {
            base.OnEnter(e);
            isSelected = true;
            ApplyStateVisual();
        }

        protected override void OnLeave(System.EventArgs e)
        {
            base.OnLeave(e);
            isSelected = false;
            ApplyStateVisual();
        }

        private void ApplyStateVisual()
        {
            labelName.ForeColor = ThemeManager.TextColor;
            labelValue.ForeColor = ThemeManager.TextColor;

            var stateForVisual = isSelected ? FieldState.Selected : telemetryState;
            var semanticColor = GetSemanticColor(stateForVisual);
            var backgroundColor = Blend(ThemeManager.ControlBGColor, semanticColor, GetTintWeight(stateForVisual));
            SetStateVisual(backgroundColor, semanticColor);
        }

        private static int GetTintWeight(FieldState state)
        {
            switch (state)
            {
                case FieldState.Warning:
                    return 0;
                case FieldState.Critical:
                    return 30;
                case FieldState.Inactive:
                    return 18;
                case FieldState.Selected:
                    return 28;
                default:
                    return 20;
            }
        }

        private static Color GetSemanticColor(FieldState state)
        {
            switch (state)
            {
                case FieldState.Warning:
                    return WarningColor;
                case FieldState.Critical:
                    return CriticalColor;
                case FieldState.Inactive:
                    return InactiveColor;
                case FieldState.Selected:
                    return SelectedColor;
                default:
                    return NormalColor;
            }
        }

        private static Color Blend(Color baseColor, Color tintColor, int tintPercent)
        {
            if (tintPercent <= 0)
            {
                return baseColor;
            }

            if (tintPercent >= 100)
            {
                return tintColor;
            }

            var inv = 100 - tintPercent;
            return Color.FromArgb(
                (baseColor.R * inv + tintColor.R * tintPercent) / 100,
                (baseColor.G * inv + tintColor.G * tintPercent) / 100,
                (baseColor.B * inv + tintColor.B * tintPercent) / 100);
        }

        private void Tile_MouseDown(object sender, MouseEventArgs e)
        {
            Focus();
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

            if (!isDropTarget)
            {
                return;
            }

            // Drag-hover cue: draw a thick outline only, leaving tile fill/state untouched.
            using (var dropTargetPen = new Pen(Color.Yellow, 3F))
            {
                var highlightBorder = ClientRectangle;
                highlightBorder.Inflate(-2, -2);
                highlightBorder.Width -= 1;
                highlightBorder.Height -= 1;
                e.Graphics.DrawRectangle(dropTargetPen, highlightBorder);
            }
        }
    }
}
