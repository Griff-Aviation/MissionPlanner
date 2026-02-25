namespace MissionPlanner.GCSViews
{
    partial class MavlinkDashboardView
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.labelPlaceholder = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // labelPlaceholder
            // 
            this.labelPlaceholder.Dock = System.Windows.Forms.DockStyle.Fill;
            this.labelPlaceholder.Location = new System.Drawing.Point(0, 0);
            this.labelPlaceholder.Name = "labelPlaceholder";
            this.labelPlaceholder.Size = new System.Drawing.Size(150, 150);
            this.labelPlaceholder.TabIndex = 0;
            this.labelPlaceholder.Text = "MAVLink Dashboard (WIP)";
            this.labelPlaceholder.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // MavlinkDashboardView
            // 
            this.Controls.Add(this.labelPlaceholder);
            this.Name = "MavlinkDashboardView";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Label labelPlaceholder;
    }
}
