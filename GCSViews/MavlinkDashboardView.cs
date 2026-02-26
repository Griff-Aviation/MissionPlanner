using MissionPlanner.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class MavlinkDashboardView : MyUserControl
    {
        public event EventHandler PopOutRequested;
        public event EventHandler PopInRequested;
        private int uiTickCount;
        private bool isPoppedOut;

        public MavlinkDashboardView()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetPoppedOutState(false);
            InitializeDefaultTiles();
        }

        private void buttonPopOut_Click(object sender, EventArgs e)
        {
            if (isPoppedOut)
            {
                PopInRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                PopOutRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void uiTickTimer_Tick(object sender, EventArgs e)
        {
            uiTickCount++;
            labelUiTick.Text = "UI tick: " + uiTickCount;
        }

        public void SetPoppedOutState(bool poppedOut)
        {
            isPoppedOut = poppedOut;
            buttonPopOut.Text = poppedOut ? "Pop In" : "Pop Out";
        }

        private void InitializeDefaultTiles()
        {
            var modeTile = new TelemetryTileControl
            {
                Name = "modeTile",
                TileLabel = "Mode",
                TileValue = "-"
            };

            modeTile.SetStateVisual(SystemColors.ControlDarkDark, SystemColors.ActiveBorder);
            flowLayoutPanelTiles.Controls.Add(modeTile);

            var armTile = new TelemetryTileControl
            {
                Name = "armTile",
                TileLabel = "Arm/Disarm",
                TileValue = "Armed"
            };

            armTile.SetStateVisual(SystemColors.ControlDarkDark, SystemColors.ActiveBorder);
            flowLayoutPanelTiles.Controls.Add(armTile);

        }
    }
}
