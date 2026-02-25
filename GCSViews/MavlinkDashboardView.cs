using MissionPlanner.Controls;
using System;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class MavlinkDashboardView : MyUserControl
    {
        public event EventHandler PopOutRequested;

        public MavlinkDashboardView()
        {
            InitializeComponent();
        }

        private void buttonPopOut_Click(object sender, EventArgs e)
        {
            PopOutRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
