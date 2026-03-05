namespace MissionPlanner.GCSViews
{
    partial class DashboardView
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
            this.components = new System.ComponentModel.Container();
            this.panelTop = new System.Windows.Forms.Panel();
            this.buttonPopOut = new System.Windows.Forms.Button();
            this.flowLayoutPanelTiles = new System.Windows.Forms.FlowLayoutPanel();
            this.uiTickTimer = new System.Windows.Forms.Timer(this.components);
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
            // flowLayoutPanelTiles
            // 
            this.flowLayoutPanelTiles.AutoScroll = true;
            this.flowLayoutPanelTiles.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flowLayoutPanelTiles.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.flowLayoutPanelTiles.Location = new System.Drawing.Point(0, 35);
            this.flowLayoutPanelTiles.Name = "flowLayoutPanelTiles";
            this.flowLayoutPanelTiles.Padding = new System.Windows.Forms.Padding(6);
            this.flowLayoutPanelTiles.Size = new System.Drawing.Size(150, 115);
            this.flowLayoutPanelTiles.TabIndex = 1;
            this.flowLayoutPanelTiles.WrapContents = true;
            // 
            // uiTickTimer
            // 
            this.uiTickTimer.Enabled = true;
            this.uiTickTimer.Interval = 150;
            this.uiTickTimer.Tick += new System.EventHandler(this.uiTickTimer_Tick);
            // 
            // DashboardView
            // 
            this.Controls.Add(this.flowLayoutPanelTiles);
            this.Controls.Add(this.panelTop);
            this.Name = "DashboardView";
            this.panelTop.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Button buttonPopOut;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelTiles;
        private System.Windows.Forms.Timer uiTickTimer;
    }
}
