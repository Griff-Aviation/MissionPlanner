using MissionPlanner.Controls;
using MissionPlanner.MavlinkDashboard;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class MavlinkDashboardView : MyUserControl
    {
        public event EventHandler PopOutRequested;
        public event EventHandler PopInRequested;
        private bool isPoppedOut;
        private readonly IFieldValueSource fieldValueSource = new CurrentStateFieldValueSource();
        private readonly List<(FieldKey Key, TelemetryTileControl Tile)> tiles = new List<(FieldKey Key, TelemetryTileControl Tile)>();

        public MavlinkDashboardView()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetPoppedOutState(false);
            InitializeDefaultTiles();
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
            foreach (var tile in tiles)
            {
                if (fieldValueSource.TryGetValue(tile.Key, out var value))
                {
                    tile.Tile.SetFieldValue(value);
                }
            }
        }

        private void InitializeDefaultTiles()
        {
            AddTile("CURRENT_STATE", "MODE");
            AddTile("CURRENT_STATE", "ARMED");
            AddTile("CURRENT_STATE", "ROLL");
            AddTile("CURRENT_STATE", "PITCH");
            AddTile("CURRENT_STATE", "YAW");
            AddTile("CURRENT_STATE", "REL_ALT");
            AddTile("CURRENT_STATE", "AMSL_ALT");
            AddTile("CURRENT_STATE", "AIR_SPEED");
            AddTile("CURRENT_STATE", "GROUND_SPEED");
            AddTile("CURRENT_STATE", "GPS_FIX");
            AddTile("CURRENT_STATE", "GPS_SATS");
            AddTile("CURRENT_STATE", "BATTERY1_VOLTAGE");
            AddTile("CURRENT_STATE", "BATTERY1_REMAINING");
            AddTile("CURRENT_STATE", "LINK_QUALITY");
            AddTile("CURRENT_STATE", "RSSI");
        }

        private void AddTile(string message, string field)
        {
            var key = new FieldKey
            {
                Message = message,
                Field = field
            };

            var tile = new TelemetryTileControl
            {
                Name = "tile_" + field.ToLowerInvariant()
            };

            tiles.Add((key, tile));
            flowLayoutPanelTiles.Controls.Add(tile);
        }
    }
}
