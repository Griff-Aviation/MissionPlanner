using MissionPlanner.Controls;
using MissionPlanner.MavlinkDashboard;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class MavlinkDashboardView : MyUserControl
    {
        public event EventHandler PopOutRequested;
        public event EventHandler PopInRequested;
        private bool isPoppedOut;
        private readonly IFieldValueSource fieldValueSource = new CurrentStateFieldValueSource();
        private readonly List<(DashboardTileConfig Config, TelemetryTileControl Tile)> tiles = new List<(DashboardTileConfig Config, TelemetryTileControl Tile)>();
        private readonly Button buttonResetDefaults = new Button();
        private DashboardConfig dashboardConfig;

        public MavlinkDashboardView()
        {
            InitializeComponent();
            InitializeResetButton();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetPoppedOutState(false);
            LoadDashboardConfig();
            RefreshTiles();
            Disposed += MavlinkDashboardView_Disposed;
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
                if (fieldValueSource.TryGetValue(tile.Config.FieldKey, out var value))
                {
                    if (!string.IsNullOrWhiteSpace(tile.Config.LabelOverride))
                    {
                        value.Label = tile.Config.LabelOverride;
                    }

                    if (!string.IsNullOrWhiteSpace(tile.Config.UnitsOverride))
                    {
                        value.Units = tile.Config.UnitsOverride;
                    }

                    tile.Tile.SetFieldValue(value);
                }
            }
        }

        private void LoadDashboardConfig()
        {
            dashboardConfig = DashboardConfigStore.LoadOrCreateDefault(CreateDefaultConfig);
            ApplyDashboardConfig(dashboardConfig);
        }

        private DashboardConfig CreateDefaultConfig()
        {
            var config = new DashboardConfig();
            AddDefaultTile(config, "CURRENT_STATE", "MODE");
            AddDefaultTile(config, "CURRENT_STATE", "ARMED");
            AddDefaultTile(config, "CURRENT_STATE", "ROLL");
            AddDefaultTile(config, "CURRENT_STATE", "PITCH");
            AddDefaultTile(config, "CURRENT_STATE", "YAW");
            AddDefaultTile(config, "CURRENT_STATE", "REL_ALT");
            AddDefaultTile(config, "CURRENT_STATE", "AMSL_ALT");
            AddDefaultTile(config, "CURRENT_STATE", "AIR_SPEED");
            AddDefaultTile(config, "CURRENT_STATE", "GROUND_SPEED");
            AddDefaultTile(config, "CURRENT_STATE", "GPS_FIX");
            AddDefaultTile(config, "CURRENT_STATE", "GPS_SATS");
            AddDefaultTile(config, "CURRENT_STATE", "BATTERY1_VOLTAGE");
            AddDefaultTile(config, "CURRENT_STATE", "BATTERY1_REMAINING");
            AddDefaultTile(config, "CURRENT_STATE", "LINK_QUALITY");
            AddDefaultTile(config, "CURRENT_STATE", "RSSI");
            return config;
        }

        private static void AddDefaultTile(DashboardConfig config, string message, string field)
        {
            config.Tiles.Add(new DashboardTileConfig
            {
                FieldKey = new FieldKey
                {
                    Message = message,
                    Field = field
                },
                TileType = "Value",
                Thresholds = new DashboardThresholdConfig()
            });
        }

        private void ApplyDashboardConfig(DashboardConfig config)
        {
            flowLayoutPanelTiles.SuspendLayout();
            flowLayoutPanelTiles.Controls.Clear();
            tiles.Clear();

            foreach (var tileConfig in config.Tiles)
            {
                AddTile(tileConfig);
            }

            flowLayoutPanelTiles.ResumeLayout();
        }

        private void AddTile(DashboardTileConfig tileConfig)
        {
            if (tileConfig?.FieldKey == null || string.IsNullOrWhiteSpace(tileConfig.FieldKey.Field))
            {
                return;
            }

            var fieldName = tileConfig.FieldKey.Field;

            var tile = new TelemetryTileControl
            {
                Name = "tile_" + fieldName.ToLowerInvariant()
            };

            if (dashboardConfig?.Layout != null)
            {
                tile.Size = new Size(
                    Math.Max(80, dashboardConfig.Layout.TileWidth),
                    Math.Max(50, dashboardConfig.Layout.TileHeight));
            }

            tiles.Add((tileConfig, tile));
            flowLayoutPanelTiles.Controls.Add(tile);
        }

        private void InitializeResetButton()
        {
            buttonResetDefaults.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonResetDefaults.Location = new Point(3, 6);
            buttonResetDefaults.Name = "buttonResetDefaults";
            buttonResetDefaults.Size = new Size(60, 23);
            buttonResetDefaults.TabIndex = 1;
            buttonResetDefaults.Text = "Reset";
            buttonResetDefaults.UseVisualStyleBackColor = true;
            buttonResetDefaults.Click += buttonResetDefaults_Click;
            panelTop.Controls.Add(buttonResetDefaults);
        }

        private void buttonResetDefaults_Click(object sender, EventArgs e)
        {
            dashboardConfig = CreateDefaultConfig();
            ApplyDashboardConfig(dashboardConfig);
            SaveDashboardConfig();
            RefreshTiles();
        }

        private void MavlinkDashboardView_Disposed(object sender, EventArgs e)
        {
            SaveDashboardConfig();
        }

        private void SaveDashboardConfig()
        {
            if (dashboardConfig == null)
            {
                return;
            }

            dashboardConfig.Layout = dashboardConfig.Layout ?? new DashboardLayoutConfig();

            if (tiles.Count > 0)
            {
                dashboardConfig.Layout.TileWidth = tiles[0].Tile.Width;
                dashboardConfig.Layout.TileHeight = tiles[0].Tile.Height;
            }

            dashboardConfig.Layout.ColumnsHint = GetColumnsHint();
            dashboardConfig.Tiles = new List<DashboardTileConfig>();

            foreach (var tile in tiles)
            {
                dashboardConfig.Tiles.Add(new DashboardTileConfig
                {
                    FieldKey = new FieldKey
                    {
                        Message = tile.Config.FieldKey.Message,
                        Field = tile.Config.FieldKey.Field,
                        InstanceId = tile.Config.FieldKey.InstanceId
                    },
                    LabelOverride = tile.Config.LabelOverride,
                    UnitsOverride = tile.Config.UnitsOverride,
                    Thresholds = new DashboardThresholdConfig
                    {
                        Warning = tile.Config.Thresholds?.Warning,
                        Critical = tile.Config.Thresholds?.Critical
                    },
                    TileType = tile.Config.TileType
                });
            }

            DashboardConfigStore.Save(dashboardConfig);
        }

        private int GetColumnsHint()
        {
            if (tiles.Count == 0)
            {
                return 1;
            }

            var tileWidth = tiles[0].Tile.Width + tiles[0].Tile.Margin.Horizontal;
            var availableWidth = Math.Max(1, flowLayoutPanelTiles.ClientSize.Width - flowLayoutPanelTiles.Padding.Horizontal);
            return Math.Max(1, availableWidth / Math.Max(1, tileWidth));
        }
    }
}
