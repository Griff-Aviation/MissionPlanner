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
            this.panelTop = new System.Windows.Forms.Panel();
            this.buttonPopOut = new System.Windows.Forms.Button();
            this.labelPlaceholder = new System.Windows.Forms.Label();
            this.panelTop.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.buttonPopOut);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(150, 35);
            this.panelTop.TabIndex = 0;
            // 
            // buttonPopOut
            // 
            this.buttonPopOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonPopOut.Location = new System.Drawing.Point(69, 6);
            this.buttonPopOut.Name = "buttonPopOut";
            this.buttonPopOut.Size = new System.Drawing.Size(75, 23);
            this.buttonPopOut.TabIndex = 0;
            this.buttonPopOut.Text = "Pop Out";
            this.buttonPopOut.UseVisualStyleBackColor = true;
            this.buttonPopOut.Click += new System.EventHandler(this.buttonPopOut_Click);
            // 
            // labelPlaceholder
            // 
            this.labelPlaceholder.Dock = System.Windows.Forms.DockStyle.Fill;
            this.labelPlaceholder.Location = new System.Drawing.Point(0, 35);
            this.labelPlaceholder.Name = "labelPlaceholder";
            this.labelPlaceholder.Size = new System.Drawing.Size(150, 115);
            this.labelPlaceholder.TabIndex = 1;
            this.labelPlaceholder.Text = "MAVLink Dashboard (WIP)";
            this.labelPlaceholder.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // MavlinkDashboardView
            // 
            this.Controls.Add(this.labelPlaceholder);
            this.Controls.Add(this.panelTop);
            this.Name = "MavlinkDashboardView";
            this.panelTop.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Button buttonPopOut;
        private System.Windows.Forms.Label labelPlaceholder;
    }
}
