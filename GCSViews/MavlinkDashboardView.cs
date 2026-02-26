using MissionPlanner.Controls;
using MissionPlanner.MavlinkDashboard;
using System;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class MavlinkDashboardView : MyUserControl
    {
        public event EventHandler PopOutRequested;
        public event EventHandler PopInRequested;
        private bool isPoppedOut;
        private readonly IFieldValueSource fieldValueSource = new SyntheticFieldValueSource();
        private readonly FieldKey modeFieldKey = new FieldKey {Message = "SYNTHETIC", Field = "VALUE"};
        private readonly TelemetryTileControl modeTile = new TelemetryTileControl {Name = "modeTile"};

        public MavlinkDashboardView()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetPoppedOutState(false);
            flowLayoutPanelTiles.Controls.Add(modeTile);
            RefreshTiles();
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
            RefreshTiles();
        }

        public void SetPoppedOutState(bool poppedOut)
        {
            isPoppedOut = poppedOut;
            buttonPopOut.Text = poppedOut ? "Pop In" : "Pop Out";
        }

        private void RefreshTiles()
        {
            if (fieldValueSource.TryGetValue(modeFieldKey, out var value))
            {
                modeTile.SetFieldValue(value);
            }
        }
    }
}
