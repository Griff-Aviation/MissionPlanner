using MissionPlanner.Controls;
using MissionPlanner.MavlinkDashboard;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class MavlinkDashboardView : MyUserControl
    {
        public event EventHandler PopOutRequested;
        public event EventHandler PopInRequested;
        private bool isPoppedOut;
        private readonly IFieldValueSource fieldValueSource = new CurrentStateFieldValueSource();
        // Keep config metadata and runtime control together so tile order/state stays in sync.
        private readonly List<(DashboardTileConfig Config, TelemetryTileControl Tile)> tiles = new List<(DashboardTileConfig Config, TelemetryTileControl Tile)>();
        private readonly Button buttonSaveConfig = new Button();
        private readonly Button buttonApplySavedConfig = new Button();
        private readonly ContextMenuStrip tileContextMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem configureTileMenuItem = new ToolStripMenuItem("Configure...");
        private readonly ToolStripMenuItem removeTileMenuItem = new ToolStripMenuItem("Remove");
        private readonly TextBox disconnectStatusTextBox = new TextBox();
        private static readonly string[] ThresholdOperators = { ">", "<", "==", "!=" };
        private static readonly Random textColorRandom = new Random();
        private const string ThemeDefaultTextColorOption = "Theme Default";
        // Keep these options aligned with Quick tab value colors.
        private static readonly string[] QuickValueTextColorOptions =
        {
            ThemeDefaultTextColorOption,
            "Blue",
            "Yellow",
            "Pink",
            "LimeGreen",
            "Orange",
            "Aqua",
            "LightCoral",
            "LightSteelBlue",
            "DarkKhaki",
            "LightYellow",
            "Violet",
            "YellowGreen",
            "OrangeRed",
            "Tomato",
            "Teal",
            "CornflowerBlue"
        };
        private readonly Button buttonExplorer = new Button();
        private const int DisconnectBorderThickness = 5;
        private const int DisconnectBorderInset = 5;
        private static readonly TimeSpan DisconnectBorderThreshold = TimeSpan.FromSeconds(5);
        private static readonly Color DisconnectBorderColor = Color.FromArgb(220, 64, 64);
        private static readonly Color DisconnectStatusTextColor = Color.Red;
        private ExplorerWindowForm explorerWindow;
        private DashboardConfig dashboardConfig;
        private TelemetryTileControl draggingTile;
        private Point dragStartPointScreen;
        private TelemetryTileControl contextMenuTargetTile;
        private TelemetryTileControl currentDropTargetTile;
        private bool allowExplorerWindowClose;
        private bool hasSeenAircraftConnection;
        private bool showDisconnectBorder;

        public MavlinkDashboardView()
        {
            InitializeComponent();
            InitializeSaveConfigButton();
            InitializeApplySavedConfigButton();
            InitializeExplorerButton();
            InitializeDisconnectStatusTextBox();
            InitializeTileContextMenu();
            InitializeEditModeSupport();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetPoppedOutState(false);
            LoadDashboardConfig();
            RefreshTiles();
            UpdateDisconnectBorderState();
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

        private void buttonExplorer_Click(object sender, EventArgs e)
        {
            ShowExplorerWindow();
        }

        private void uiTickTimer_Tick(object sender, EventArgs e)
        {
            // Keep tile values current from the live telemetry source.
            RefreshTiles();
            RefreshExplorerMessageList();
            UpdateDisconnectBorderState();
        }

        public void SetPoppedOutState(bool poppedOut)
        {
            isPoppedOut = poppedOut;
            buttonPopOut.Text = poppedOut ? "Pop In" : "Pop Out";
        }

        private void UpdateDisconnectBorderState()
        {
            bool isConnectedNow;
            try
            {
                isConnectedNow = IsAircraftTelemetryConnected();
            }
            catch
            {
                // Border logic must never interfere with connection or UI update flow.
                isConnectedNow = false;
            }

            if (isConnectedNow)
            {
                hasSeenAircraftConnection = true;
            }

            // Only raise the red border after a real connected->disconnected transition.
            var shouldShowBorder = hasSeenAircraftConnection && !isConnectedNow;
            if (showDisconnectBorder == shouldShowBorder)
            {
                return;
            }

            showDisconnectBorder = shouldShowBorder;
            disconnectStatusTextBox.Visible = showDisconnectBorder;
            disconnectStatusTextBox.BringToFront();
            flowLayoutPanelTiles.Invalidate();
        }

        private static bool IsAircraftTelemetryConnected()
        {
            var sampleTime = MainV2.comPort?.MAV?.cs?.datetime ?? DateTime.MinValue;
            if (sampleTime <= DateTime.MinValue.AddSeconds(1))
            {
                return false;
            }

            DateTime sampleTimeUtc;
            if (sampleTime.Kind == DateTimeKind.Utc)
            {
                sampleTimeUtc = sampleTime;
            }
            else if (sampleTime.Kind == DateTimeKind.Local)
            {
                sampleTimeUtc = sampleTime.ToUniversalTime();
            }
            else
            {
                sampleTimeUtc = DateTime.SpecifyKind(sampleTime, DateTimeKind.Local).ToUniversalTime();
            }

            return (DateTime.UtcNow - sampleTimeUtc) < DisconnectBorderThreshold;
        }

        private void RefreshTiles()
        {
            foreach (var tile in tiles)
            {
                if (fieldValueSource.TryGetValue(tile.Config.FieldKey, out var value))
                {
                    // UI overrides are applied after source formatting.
                    if (!string.IsNullOrWhiteSpace(tile.Config.LabelOverride))
                    {
                        value.Label = tile.Config.LabelOverride;
                    }

                    if (!string.IsNullOrWhiteSpace(tile.Config.UnitsOverride))
                    {
                        value.Units = tile.Config.UnitsOverride;
                    }

                    tile.Tile.SetValueTextColor(ResolveConfiguredTextColor(tile.Config));
                    value.State = ApplyThresholdState(value, tile.Config.Thresholds);
                    ApplyDecimalPlaces(value, tile.Config.DecimalPlaces);
                    tile.Tile.SetFieldValue(value);
                }
            }
        }

        private void LoadDashboardConfig()
        {
            // Missing/corrupt file falls back to shipped defaults and is re-saved by the store.
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
            AddDefaultTile(config, "CURRENT_STATE", "BATTERY2_VOLTAGE");
            AddDefaultTile(config, "CURRENT_STATE", "BATTERY2_REMAINING");
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
            // Rebuild panel from config order so startup/apply actions are deterministic.
            flowLayoutPanelTiles.SuspendLayout();
            flowLayoutPanelTiles.Controls.Clear();
            tiles.Clear();

            foreach (var tileConfig in config.Tiles)
            {
                if (tileConfig == null || !tileConfig.IsVisible)
                {
                    continue;
                }

                AddTile(tileConfig);
            }

            flowLayoutPanelTiles.ResumeLayout();
            RefreshExplorerFieldSelectionState();
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

            WireTileInteractions(tile);
            tile.SetValueTextColor(ResolveConfiguredTextColor(tileConfig));
            tiles.Add((tileConfig, tile));
            flowLayoutPanelTiles.Controls.Add(tile);
            tile.Cursor = Cursors.SizeAll;
            RefreshExplorerFieldSelectionState();
        }

        private void InitializeSaveConfigButton()
        {
            buttonSaveConfig.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonSaveConfig.Location = new Point(3, 6);
            buttonSaveConfig.Name = "buttonSaveConfig";
            buttonSaveConfig.Size = new Size(95, 23);
            buttonSaveConfig.TabIndex = 1;
            buttonSaveConfig.Text = "Save Config";
            buttonSaveConfig.UseVisualStyleBackColor = true;
            buttonSaveConfig.Click += buttonSaveConfig_Click;
            panelTop.Controls.Add(buttonSaveConfig);
        }

        private void InitializeApplySavedConfigButton()
        {
            buttonApplySavedConfig.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonApplySavedConfig.Location = new Point(101, 6);
            buttonApplySavedConfig.Name = "buttonApplySavedConfig";
            buttonApplySavedConfig.Size = new Size(130, 23);
            buttonApplySavedConfig.TabIndex = 2;
            buttonApplySavedConfig.Text = "Apply Saved Config";
            buttonApplySavedConfig.UseVisualStyleBackColor = true;
            buttonApplySavedConfig.Click += buttonApplySavedConfig_Click;
            panelTop.Controls.Add(buttonApplySavedConfig);
        }

        private void InitializeExplorerButton()
        {
            // Explorer is always available and opens in a separate modeless window.
            buttonExplorer.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonExplorer.Location = new Point(235, 6);
            buttonExplorer.Name = "buttonExplorer";
            buttonExplorer.Size = new Size(70, 23);
            buttonExplorer.TabIndex = 3;
            buttonExplorer.Text = "Explorer";
            buttonExplorer.UseVisualStyleBackColor = true;
            buttonExplorer.Click += buttonExplorer_Click;
            panelTop.Controls.Add(buttonExplorer);
        }

        private void InitializeDisconnectStatusTextBox()
        {
            disconnectStatusTextBox.Name = "disconnectStatusTextBox";
            disconnectStatusTextBox.ReadOnly = true;
            disconnectStatusTextBox.TabStop = false;
            disconnectStatusTextBox.BorderStyle = BorderStyle.FixedSingle;
            disconnectStatusTextBox.TextAlign = HorizontalAlignment.Center;
            disconnectStatusTextBox.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point);
            disconnectStatusTextBox.Text = "DISCONNECTED FROM AIRCRAFT";
            disconnectStatusTextBox.Width = 340;
            disconnectStatusTextBox.Height = 26;
            disconnectStatusTextBox.Visible = false;
            disconnectStatusTextBox.ForeColor = DisconnectStatusTextColor;
            disconnectStatusTextBox.BackColor = MissionPlanner.Utilities.ThemeManager.ControlBGColor;

            panelTop.Controls.Add(disconnectStatusTextBox);
            panelTop.Resize += panelTop_Resize;
            PositionDisconnectStatusTextBox();
        }

        private void panelTop_Resize(object sender, EventArgs e)
        {
            PositionDisconnectStatusTextBox();
        }

        private void PositionDisconnectStatusTextBox()
        {
            var x = (panelTop.ClientSize.Width - disconnectStatusTextBox.Width) / 2;
            disconnectStatusTextBox.Location = new Point(Math.Max(0, x), 5);
        }

        private void InitializeTileContextMenu()
        {
            configureTileMenuItem.Name = "configureTileMenuItem";
            configureTileMenuItem.Click += configureTileMenuItem_Click;
            removeTileMenuItem.Name = "removeTileMenuItem";
            removeTileMenuItem.Click += removeTileMenuItem_Click;
            tileContextMenu.Items.Add(configureTileMenuItem);
            tileContextMenu.Items.Add(removeTileMenuItem);
            tileContextMenu.Opening += tileContextMenu_Opening;
        }

        private void InitializeEditModeSupport()
        {
            // Reordering works through the panel-level drag/drop surface.
            flowLayoutPanelTiles.AllowDrop = true;
            flowLayoutPanelTiles.Paint += flowLayoutPanelTiles_Paint;
            flowLayoutPanelTiles.MouseDown += flowLayoutPanelTiles_MouseDown;
            flowLayoutPanelTiles.DragEnter += flowLayoutPanelTiles_DragEnter;
            flowLayoutPanelTiles.DragOver += flowLayoutPanelTiles_DragOver;
            flowLayoutPanelTiles.DragLeave += flowLayoutPanelTiles_DragLeave;
            flowLayoutPanelTiles.DragDrop += flowLayoutPanelTiles_DragDrop;
        }

        private void flowLayoutPanelTiles_Paint(object sender, PaintEventArgs e)
        {
            if (!showDisconnectBorder)
            {
                return;
            }

            var borderBounds = flowLayoutPanelTiles.ClientRectangle;
            if (borderBounds.Width <= 0 || borderBounds.Height <= 0)
            {
                return;
            }

            borderBounds.Inflate(-DisconnectBorderInset, -DisconnectBorderInset);
            if (borderBounds.Width <= 0 || borderBounds.Height <= 0)
            {
                return;
            }

            borderBounds.Width -= 1;
            borderBounds.Height -= 1;

            using (var pen = new Pen(DisconnectBorderColor, DisconnectBorderThickness))
            {
                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(pen, borderBounds);
            }
        }

        private void ShowExplorerWindow()
        {
            if (explorerWindow == null || explorerWindow.IsDisposed)
            {
                explorerWindow = new ExplorerWindowForm(IsFieldTileSelected, HandleExplorerFieldCheckedChanged, GetExplorerFieldLabelOverride);
                explorerWindow.FormClosing += explorerWindow_FormClosing;
                MissionPlanner.Utilities.ThemeManager.ApplyThemeTo(explorerWindow);
            }

            if (!explorerWindow.Visible)
            {
                if (explorerWindow.Owner == null)
                {
                    var owner = FindForm();
                    if (owner != null)
                    {
                        explorerWindow.Show(owner);
                    }
                    else
                    {
                        explorerWindow.Show();
                    }
                }
                else
                {
                    explorerWindow.Show();
                }
            }
            else
            {
                explorerWindow.BringToFront();
            }

            RefreshExplorerMessageList();
            RefreshExplorerFieldSelectionState();
        }

        private void RefreshExplorerMessageList()
        {
            if (explorerWindow == null || explorerWindow.IsDisposed || !explorerWindow.Visible)
            {
                return;
            }

            // Explorer previews are sourced from CurrentState (same model used by "Display This").
            explorerWindow.UpdateCurrentStatePreviewValues(MainV2.comPort?.MAV?.cs);
        }

        private void buttonSaveConfig_Click(object sender, EventArgs e)
        {
            SaveDashboardConfig();
            dashboardConfig.SavedTiles = CloneTileConfigList(dashboardConfig.Tiles);
            dashboardConfig.SavedLayout = CloneLayoutConfig(dashboardConfig.Layout);
            DashboardConfigStore.Save(dashboardConfig);
            ShowSaveButtonFeedback(buttonSaveConfig, "Save Config");
        }

        private void buttonApplySavedConfig_Click(object sender, EventArgs e)
        {
            if (dashboardConfig?.SavedTiles == null || dashboardConfig.SavedTiles.Count == 0)
            {
                return;
            }

            dashboardConfig.Tiles = CloneTileConfigList(dashboardConfig.SavedTiles);
            dashboardConfig.Layout = CloneLayoutConfig(dashboardConfig.SavedLayout) ?? dashboardConfig.Layout;
            ApplyDashboardConfig(dashboardConfig);
            SaveDashboardConfig();
            RefreshTiles();
        }

        private void WireTileInteractions(TelemetryTileControl tile)
        {
            WireTileControl(tile, tile);
        }

        private void WireTileControl(Control control, TelemetryTileControl ownerTile)
        {
            // Attach edit interactions to all child surfaces so dragging works from labels/padding too.
            control.ContextMenuStrip = tileContextMenu;
            control.MouseDown += (sender, e) => TileSurface_MouseDown(ownerTile, e);
            control.MouseMove += (sender, e) => TileSurface_MouseMove(ownerTile, e);
            control.MouseDoubleClick += (sender, e) => TileSurface_MouseDoubleClick(ownerTile, e);

            foreach (Control child in control.Controls)
            {
                WireTileControl(child, ownerTile);
            }
        }

        private void TileSurface_MouseDown(TelemetryTileControl tile, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // Capture drag origin; MouseMove starts actual drag after threshold.
            draggingTile = tile;
            dragStartPointScreen = Cursor.Position;
        }

        private void TileSurface_MouseMove(TelemetryTileControl tile, MouseEventArgs e)
        {
            if (draggingTile != tile)
            {
                return;
            }

            if ((Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
            {
                return;
            }

            if (!HasExceededDragThreshold(dragStartPointScreen, Cursor.Position))
            {
                return;
            }

            // Reset local drag state before invoking WinForms drag loop.
            draggingTile = null;
            try
            {
                tile.DoDragDrop(tile, DragDropEffects.Move);
            }
            finally
            {
                ClearDropTargetHighlight();
            }
        }

        private static bool HasExceededDragThreshold(Point startScreen, Point currentScreen)
        {
            // Use standard system drag threshold to avoid accidental reorders on simple clicks.
            return Math.Abs(currentScreen.X - startScreen.X) >= SystemInformation.DragSize.Width ||
                   Math.Abs(currentScreen.Y - startScreen.Y) >= SystemInformation.DragSize.Height;
        }

        private void flowLayoutPanelTiles_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(TelemetryTileControl))
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }

        private void flowLayoutPanelTiles_DragOver(object sender, DragEventArgs e)
        {
            // Keep WinForms drop effect updated and drive live hover highlighting while dragging.
            e.Effect = e.Data.GetDataPresent(typeof(TelemetryTileControl))
                ? DragDropEffects.Move
                : DragDropEffects.None;

            if (e.Effect != DragDropEffects.Move)
            {
                ClearDropTargetHighlight();
                return;
            }

            var dragged = e.Data.GetData(typeof(TelemetryTileControl)) as TelemetryTileControl;
            if (dragged == null)
            {
                ClearDropTargetHighlight();
                return;
            }

            var point = flowLayoutPanelTiles.PointToClient(new Point(e.X, e.Y));
            UpdateDropTargetHighlight(dragged, point);
        }

        private void flowLayoutPanelTiles_DragLeave(object sender, EventArgs e)
        {
            // Pointer left the drop surface; remove hover target cue.
            ClearDropTargetHighlight();
        }

        private void flowLayoutPanelTiles_DragDrop(object sender, DragEventArgs e)
        {
            ClearDropTargetHighlight();

            if (!e.Data.GetDataPresent(typeof(TelemetryTileControl)))
            {
                return;
            }

            var dragged = e.Data.GetData(typeof(TelemetryTileControl)) as TelemetryTileControl;
            if (dragged == null)
            {
                return;
            }

            var point = flowLayoutPanelTiles.PointToClient(new Point(e.X, e.Y));
            var target = GetTileFromControl(flowLayoutPanelTiles.GetChildAtPoint(point));
            // Drop on tile -> use tile index; drop in gap -> compute insertion slot from gap location.
            var targetIndex = target == null ? GetGapInsertionIndex(point) : GetTileIndex(target);
            MoveTile(dragged, targetIndex);
            SaveDashboardConfig();
        }

        private void flowLayoutPanelTiles_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // Clicking tile-free area clears current tile selection highlight.
            if (GetTileFromControl(flowLayoutPanelTiles.GetChildAtPoint(e.Location)) == null)
            {
                ClearTileSelectionVisuals();
            }
        }

        private void UpdateDropTargetHighlight(TelemetryTileControl dragged, Point pointerPointInPanel)
        {
            // Resolve tile under pointer; we only highlight actual target tiles, not gap space.
            var target = GetTileFromControl(flowLayoutPanelTiles.GetChildAtPoint(pointerPointInPanel));
            if (target == null || ReferenceEquals(target, dragged))
            {
                ClearDropTargetHighlight();
                return;
            }

            if (ReferenceEquals(target, currentDropTargetTile))
            {
                // Still over the same tile; no visual/state update needed.
                return;
            }

            // Pointer moved to a new tile, so old target highlight must be removed.
            SetCurrentDropTargetTile(target);
        }

        private void ClearDropTargetHighlight()
        {
            // Ensure only the actively hovered tile is highlighted.
            SetCurrentDropTargetTile(null);
        }

        private void SetCurrentDropTargetTile(TelemetryTileControl target)
        {
            if (ReferenceEquals(currentDropTargetTile, target))
            {
                return;
            }

            if (currentDropTargetTile != null)
            {
                // Reset previous hover target immediately when pointer moves away.
                currentDropTargetTile.IsDropTarget = false;
            }

            currentDropTargetTile = target;

            if (currentDropTargetTile != null)
            {
                // Only one tile can be marked as active drop target at a time.
                currentDropTargetTile.IsDropTarget = true;
            }
        }

        private int GetGapInsertionIndex(Point point)
        {
            // In empty space, insert at the tile that visually comes before the drop gap.
            for (var i = 0; i < tiles.Count; i++)
            {
                var bounds = tiles[i].Tile.Bounds;
                if (point.Y < bounds.Top)
                {
                    return Math.Max(0, i - 1);
                }

                if (point.Y <= bounds.Bottom && point.X < bounds.Left)
                {
                    return Math.Max(0, i - 1);
                }
            }

            return Math.Max(0, tiles.Count - 1);
        }

        private void MoveTile(TelemetryTileControl dragged, int targetIndex)
        {
            var sourceIndex = GetTileIndex(dragged);
            if (sourceIndex < 0 || sourceIndex == targetIndex)
            {
                return;
            }

            // Reorder the config/control tuple list first; panel controls mirror this list afterward.
            var tile = tiles[sourceIndex];
            tiles.RemoveAt(sourceIndex);

            targetIndex = Math.Max(0, Math.Min(targetIndex, tiles.Count));
            tiles.Insert(targetIndex, tile);
            RefreshTileOrderInPanel();
        }

        private void RefreshTileOrderInPanel()
        {
            flowLayoutPanelTiles.SuspendLayout();
            flowLayoutPanelTiles.Controls.Clear();

            // Controls order in the panel is derived solely from the tiles list order.
            foreach (var tile in tiles)
            {
                flowLayoutPanelTiles.Controls.Add(tile.Tile);
            }

            flowLayoutPanelTiles.ResumeLayout();
        }

        private void tileContextMenu_Opening(object sender, CancelEventArgs e)
        {
            // Resolve the owning tile from whichever inner child was right-clicked.
            contextMenuTargetTile = GetTileFromControl(tileContextMenu.SourceControl);
            configureTileMenuItem.Enabled = contextMenuTargetTile != null;
            removeTileMenuItem.Enabled = contextMenuTargetTile != null;

            if (contextMenuTargetTile == null)
            {
                e.Cancel = true;
            }
        }

        private void removeTileMenuItem_Click(object sender, EventArgs e)
        {
            if (contextMenuTargetTile == null)
            {
                return;
            }

            var index = GetTileIndex(contextMenuTargetTile);
            if (index < 0)
            {
                return;
            }

            RemoveTileForFieldKey(tiles[index].Config.FieldKey);
            contextMenuTargetTile = null;
            RefreshTiles();
            SaveDashboardConfig();
            RefreshExplorerFieldSelectionState();
        }

        private void configureTileMenuItem_Click(object sender, EventArgs e)
        {
            ConfigureTile(contextMenuTargetTile);
        }

        private int GetTileIndex(TelemetryTileControl tile)
        {
            for (var i = 0; i < tiles.Count; i++)
            {
                if (ReferenceEquals(tiles[i].Tile, tile))
                {
                    return i;
                }
            }

            return -1;
        }

        private static TelemetryTileControl GetTileFromControl(Control control)
        {
            // Walk up from child labels/panels to the root tile control.
            while (control != null && !(control is TelemetryTileControl))
            {
                control = control.Parent;
            }

            return control as TelemetryTileControl;
        }

        private void TileSurface_MouseDoubleClick(TelemetryTileControl tile, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ConfigureTile(tile);
        }

        private void ConfigureTile(TelemetryTileControl tile)
        {
            if (tile == null)
            {
                return;
            }

            var index = GetTileIndex(tile);
            if (index < 0)
            {
                return;
            }

            var config = tiles[index].Config;
            if (!ShowTileConfigurationDialog(config))
            {
                return;
            }

            RefreshTiles();
            SaveDashboardConfig();
            RefreshExplorerFieldSelectionState();
        }

        private void ClearTileSelectionVisuals()
        {
            foreach (var tile in tiles)
            {
                tile.Tile.SetSelectionVisual(false);
            }
        }

        private void MavlinkDashboardView_Disposed(object sender, EventArgs e)
        {
            if (explorerWindow != null && !explorerWindow.IsDisposed)
            {
                allowExplorerWindowClose = true;
                explorerWindow.Close();
                explorerWindow.Dispose();
                explorerWindow = null;
            }

            SaveDashboardConfig();
        }

        private void explorerWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowExplorerWindowClose || e.CloseReason != CloseReason.UserClosing)
            {
                return;
            }

            // Keep explorer instance alive so reopening restores the same screen position.
            e.Cancel = true;
            explorerWindow.Hide();
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
            var persistedTiles = new List<DashboardTileConfig>();
            var existingTiles = dashboardConfig.Tiles ?? new List<DashboardTileConfig>();

            foreach (var tile in tiles)
            {
                // Visible dashboard tiles are persisted first, in the current on-screen order.
                var visibleConfig = CloneTileConfig(tile.Config);
                visibleConfig.IsVisible = true;
                persistedTiles.Add(visibleConfig);
            }

            foreach (var existing in existingTiles)
            {
                if (existing?.FieldKey == null || string.IsNullOrWhiteSpace(existing.FieldKey.Field))
                {
                    continue;
                }

                var alreadyPersisted = persistedTiles.Exists(x => FieldKeyEquals(x.FieldKey, existing.FieldKey));
                if (alreadyPersisted)
                {
                    continue;
                }

                // Keep non-visible entries so re-checking in explorer restores prior tile configuration.
                var hiddenConfig = CloneTileConfig(existing);
                hiddenConfig.IsVisible = false;
                persistedTiles.Add(hiddenConfig);
            }

            dashboardConfig.Tiles = persistedTiles;
            DashboardConfigStore.Save(dashboardConfig);
        }

        private bool ShowTileConfigurationDialog(DashboardTileConfig config)
        {
            if (config?.FieldKey == null)
            {
                return false;
            }

            var originalConfig = CloneTileConfig(config);
            config.Thresholds = config.Thresholds ?? new DashboardThresholdConfig();

            using (var dialog = new Form())
            {
                // Prefill editable fields with effective defaults so the user starts from current behavior.
                GetDefaultTileTextValues(config, out var defaultLabelText, out var defaultUnitsText);
                dialog.Text = "Configure Tile";
                dialog.Name = "configureTileDialog";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(360, 260);

                var textLabel = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(config.LabelOverride) ? defaultLabelText : config.LabelOverride,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                var textUnits = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(config.UnitsOverride) ? defaultUnitsText : config.UnitsOverride,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                var comboTextColor = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                comboTextColor.Items.AddRange(QuickValueTextColorOptions);
                comboTextColor.SelectedItem = ResolveTextColorOption(config.TextColorName);
                var buttonRandomTextColor = new Button
                {
                    Text = "Rnd",
                    Width = 44,
                    Anchor = AnchorStyles.Right
                };
                buttonRandomTextColor.Click += (s, e) =>
                {
                    const int firstColorIndex = 1; // Skip "Theme Default" for random color picks.
                    if (comboTextColor.Items.Count <= firstColorIndex)
                    {
                        return;
                    }

                    comboTextColor.SelectedIndex = textColorRandom.Next(firstColorIndex, comboTextColor.Items.Count);
                };
                var colorEditor = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty
                };
                colorEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                colorEditor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
                colorEditor.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                colorEditor.Controls.Add(comboTextColor, 0, 0);
                colorEditor.Controls.Add(buttonRandomTextColor, 1, 0);
                var existingDecimals = config.DecimalPlaces.HasValue
                    ? Math.Max(0, Math.Min(6, config.DecimalPlaces.Value))
                    : -1;
                var checkAutoDecimals = new CheckBox
                {
                    AutoSize = true,
                    Checked = !config.DecimalPlaces.HasValue
                };
                var inputCustomDecimals = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = 6,
                    Value = existingDecimals >= 0 ? existingDecimals : 1,
                    Anchor = AnchorStyles.Left
                };
                inputCustomDecimals.Enabled = !checkAutoDecimals.Checked;
                checkAutoDecimals.CheckedChanged += (s, e) =>
                {
                    inputCustomDecimals.Enabled = !checkAutoDecimals.Checked;
                };

                var decimalsEditor = CreateDecimalPlacesEditor(checkAutoDecimals, inputCustomDecimals);

                var comboWarningOperator = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                comboWarningOperator.Items.AddRange(ThresholdOperators);
                comboWarningOperator.SelectedItem = ResolveThresholdOperator(config.Thresholds?.WarningOperator);

                var textWarning = new TextBox
                {
                    Text = GetThresholdEditorValue(config.Thresholds?.WarningOperator, config.Thresholds?.Warning, config.Thresholds?.WarningText),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                var comboCriticalOperator = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                comboCriticalOperator.Items.AddRange(ThresholdOperators);
                comboCriticalOperator.SelectedItem = ResolveThresholdOperator(config.Thresholds?.CriticalOperator);

                var textCritical = new TextBox
                {
                    Text = GetThresholdEditorValue(config.Thresholds?.CriticalOperator, config.Thresholds?.Critical, config.Thresholds?.CriticalText),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };

                var warningEditor = CreateThresholdEditor(comboWarningOperator, textWarning);
                var criticalEditor = CreateThresholdEditor(comboCriticalOperator, textCritical);

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    ColumnCount = 2,
                    RowCount = 8
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                AddConfigRow(layout, 1, "Label", textLabel);
                AddConfigRow(layout, 2, "Units", textUnits);
                AddConfigRow(layout, 3, "Value color", colorEditor);
                AddConfigRow(layout, 4, "Decimal pts", decimalsEditor);
                AddConfigRow(layout, 5, "Warning", warningEditor);
                AddConfigRow(layout, 6, "Critical", criticalEditor);
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft
                };
                var buttonPresetPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight
                };

                var buttonOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                var buttonCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Abort, AutoSize = true };
                var buttonResetDefaults = new Button { Text = "Reset", AutoSize = true };
                var buttonSaveConfig = new Button { Text = "Save Config", AutoSize = true };
                var buttonApplySavedConfig = new Button
                {
                    Text = "Apply Saved Config",
                    AutoSize = true,
                    Enabled = FindSavedTileConfig(config.FieldKey) != null
                };
                buttonPanel.Controls.Add(buttonOk);
                buttonPanel.Controls.Add(buttonCancel);
                buttonPresetPanel.Controls.Add(buttonResetDefaults);
                buttonPresetPanel.Controls.Add(buttonSaveConfig);
                buttonPresetPanel.Controls.Add(buttonApplySavedConfig);
                buttonPresetPanel.WrapContents = false;
                buttonPanel.WrapContents = false;
                layout.Controls.Add(buttonPresetPanel, 0, 0);
                layout.SetColumnSpan(buttonPresetPanel, 2);
                layout.Controls.Add(buttonPanel, 1, 7);

                dialog.Controls.Add(layout);
                dialog.AcceptButton = buttonOk;
                dialog.CancelButton = buttonCancel;
                MissionPlanner.Utilities.ThemeManager.ApplyThemeTo(dialog);

                Func<bool, bool> applyEditorValuesToConfig = requireValidThresholds =>
                {
                    var warningOperator = ResolveThresholdOperator(comboWarningOperator.SelectedItem as string);
                    var criticalOperator = ResolveThresholdOperator(comboCriticalOperator.SelectedItem as string);
                    var warningText = NormalizeThresholdText(textWarning.Text);
                    var criticalText = NormalizeThresholdText(textCritical.Text);
                    var warningParsed = TryParseThresholdValueForOperator(warningOperator, warningText, out var warningValue);
                    var criticalParsed = TryParseThresholdValueForOperator(criticalOperator, criticalText, out var criticalValue);
                    if (requireValidThresholds && (!warningParsed || !criticalParsed))
                    {
                        return false;
                    }

                    var labelOverride = NormalizeOverrideText(textLabel.Text);
                    var unitsOverride = NormalizeOverrideText(textUnits.Text);
                    var normalizedDefaultLabel = NormalizeOverrideText(defaultLabelText);
                    var normalizedDefaultUnits = NormalizeOverrideText(defaultUnitsText);
                    config.LabelOverride = string.Equals(labelOverride, normalizedDefaultLabel, StringComparison.Ordinal) ? null : labelOverride;
                    config.UnitsOverride = string.Equals(unitsOverride, normalizedDefaultUnits, StringComparison.Ordinal) ? null : unitsOverride;
                    config.TextColorName = NormalizeTextColorSelection(comboTextColor.SelectedItem as string);
                    config.DecimalPlaces = checkAutoDecimals.Checked ? (int?)null : (int)inputCustomDecimals.Value;
                    config.Thresholds.WarningOperator = warningOperator;
                    config.Thresholds.CriticalOperator = criticalOperator;
                    config.Thresholds.WarningText = IsStringThresholdOperator(warningOperator) ? warningText : null;
                    config.Thresholds.CriticalText = IsStringThresholdOperator(criticalOperator) ? criticalText : null;

                    // Ignore partially typed invalid threshold text during live preview.
                    if (warningParsed)
                    {
                        config.Thresholds.Warning = warningValue;
                    }

                    if (criticalParsed)
                    {
                        config.Thresholds.Critical = criticalValue;
                    }

                    return true;
                };

                Action refreshPreview = () =>
                {
                    applyEditorValuesToConfig(false);
                    RefreshTiles();
                };

                Action<DashboardTileConfig> loadEditorsFromConfig = source =>
                {
                    textLabel.Text = string.IsNullOrWhiteSpace(source?.LabelOverride) ? defaultLabelText : source.LabelOverride;
                    textUnits.Text = string.IsNullOrWhiteSpace(source?.UnitsOverride) ? defaultUnitsText : source.UnitsOverride;
                    comboTextColor.SelectedItem = ResolveTextColorOption(source?.TextColorName);
                    if (comboTextColor.SelectedIndex < 0 && comboTextColor.Items.Count > 0)
                    {
                        comboTextColor.SelectedIndex = 0;
                    }

                    var sourceDecimals = source?.DecimalPlaces;
                    checkAutoDecimals.Checked = !sourceDecimals.HasValue;
                    inputCustomDecimals.Value = sourceDecimals.HasValue
                        ? Math.Max(inputCustomDecimals.Minimum, Math.Min(inputCustomDecimals.Maximum, sourceDecimals.Value))
                        : 1;

                    comboWarningOperator.SelectedItem = ResolveThresholdOperator(source?.Thresholds?.WarningOperator);
                    comboCriticalOperator.SelectedItem = ResolveThresholdOperator(source?.Thresholds?.CriticalOperator);
                    textWarning.Text = GetThresholdEditorValue(source?.Thresholds?.WarningOperator, source?.Thresholds?.Warning, source?.Thresholds?.WarningText);
                    textCritical.Text = GetThresholdEditorValue(source?.Thresholds?.CriticalOperator, source?.Thresholds?.Critical, source?.Thresholds?.CriticalText);
                };

                EventHandler livePreviewChanged = (s, e) =>
                {
                    refreshPreview();
                };

                textLabel.TextChanged += livePreviewChanged;
                textUnits.TextChanged += livePreviewChanged;
                comboTextColor.SelectedIndexChanged += livePreviewChanged;
                checkAutoDecimals.CheckedChanged += livePreviewChanged;
                inputCustomDecimals.ValueChanged += livePreviewChanged;
                comboWarningOperator.SelectedIndexChanged += livePreviewChanged;
                textWarning.TextChanged += livePreviewChanged;
                comboCriticalOperator.SelectedIndexChanged += livePreviewChanged;
                textCritical.TextChanged += livePreviewChanged;
                buttonResetDefaults.Click += (s, e) =>
                {
                    // Reset editor controls to baseline defaults for this selected field.
                    loadEditorsFromConfig(new DashboardTileConfig
                    {
                        Thresholds = new DashboardThresholdConfig()
                    });
                    refreshPreview();
                };
                buttonSaveConfig.Click += (s, e) =>
                {
                    if (!applyEditorValuesToConfig(true))
                    {
                        MessageBox.Show(dialog, "For operators > and <, Warning/Critical thresholds must be numeric values.", "Invalid Threshold",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Config dialog saves write into the same dashboard saved-config set.
                    UpsertSavedTileConfig(config);
                    dashboardConfig.SavedLayout = CloneLayoutConfig(dashboardConfig.Layout);
                    DashboardConfigStore.Save(dashboardConfig);
                    buttonApplySavedConfig.Enabled = true;
                    ShowSaveButtonFeedback(buttonSaveConfig, "Save Config");
                };
                buttonApplySavedConfig.Click += (s, e) =>
                {
                    var savedConfig = FindSavedTileConfig(config.FieldKey);
                    if (savedConfig == null)
                    {
                        return;
                    }

                    loadEditorsFromConfig(savedConfig);
                    refreshPreview();
                };

                var dialogResult = dialog.ShowDialog(this);
                if (dialogResult == DialogResult.Abort)
                {
                    // Cancel discards transient preview edits applied while typing.
                    CopyTileConfig(originalConfig, config);
                    RefreshTiles();
                    return false;
                }

                if (dialogResult == DialogResult.OK && !applyEditorValuesToConfig(true))
                {
                    CopyTileConfig(originalConfig, config);
                    RefreshTiles();
                    MessageBox.Show(this, "For operators > and <, Warning/Critical thresholds must be numeric values.", "Invalid Threshold",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                return true;
            }
        }

        private static string NormalizeOverrideText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static void ShowSaveButtonFeedback(Button button, string restoreText)
        {
            if (button == null || button.IsDisposed)
            {
                return;
            }

            button.Text = "Saved";
            var timer = new Timer { Interval = 900 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!button.IsDisposed)
                {
                    button.Text = restoreText;
                }
            };
            timer.Start();
        }

        private void GetDefaultTileTextValues(DashboardTileConfig config, out string label, out string units)
        {
            label = config?.FieldKey?.Field ?? string.Empty;
            units = string.Empty;

            if (config?.FieldKey == null)
            {
                return;
            }

            if (fieldValueSource.TryGetValue(config.FieldKey, out var fieldValue))
            {
                if (!string.IsNullOrWhiteSpace(fieldValue?.Label))
                {
                    label = fieldValue.Label;
                }

                if (!string.IsNullOrWhiteSpace(fieldValue?.Units))
                {
                    units = fieldValue.Units;
                }
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = config.FieldKey.Field;
            }
        }

        private static string ResolveThresholdOperator(string value)
        {
            foreach (var op in ThresholdOperators)
            {
                if (string.Equals(op, value, StringComparison.Ordinal))
                {
                    return op;
                }
            }

            return ">";
        }

        private static bool IsStringThresholdOperator(string thresholdOperator)
        {
            var op = ResolveThresholdOperator(thresholdOperator);
            return op == "==" || op == "!=";
        }

        private static string NormalizeThresholdText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static string GetThresholdEditorValue(string thresholdOperator, double? numericValue, string stringValue)
        {
            if (IsStringThresholdOperator(thresholdOperator))
            {
                var text = NormalizeThresholdText(stringValue);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            return numericValue.HasValue
                ? numericValue.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static bool TryParseThresholdValueForOperator(string thresholdOperator, string thresholdText, out double? numericValue)
        {
            numericValue = null;
            if (IsStringThresholdOperator(thresholdOperator))
            {
                return true;
            }

            return TryParseNullableDouble(thresholdText, out numericValue);
        }

        private static string ResolveTextColorOption(string configuredColorName)
        {
            foreach (var option in QuickValueTextColorOptions)
            {
                if (string.Equals(option, configuredColorName, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return ThemeDefaultTextColorOption;
        }

        private static string NormalizeTextColorSelection(string selectedOption)
        {
            if (string.IsNullOrWhiteSpace(selectedOption) ||
                string.Equals(selectedOption, ThemeDefaultTextColorOption, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return selectedOption;
        }

        private static Color? ResolveConfiguredTextColor(DashboardTileConfig config)
        {
            var colorName = config?.TextColorName;
            if (string.IsNullOrWhiteSpace(colorName))
            {
                return null;
            }

            var color = Color.FromName(colorName.Trim());
            if (!color.IsNamedColor && !color.IsKnownColor)
            {
                return null;
            }

            return color;
        }

        private static void ApplyDecimalPlaces(FieldValue fieldValue, int? decimalPlaces)
        {
            if (fieldValue == null || !decimalPlaces.HasValue)
            {
                return;
            }

            if (!TryParseNumericValue(fieldValue.FormattedValue, out var numericValue))
            {
                return;
            }

            fieldValue.FormattedValue = numericValue.ToString("F" + decimalPlaces.Value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static bool TryParseNullableDouble(string text, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantParsed))
            {
                value = invariantParsed;
                return true;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var localParsed))
            {
                value = localParsed;
                return true;
            }

            return false;
        }

        private static void AddConfigRow(TableLayoutPanel layout, int rowIndex, string label, Control editor)
        {
            while (layout.RowStyles.Count <= rowIndex)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            }

            var rowLabel = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            editor.Dock = DockStyle.Fill;
            layout.Controls.Add(rowLabel, 0, rowIndex);
            layout.Controls.Add(editor, 1, rowIndex);
        }

        private static Control CreateDecimalPlacesEditor(CheckBox autoCheckBox, NumericUpDown customInput)
        {
            var container = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var labelAuto = new Label
            {
                Text = "auto",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left
            };
            var labelCustom = new Label
            {
                Text = "custom:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left
            };

            autoCheckBox.Anchor = AnchorStyles.Left;
            customInput.Anchor = AnchorStyles.Left;
            container.Controls.Add(labelAuto, 0, 0);
            container.Controls.Add(autoCheckBox, 1, 0);
            container.Controls.Add(labelCustom, 2, 0);
            container.Controls.Add(customInput, 3, 0);
            return container;
        }

        private static Control CreateThresholdEditor(ComboBox operatorSelector, TextBox valueEditor)
        {
            // Keep operator and threshold input together on one row.
            var container = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            operatorSelector.Dock = DockStyle.Fill;
            valueEditor.Dock = DockStyle.Fill;
            container.Controls.Add(operatorSelector, 0, 0);
            container.Controls.Add(valueEditor, 1, 0);
            return container;
        }

        private static FieldState ApplyThresholdState(FieldValue fieldValue, DashboardThresholdConfig thresholds)
        {
            if (fieldValue == null || fieldValue.State == FieldState.Inactive || fieldValue.State == FieldState.DisconnectWarning)
            {
                return fieldValue == null ? FieldState.Inactive : fieldValue.State;
            }

            if (thresholds == null)
            {
                return fieldValue.State;
            }

            var hasCriticalThreshold = IsThresholdConfigured(thresholds.CriticalOperator, thresholds.Critical, thresholds.CriticalText);
            var hasWarningThreshold = IsThresholdConfigured(thresholds.WarningOperator, thresholds.Warning, thresholds.WarningText);
            if (!hasWarningThreshold && !hasCriticalThreshold)
            {
                return fieldValue.State;
            }

            var thresholdState = FieldState.Normal;
            if (hasCriticalThreshold &&
                ThresholdCrossed(fieldValue.FormattedValue, thresholds.Critical, thresholds.CriticalText, thresholds.CriticalOperator))
            {
                thresholdState = FieldState.Critical;
            }
            else if (hasWarningThreshold &&
                     ThresholdCrossed(fieldValue.FormattedValue, thresholds.Warning, thresholds.WarningText, thresholds.WarningOperator))
            {
                thresholdState = FieldState.Warning;
            }

            return MaxState(fieldValue.State, thresholdState);
        }

        private static bool IsThresholdConfigured(string thresholdOperator, double? numericThreshold, string textThreshold)
        {
            if (IsStringThresholdOperator(thresholdOperator))
            {
                return !string.IsNullOrWhiteSpace(ResolveStringThresholdValue(textThreshold, numericThreshold));
            }

            return numericThreshold.HasValue;
        }

        private static bool ThresholdCrossed(string fieldValue, double? numericThreshold, string textThreshold, string comparisonOperator)
        {
            var op = ResolveThresholdOperator(comparisonOperator);
            if (IsStringThresholdOperator(op))
            {
                var compareValue = ResolveStringThresholdValue(textThreshold, numericThreshold);
                if (string.IsNullOrWhiteSpace(compareValue))
                {
                    return false;
                }

                var equal = string.Equals((fieldValue ?? string.Empty).Trim(), compareValue, StringComparison.OrdinalIgnoreCase);
                return op == "==" ? equal : !equal;
            }

            if (!numericThreshold.HasValue || !TryParseNumericValue(fieldValue, out var numericValue))
            {
                return false;
            }

            return ThresholdCrossed(numericValue, numericThreshold.Value, op);
        }

        private static string ResolveStringThresholdValue(string textThreshold, double? numericThreshold)
        {
            var normalized = NormalizeThresholdText(textThreshold);
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }

            return numericThreshold.HasValue
                ? numericThreshold.Value.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        private static bool ThresholdCrossed(double value, double threshold, string comparisonOperator)
        {
            switch (ResolveThresholdOperator(comparisonOperator))
            {
                case "<":
                    return value < threshold;
                default:
                    return value > threshold;
            }
        }

        private static FieldState MaxState(FieldState left, FieldState right)
        {
            if (left == FieldState.Inactive || right == FieldState.Inactive)
            {
                return FieldState.Inactive;
            }

            if (left == FieldState.DisconnectWarning || right == FieldState.DisconnectWarning)
            {
                return FieldState.DisconnectWarning;
            }

            if (left == FieldState.Critical || right == FieldState.Critical)
            {
                return FieldState.Critical;
            }

            if (left == FieldState.Warning || right == FieldState.Warning)
            {
                return FieldState.Warning;
            }

            return FieldState.Normal;
        }

        private static bool TryParseNumericValue(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantParsed))
            {
                value = invariantParsed;
                return true;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var localParsed))
            {
                value = localParsed;
                return true;
            }

            return false;
        }

        private void RefreshExplorerFieldSelectionState()
        {
            if (explorerWindow == null || explorerWindow.IsDisposed)
            {
                return;
            }

            explorerWindow.RefreshFieldSelectionState();
        }

        private bool IsFieldTileSelected(FieldKey fieldKey)
        {
            return GetTileIndexByFieldKey(fieldKey) >= 0;
        }

        private string GetExplorerFieldLabelOverride(FieldKey fieldKey)
        {
            var config = FindStoredTileConfig(fieldKey);
            if (config == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(config.LabelOverride))
            {
                return config.LabelOverride.Trim();
            }

            // Also surface default tile labels so explorer entries always show the current tile label text.
            GetDefaultTileTextValues(config, out var defaultLabelText, out _);
            return NormalizeOverrideText(defaultLabelText);
        }

        private void HandleExplorerFieldCheckedChanged(FieldKey fieldKey, bool isChecked)
        {
            if (fieldKey == null)
            {
                return;
            }

            if (isChecked)
            {
                EnsureTileForFieldKey(fieldKey);
            }
            else
            {
                RemoveTileForFieldKey(fieldKey);
            }

            RefreshTiles();
            SaveDashboardConfig();
            RefreshExplorerFieldSelectionState();
        }

        private void EnsureTileForFieldKey(FieldKey fieldKey)
        {
            if (GetTileIndexByFieldKey(fieldKey) >= 0)
            {
                return;
            }

            var existingConfig = FindStoredTileConfig(fieldKey);
            if (existingConfig != null)
            {
                existingConfig.IsVisible = true;
                AddTile(existingConfig);
                return;
            }

            AddTile(new DashboardTileConfig
            {
                FieldKey = CloneFieldKey(fieldKey),
                IsVisible = true,
                TileType = "Value",
                Thresholds = new DashboardThresholdConfig()
            });
        }

        private void RemoveTileForFieldKey(FieldKey fieldKey)
        {
            var index = GetTileIndexByFieldKey(fieldKey);
            if (index < 0)
            {
                return;
            }

            tiles[index].Config.IsVisible = false;
            var tileControl = tiles[index].Tile;
            flowLayoutPanelTiles.Controls.Remove(tileControl);
            tileControl.Dispose();
            tiles.RemoveAt(index);
        }

        private int GetTileIndexByFieldKey(FieldKey fieldKey)
        {
            for (var index = 0; index < tiles.Count; index++)
            {
                if (FieldKeyEquals(tiles[index].Config.FieldKey, fieldKey))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool FieldKeyEquals(FieldKey left, FieldKey right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            var leftMessage = left.Message ?? string.Empty;
            var rightMessage = right.Message ?? string.Empty;
            var useCurrentStateAlias =
                string.Equals(leftMessage, "CURRENT_STATE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rightMessage, "CURRENT_STATE", StringComparison.OrdinalIgnoreCase);

            var leftField = useCurrentStateAlias ? NormalizeCurrentStateFieldName(left.Field) : (left.Field ?? string.Empty);
            var rightField = useCurrentStateAlias ? NormalizeCurrentStateFieldName(right.Field) : (right.Field ?? string.Empty);

            return string.Equals(leftMessage, rightMessage, StringComparison.OrdinalIgnoreCase)
                && string.Equals(leftField, rightField, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCurrentStateFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            switch (fieldName.ToUpperInvariant())
            {
                case "MODE":
                    return "mode";
                case "ARMED":
                    return "armed";
                case "ROLL":
                    return "roll";
                case "PITCH":
                    return "pitch";
                case "YAW":
                    return "yaw";
                case "REL_ALT":
                    return "alt";
                case "AMSL_ALT":
                    return "altasl";
                case "AIR_SPEED":
                    return "airspeed";
                case "GROUND_SPEED":
                    return "groundspeed";
                case "GPS_FIX":
                    return "gpsstatus";
                case "GPS_SATS":
                    return "satcount";
                case "BATTERY1_VOLTAGE":
                    return "battery_voltage";
                case "BATTERY1_REMAINING":
                    return "battery_remaining";
                case "BATTERY2_VOLTAGE":
                    return "battery_voltage2";
                case "BATTERY2_REMAINING":
                    return "battery_remaining2";
                case "LINK_QUALITY":
                    return "linkqualitygcs";
                case "RSSI":
                    return "rssi";
                default:
                    return fieldName;
            }
        }

        private static FieldKey CloneFieldKey(FieldKey fieldKey)
        {
            return new FieldKey
            {
                Message = fieldKey.Message,
                Field = fieldKey.Field
            };
        }

        private DashboardTileConfig FindStoredTileConfig(FieldKey fieldKey)
        {
            if (dashboardConfig?.Tiles == null)
            {
                return null;
            }

            foreach (var config in dashboardConfig.Tiles)
            {
                if (config?.FieldKey != null && FieldKeyEquals(config.FieldKey, fieldKey))
                {
                    return config;
                }
            }

            return null;
        }

        private DashboardTileConfig FindSavedTileConfig(FieldKey fieldKey)
        {
            if (dashboardConfig?.SavedTiles == null)
            {
                return null;
            }

            foreach (var saved in dashboardConfig.SavedTiles)
            {
                if (saved?.FieldKey != null && FieldKeyEquals(saved.FieldKey, fieldKey))
                {
                    return saved;
                }
            }

            return null;
        }

        private void UpsertSavedTileConfig(DashboardTileConfig sourceConfig)
        {
            if (dashboardConfig == null || sourceConfig?.FieldKey == null)
            {
                return;
            }

            if (dashboardConfig.SavedLayout == null)
            {
                dashboardConfig.SavedLayout = CloneLayoutConfig(dashboardConfig.Layout);
            }

            if (dashboardConfig.SavedTiles == null || dashboardConfig.SavedTiles.Count == 0)
            {
                dashboardConfig.SavedTiles = CloneTileConfigList(dashboardConfig.Tiles);
            }

            var savedTiles = dashboardConfig.SavedTiles;
            for (var i = 0; i < savedTiles.Count; i++)
            {
                var saved = savedTiles[i];
                if (saved?.FieldKey == null || !FieldKeyEquals(saved.FieldKey, sourceConfig.FieldKey))
                {
                    continue;
                }

                var replacement = CloneTileConfig(sourceConfig);
                replacement.IsVisible = saved.IsVisible;
                savedTiles[i] = replacement;
                return;
            }

            savedTiles.Add(CloneTileConfig(sourceConfig));
        }

        private static DashboardTileConfig CloneTileConfig(DashboardTileConfig source)
        {
            if (source == null)
            {
                return null;
            }

            // Persist a copy, not live references, to keep runtime objects decoupled from saved state.
            return new DashboardTileConfig
            {
                FieldKey = source.FieldKey == null ? null : new FieldKey
                {
                    Message = source.FieldKey.Message,
                    Field = source.FieldKey.Field
                },
                IsVisible = source.IsVisible,
                LabelOverride = source.LabelOverride,
                UnitsOverride = source.UnitsOverride,
                TextColorName = source.TextColorName,
                DecimalPlaces = source.DecimalPlaces,
                Thresholds = new DashboardThresholdConfig
                {
                    WarningOperator = source.Thresholds?.WarningOperator,
                    Warning = source.Thresholds?.Warning,
                    WarningText = source.Thresholds?.WarningText,
                    CriticalOperator = source.Thresholds?.CriticalOperator,
                    Critical = source.Thresholds?.Critical,
                    CriticalText = source.Thresholds?.CriticalText
                },
                TileType = source.TileType
            };
        }

        private static void CopyTileConfig(DashboardTileConfig source, DashboardTileConfig target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.FieldKey = source.FieldKey == null ? null : CloneFieldKey(source.FieldKey);
            target.IsVisible = source.IsVisible;
            target.LabelOverride = source.LabelOverride;
            target.UnitsOverride = source.UnitsOverride;
            target.TextColorName = source.TextColorName;
            target.DecimalPlaces = source.DecimalPlaces;
            target.TileType = source.TileType;
            target.Thresholds = new DashboardThresholdConfig
            {
                WarningOperator = source.Thresholds?.WarningOperator,
                Warning = source.Thresholds?.Warning,
                WarningText = source.Thresholds?.WarningText,
                CriticalOperator = source.Thresholds?.CriticalOperator,
                Critical = source.Thresholds?.Critical,
                CriticalText = source.Thresholds?.CriticalText
            };
        }

        private static List<DashboardTileConfig> CloneTileConfigList(List<DashboardTileConfig> source)
        {
            var result = new List<DashboardTileConfig>();
            if (source == null)
            {
                return result;
            }

            foreach (var tile in source)
            {
                var cloned = CloneTileConfig(tile);
                if (cloned != null)
                {
                    result.Add(cloned);
                }
            }

            return result;
        }

        private static DashboardLayoutConfig CloneLayoutConfig(DashboardLayoutConfig source)
        {
            if (source == null)
            {
                return null;
            }

            return new DashboardLayoutConfig
            {
                ColumnsHint = source.ColumnsHint,
                TileWidth = source.TileWidth,
                TileHeight = source.TileHeight
            };
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

        private sealed class ExplorerWindowForm : Form
        {
            private const string CurrentStateMessageName = "CURRENT_STATE";

            private sealed class FieldEntry
            {
                public string FieldName;
                public string DisplayName;
                public string OverrideLabel;

                public override string ToString()
                {
                    if (ShouldShowOverrideLabel())
                    {
                        // Show configured tile label override alongside the base field label.
                        return DisplayName + " [" + OverrideLabel + "]";
                    }

                    return DisplayName;
                }

                private bool ShouldShowOverrideLabel()
                {
                    if (string.IsNullOrWhiteSpace(OverrideLabel))
                    {
                        return false;
                    }

                    var overrideText = OverrideLabel.Trim();
                    return !string.Equals(overrideText, (DisplayName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(overrideText, (FieldName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }

            private readonly Label searchLabel = new Label();
            private readonly Label searchExampleLabel = new Label();
            private readonly TextBox searchTextBox = new TextBox();
            private readonly CheckBox showAllFieldTypesCheckBox = new CheckBox();
            private readonly CheckedListBox fieldList = new CheckedListBox();
            private readonly List<FieldEntry> allFields = new List<FieldEntry>();
            private readonly List<FieldEntry> visibleFields = new List<FieldEntry>();
            private readonly Func<FieldKey, bool> isFieldTileSelected;
            private readonly Action<FieldKey, bool> fieldCheckedChanged;
            private readonly Func<FieldKey, string> getFieldLabelOverride;
            private bool suppressFieldListEvents;
            private int lastCustomFieldNameCount = -1;
            private bool hasCurrentStateDisplayNames;
            private bool showAllFieldTypes;

            public ExplorerWindowForm(Func<FieldKey, bool> isFieldTileSelected, Action<FieldKey, bool> fieldCheckedChanged,
                Func<FieldKey, string> getFieldLabelOverride)
            {
                this.isFieldTileSelected = isFieldTileSelected;
                this.fieldCheckedChanged = fieldCheckedChanged;
                this.getFieldLabelOverride = getFieldLabelOverride;

                Text = "Dashboard Fields";
                Name = "mavlinkExplorerWindow";
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(280, 360);
                Size = new Size(320, 540);

                var rootPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(8)
                };
                var headerPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 66
                };
                var searchHeaderRow = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 16,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };
                var searchExampleText = "* wildcard (e.g. esc*_temp)";
                var searchExampleFont = new Font(Font.FontFamily, 7F, FontStyle.Regular, GraphicsUnit.Point);
                var searchExampleWidth = Math.Max(120, TextRenderer.MeasureText(searchExampleText, searchExampleFont).Width + 6);
                searchHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
                searchHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, searchExampleWidth));
                searchHeaderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                searchLabel.Name = "explorerSearchLabel";
                searchLabel.Text = "Search";
                searchLabel.Dock = DockStyle.Fill;
                searchLabel.TextAlign = ContentAlignment.BottomLeft;

                searchExampleLabel.Name = "explorerSearchExampleLabel";
                searchExampleLabel.Text = searchExampleText;
                searchExampleLabel.Dock = DockStyle.Fill;
                searchExampleLabel.Font = searchExampleFont;
                searchExampleLabel.TextAlign = ContentAlignment.BottomRight;

                searchTextBox.Name = "explorerSearchTextBox";
                searchTextBox.Dock = DockStyle.Top;
                searchTextBox.Height = 22;
                searchTextBox.TextChanged += searchTextBox_TextChanged;
                searchTextBox.KeyDown += searchTextBox_KeyDown;

                showAllFieldTypesCheckBox.Name = "explorerShowAllFieldTypesCheckBox";
                showAllFieldTypesCheckBox.Text = "Show all field types (strings included)";
                showAllFieldTypesCheckBox.Dock = DockStyle.Top;
                showAllFieldTypesCheckBox.Height = 20;
                showAllFieldTypesCheckBox.Checked = false;
                showAllFieldTypesCheckBox.CheckedChanged += showAllFieldTypesCheckBox_CheckedChanged;

                fieldList.Name = "explorerFieldList";
                fieldList.Dock = DockStyle.Fill;
                fieldList.CheckOnClick = true;
                fieldList.IntegralHeight = false;
                fieldList.ItemCheck += fieldList_ItemCheck;
                fieldList.KeyDown += fieldList_KeyDown;

                // Default explorer list is numeric/bool; optional toggle exposes all field types.
                BuildFieldCatalog(MainV2.comPort?.MAV?.cs);
                ApplyFilterAndRebind();

                searchHeaderRow.Controls.Add(searchLabel, 0, 0);
                searchHeaderRow.Controls.Add(searchExampleLabel, 1, 0);
                headerPanel.Controls.Add(showAllFieldTypesCheckBox);
                headerPanel.Controls.Add(searchTextBox);
                headerPanel.Controls.Add(searchHeaderRow);
                rootPanel.Controls.Add(fieldList);
                rootPanel.Controls.Add(headerPanel);
                Controls.Add(rootPanel);
            }

            public void RefreshFieldSelectionState()
            {
                UpdateOverrideLabelsForVisibleFields();
                suppressFieldListEvents = true;
                try
                {
                    for (var i = 0; i < visibleFields.Count && i < fieldList.Items.Count; i++)
                    {
                        var fieldKey = CreateCurrentStateFieldKey(visibleFields[i].FieldName);
                        var shouldBeChecked = isFieldTileSelected != null && isFieldTileSelected(fieldKey);
                        if (fieldList.GetItemChecked(i) != shouldBeChecked)
                        {
                            fieldList.SetItemChecked(i, shouldBeChecked);
                        }
                    }
                }
                finally
                {
                    suppressFieldListEvents = false;
                }
            }

            public void UpdateCurrentStatePreviewValues(CurrentState currentState)
            {
                if (currentState == null)
                {
                    return;
                }

                // Rebuild when CurrentState descriptions become available or custom labels change.
                var customFieldCount = GetCustomFieldNameCount();
                if (!hasCurrentStateDisplayNames || customFieldCount != lastCustomFieldNameCount)
                {
                    BuildFieldCatalog(currentState);
                    ApplyFilterAndRebind();
                    return;
                }

                RefreshFieldSelectionState();
            }

            private void searchTextBox_TextChanged(object sender, EventArgs e)
            {
                ApplyFilterAndRebind();
            }

            private void searchTextBox_KeyDown(object sender, KeyEventArgs e)
            {
                if (!e.Control || e.KeyCode != Keys.Back)
                {
                    return;
                }

                searchTextBox.Clear();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            private void showAllFieldTypesCheckBox_CheckedChanged(object sender, EventArgs e)
            {
                showAllFieldTypes = showAllFieldTypesCheckBox.Checked;
                BuildFieldCatalog(MainV2.comPort?.MAV?.cs);
                ApplyFilterAndRebind();
            }

            private void fieldList_ItemCheck(object sender, ItemCheckEventArgs e)
            {
                if (suppressFieldListEvents || e.Index < 0 || e.Index >= visibleFields.Count)
                {
                    return;
                }

                // Explorer checkboxes are the single source of truth for showing/hiding dashboard tiles.
                var entry = visibleFields[e.Index];
                var fieldKey = CreateCurrentStateFieldKey(entry.FieldName);
                var isChecked = e.NewValue == CheckState.Checked;
                BeginInvoke((Action) (() => fieldCheckedChanged?.Invoke(fieldKey, isChecked)));
            }

            private void fieldList_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Back)
                {
                    searchTextBox.Focus();
                    searchTextBox.Clear();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (e.KeyCode == Keys.Back)
                {
                    ForwardBackspaceToSearch(e);
                    return;
                }

                var character = TryGetSearchCharacter(e);
                if (character == '\0')
                {
                    return;
                }

                // Typing in the list always routes to search instead of toggling checked state.
                ForwardCharacterToSearch(character, e);
            }

            private void ForwardBackspaceToSearch(KeyEventArgs e)
            {
                searchTextBox.Focus();

                if (searchTextBox.TextLength > 0)
                {
                    searchTextBox.Text = searchTextBox.Text.Substring(0, searchTextBox.TextLength - 1);
                }

                searchTextBox.SelectionStart = searchTextBox.TextLength;
                searchTextBox.SelectionLength = 0;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            private void ForwardCharacterToSearch(char character, KeyEventArgs e)
            {
                searchTextBox.Focus();
                searchTextBox.Text = (searchTextBox.Text ?? string.Empty) + character;
                searchTextBox.SelectionStart = searchTextBox.TextLength;
                searchTextBox.SelectionLength = 0;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }

            private static char TryGetSearchCharacter(KeyEventArgs e)
            {
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                {
                    var c = (char) ('A' + (e.KeyCode - Keys.A));
                    return e.Shift ? c : char.ToLowerInvariant(c);
                }

                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9 && !e.Shift)
                {
                    return (char) ('0' + (e.KeyCode - Keys.D0));
                }

                if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
                {
                    return (char) ('0' + (e.KeyCode - Keys.NumPad0));
                }

                if (e.KeyCode == Keys.Space)
                {
                    return ' ';
                }

                return '\0';
            }

            private void BuildFieldCatalog(CurrentState currentState)
            {
                allFields.Clear();

                foreach (var property in typeof(CurrentState).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    if (!showAllFieldTypes &&
                        !IsNumericPropertyType(propertyType) &&
                        propertyType != typeof(bool))
                    {
                        continue;
                    }

                    var displayName = currentState != null ? currentState.GetFieldDesc(property.Name) : property.Name;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = property.Name;
                    }

                    allFields.Add(new FieldEntry
                    {
                        FieldName = property.Name,
                        DisplayName = displayName
                    });
                }

                allFields.Sort((left, right) => CurrentState.StringCompareTo(left.DisplayName, right.DisplayName));
                lastCustomFieldNameCount = GetCustomFieldNameCount();
                hasCurrentStateDisplayNames = currentState != null;
            }

            private void ApplyFilterAndRebind()
            {
                var query = (searchTextBox.Text ?? string.Empty).Trim();
                UpdateOverrideLabelsForAllFields();

                visibleFields.Clear();
                foreach (var entry in allFields)
                {
                    if (!string.IsNullOrWhiteSpace(query) &&
                        !MatchesSearchQuery(entry.DisplayName, query) &&
                        !MatchesSearchQuery(entry.FieldName, query) &&
                        !MatchesSearchQuery(entry.OverrideLabel, query))
                    {
                        continue;
                    }

                    visibleFields.Add(entry);
                }

                suppressFieldListEvents = true;
                try
                {
                    fieldList.BeginUpdate();
                    fieldList.Items.Clear();

                    foreach (var entry in visibleFields)
                    {
                        fieldList.Items.Add(entry);
                    }

                    fieldList.ClearSelected();
                    fieldList.EndUpdate();
                    RefreshFieldSelectionState();
                }
                finally
                {
                    suppressFieldListEvents = false;
                }
            }

            private static bool MatchesSearchQuery(string candidate, string query)
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(candidate))
                {
                    return false;
                }

                if (query.IndexOf('*') < 0)
                {
                    return candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                // Wildcard search uses ordered segment matching so "battery*1" matches any text containing
                // "battery" followed by "1" with any characters between them.
                var segments = query.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    return true;
                }

                var searchStart = 0;
                foreach (var segment in segments)
                {
                    var segmentIndex = candidate.IndexOf(segment, searchStart, StringComparison.OrdinalIgnoreCase);
                    if (segmentIndex < 0)
                    {
                        return false;
                    }

                    searchStart = segmentIndex + segment.Length;
                }

                return true;
            }

            private void UpdateOverrideLabelsForAllFields()
            {
                foreach (var entry in allFields)
                {
                    entry.OverrideLabel = GetOverrideLabel(entry.FieldName);
                }
            }

            private void UpdateOverrideLabelsForVisibleFields()
            {
                var labelsUpdated = false;
                foreach (var entry in visibleFields)
                {
                    var current = entry.OverrideLabel;
                    var updated = GetOverrideLabel(entry.FieldName);
                    if (!string.Equals(current, updated, StringComparison.Ordinal))
                    {
                        entry.OverrideLabel = updated;
                        labelsUpdated = true;
                    }
                }

                if (labelsUpdated)
                {
                    fieldList.Refresh();
                }
            }

            private string GetOverrideLabel(string fieldName)
            {
                if (getFieldLabelOverride == null)
                {
                    return null;
                }

                var fieldKey = CreateCurrentStateFieldKey(fieldName);
                var label = getFieldLabelOverride(fieldKey);
                return string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            }

            private static int GetCustomFieldNameCount()
            {
                try
                {
                    return CurrentState.custom_field_names?.Count ?? 0;
                }
                catch
                {
                    return 0;
                }
            }

            private static FieldKey CreateCurrentStateFieldKey(string fieldName)
            {
                // CURRENT_STATE is the canonical namespace for dashboard fields sourced from CurrentState.
                return new FieldKey
                {
                    Message = CurrentStateMessageName,
                    Field = fieldName
                };
            }

            private static bool IsNumericPropertyType(Type propertyType)
            {
                switch (Type.GetTypeCode(propertyType))
                {
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Decimal:
                    case TypeCode.Double:
                    case TypeCode.Single:
                        return true;
                    default:
                        return false;
                }
            }
        }

    }
}
