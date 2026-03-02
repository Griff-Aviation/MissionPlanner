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
        private bool editMode;
        private readonly IFieldValueSource fieldValueSource = new CurrentStateFieldValueSource();
        // Keep config metadata and runtime control together so tile order/state stays in sync.
        private readonly List<(DashboardTileConfig Config, TelemetryTileControl Tile)> tiles = new List<(DashboardTileConfig Config, TelemetryTileControl Tile)>();
        private readonly Button buttonResetDefaults = new Button();
        private readonly Button buttonEditDashboard = new Button();
        private readonly ContextMenuStrip tileContextMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem removeTileMenuItem = new ToolStripMenuItem("Remove");
        // Visual edit-mode cue rendered above all children without affecting layout metrics.
        private readonly EditModeOverlayControl editModeOverlay = new EditModeOverlayControl();
        private readonly Button buttonExplorer = new Button();
        private ExplorerWindowForm explorerWindow;
        private DashboardConfig dashboardConfig;
        private TelemetryTileControl draggingTile;
        private Point dragStartPointScreen;
        private TelemetryTileControl contextMenuTargetTile;
        private TelemetryTileControl currentDropTargetTile;

        public MavlinkDashboardView()
        {
            InitializeComponent();
            InitializeResetButton();
            InitializeEditButton();
            InitializeExplorerButton();
            InitializeTileContextMenu();
            InitializeEditModeSupport();
            InitializeEditModeOverlay();
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

        private void buttonExplorer_Click(object sender, EventArgs e)
        {
            ShowExplorerWindow();
        }

        private void uiTickTimer_Tick(object sender, EventArgs e)
        {
            // Keep tile values current from the live telemetry source.
            RefreshTiles();
            RefreshExplorerMessageList();
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
                    // UI overrides are applied after source formatting.
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
            // Rebuild panel from config order so startup and reset are deterministic.
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

            WireTileInteractions(tile);
            tiles.Add((tileConfig, tile));
            flowLayoutPanelTiles.Controls.Add(tile);
            UpdateTileInteractionState();
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

        private void InitializeEditButton()
        {
            buttonEditDashboard.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonEditDashboard.Location = new Point(66, 6);
            buttonEditDashboard.Name = "buttonEditDashboard";
            buttonEditDashboard.Size = new Size(95, 23);
            buttonEditDashboard.TabIndex = 2;
            buttonEditDashboard.Text = "Edit Dashboard";
            buttonEditDashboard.UseVisualStyleBackColor = true;
            buttonEditDashboard.Click += buttonEditDashboard_Click;
            panelTop.Controls.Add(buttonEditDashboard);
        }

        private void InitializeExplorerButton()
        {
            // Explorer is always available and opens in a separate modeless window.
            buttonExplorer.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            buttonExplorer.Location = new Point(164, 6);
            buttonExplorer.Name = "buttonExplorer";
            buttonExplorer.Size = new Size(70, 23);
            buttonExplorer.TabIndex = 3;
            buttonExplorer.Text = "Explorer";
            buttonExplorer.UseVisualStyleBackColor = true;
            buttonExplorer.Click += buttonExplorer_Click;
            panelTop.Controls.Add(buttonExplorer);
        }

        private void InitializeTileContextMenu()
        {
            removeTileMenuItem.Name = "removeTileMenuItem";
            removeTileMenuItem.Click += removeTileMenuItem_Click;
            tileContextMenu.Items.Add(removeTileMenuItem);
            tileContextMenu.Opening += tileContextMenu_Opening;
        }

        private void InitializeEditModeSupport()
        {
            // Reordering works through the panel-level drag/drop surface.
            flowLayoutPanelTiles.AllowDrop = true;
            flowLayoutPanelTiles.DragEnter += flowLayoutPanelTiles_DragEnter;
            flowLayoutPanelTiles.DragOver += flowLayoutPanelTiles_DragOver;
            flowLayoutPanelTiles.DragLeave += flowLayoutPanelTiles_DragLeave;
            flowLayoutPanelTiles.DragDrop += flowLayoutPanelTiles_DragDrop;
        }

        private void InitializeEditModeOverlay()
        {
            editModeOverlay.Dock = DockStyle.Fill;
            editModeOverlay.Visible = false;
            editModeOverlay.TabStop = false;
            Controls.Add(editModeOverlay);
            // Must stay on top so the border is not hidden by docked child controls.
            editModeOverlay.BringToFront();
        }

        private void ShowExplorerWindow()
        {
            if (explorerWindow == null || explorerWindow.IsDisposed)
            {
                explorerWindow = new ExplorerWindowForm();
                MissionPlanner.Utilities.ThemeManager.ApplyThemeTo(explorerWindow);
            }

            if (!explorerWindow.Visible)
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
                explorerWindow.BringToFront();
            }

            RefreshExplorerMessageList();
        }

        private void RefreshExplorerMessageList()
        {
            if (explorerWindow == null || explorerWindow.IsDisposed || !explorerWindow.Visible)
            {
                return;
            }

            var currentMav = MainV2.comPort?.MAV;
            if (currentMav == null || currentMav.packetspersecondbuild == null)
            {
                return;
            }

            var recentMessageIds = new List<uint>();
            var cutoff = DateTime.UtcNow.AddSeconds(-5);

            try
            {
                // Use recent packet timestamps so the explorer shows only active traffic.
                foreach (var packet in currentMav.packetspersecondbuild)
                {
                    if (packet.Value >= cutoff)
                    {
                        recentMessageIds.Add(packet.Key);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Dictionary can mutate while telemetry threads update it; skip this UI tick.
                return;
            }

            explorerWindow.UpdateReceivedMessages(recentMessageIds);
            // preview updates are throttled by the existing 150ms UI timer.
            explorerWindow.UpdateFieldPreviewValues(currentMav);
        }

        private void buttonResetDefaults_Click(object sender, EventArgs e)
        {
            dashboardConfig = CreateDefaultConfig();
            ApplyDashboardConfig(dashboardConfig);
            SaveDashboardConfig();
            RefreshTiles();
        }

        private void buttonEditDashboard_Click(object sender, EventArgs e)
        {
            SetEditMode(!editMode);
        }

        private void SetEditMode(bool enabled)
        {
            if (editMode == enabled)
            {
                return;
            }

            editMode = enabled;
            buttonEditDashboard.Text = enabled ? "Done Editing" : "Edit Dashboard";
            UpdateTileInteractionState();
            // Toggle the top-most border overlay with edit mode.
            editModeOverlay.Visible = enabled;

            if (enabled)
            {
                // Ensure re-ordering/resize repaint does not bury the edit overlay.
                editModeOverlay.BringToFront();
                editModeOverlay.Invalidate();
            }

            if (!enabled)
            {
                // Persist once when editing session ends.
                ClearDropTargetHighlight();
                SaveDashboardConfig();
            }
        }

        private void UpdateTileInteractionState()
        {
            foreach (var tile in tiles)
            {
                tile.Tile.Cursor = editMode ? Cursors.SizeAll : Cursors.Default;
            }
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

            foreach (Control child in control.Controls)
            {
                WireTileControl(child, ownerTile);
            }
        }

        private void TileSurface_MouseDown(TelemetryTileControl tile, MouseEventArgs e)
        {
            if (!editMode || e.Button != MouseButtons.Left)
            {
                return;
            }

            // Capture drag origin; MouseMove starts actual drag after threshold.
            draggingTile = tile;
            dragStartPointScreen = Cursor.Position;
        }

        private void TileSurface_MouseMove(TelemetryTileControl tile, MouseEventArgs e)
        {
            if (!editMode || draggingTile != tile)
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
            e.Effect = editMode && e.Data.GetDataPresent(typeof(TelemetryTileControl))
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }

        private void flowLayoutPanelTiles_DragOver(object sender, DragEventArgs e)
        {
            // Keep WinForms drop effect updated and drive live hover highlighting while dragging.
            e.Effect = editMode && e.Data.GetDataPresent(typeof(TelemetryTileControl))
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

            if (!editMode || !e.Data.GetDataPresent(typeof(TelemetryTileControl)))
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
            if (!editMode)
            {
                e.Cancel = true;
                return;
            }

            // Resolve the owning tile from whichever inner child was right-clicked.
            contextMenuTargetTile = GetTileFromControl(tileContextMenu.SourceControl);
            removeTileMenuItem.Enabled = contextMenuTargetTile != null;
        }

        private void removeTileMenuItem_Click(object sender, EventArgs e)
        {
            if (!editMode || contextMenuTargetTile == null)
            {
                return;
            }

            var index = GetTileIndex(contextMenuTargetTile);
            if (index < 0)
            {
                return;
            }

            // Dispose removed control immediately; config is persisted when edit mode exits.
            flowLayoutPanelTiles.Controls.Remove(contextMenuTargetTile);
            contextMenuTargetTile.Dispose();
            tiles.RemoveAt(index);
            contextMenuTargetTile = null;
            RefreshTiles();
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

        private void MavlinkDashboardView_Disposed(object sender, EventArgs e)
        {
            if (explorerWindow != null && !explorerWindow.IsDisposed)
            {
                explorerWindow.Close();
                explorerWindow.Dispose();
                explorerWindow = null;
            }

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
                // Persist a copy, not live references, to keep runtime objects decoupled from saved state.
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

        private sealed class ExplorerWindowForm : Form
        {
            private sealed class FieldNodeTag
            {
                public uint MessageId;
                public int? InstanceId;
                public FieldInfo FieldInfo;
                public string FieldName;
                public string LastPreviewText;
            }

            private sealed class InstanceNodeTag
            {
                public uint MessageId;
                public int InstanceId;
            }

            private sealed class PlaceholderNodeTag
            {
            }

            private sealed class StatusNodeTag
            {
            }

            private readonly TreeView treeView = new TreeView();
            private readonly TextBox searchTextBox = new TextBox();
            private readonly CheckBox showAllMessagesCheckBox = new CheckBox();
            private readonly List<uint> allDialectMessageIds = new List<uint>();
            private readonly HashSet<uint> currentlyReceivedMessageIds = new HashSet<uint>();
            private readonly Dictionary<uint, FieldInfo[]> messageFieldsById = new Dictionary<uint, FieldInfo[]>();
            // Top-level tree nodes keyed by MAVLink message id for incremental add/remove/update.
            private readonly Dictionary<uint, TreeNode> visibleMessageNodes = new Dictionary<uint, TreeNode>();
            // Latest BATTERY_STATUS payload cache by battery id (0/1).
            private readonly Dictionary<int, object> latestBatteryPayloadById = new Dictionary<int, object>();
            private static readonly uint BatteryStatusMessageId = (uint) MAVLink.MAVLINK_MSG_ID.BATTERY_STATUS;

            public ExplorerWindowForm()
            {
                Text = "MAVLink Explorer";
                Name = "mavlinkExplorerWindow";
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(280, 360);
                Size = new Size(320, 540);

                var rootPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(8)
                };

                var searchLabel = new Label
                {
                    Name = "explorerSearchLabel",
                    Text = "Search",
                    Dock = DockStyle.Top,
                    Height = 16,
                    TextAlign = ContentAlignment.BottomLeft
                };

                searchTextBox.Name = "explorerSearchTextBox";
                searchTextBox.Dock = DockStyle.Top;
                searchTextBox.Height = 22;
                searchTextBox.TextChanged += searchTextBox_TextChanged;

                showAllMessagesCheckBox.Name = "showAllMessagesCheckBox";
                showAllMessagesCheckBox.Text = "Show All Messages";
                showAllMessagesCheckBox.Dock = DockStyle.Top;
                showAllMessagesCheckBox.Height = 22;
                showAllMessagesCheckBox.CheckedChanged += showAllMessagesCheckBox_CheckedChanged;

                var headerPanel = new Panel
                {
                    Name = "explorerHeaderPanel",
                    Dock = DockStyle.Top,
                    Height = 70
                };

                headerPanel.Controls.Add(showAllMessagesCheckBox);
                headerPanel.Controls.Add(searchTextBox);
                headerPanel.Controls.Add(searchLabel);

                treeView.Name = "explorerTreeView";
                treeView.Dock = DockStyle.Fill;
                treeView.HideSelection = false;
                treeView.BeforeExpand += treeView_BeforeExpand;
                // Initial status until live MAVLink traffic is observed.
                treeView.Nodes.Add(new TreeNode("Waiting for MAVLink messages...")
                {
                    Tag = new StatusNodeTag()
                });

                BuildDialectMessageIndex();
                rootPanel.Controls.Add(treeView);
                rootPanel.Controls.Add(headerPanel);
                Controls.Add(rootPanel);
            }

            public void UpdateReceivedMessages(IEnumerable<uint> receivedMessageIds)
            {
                if (receivedMessageIds == null)
                {
                    return;
                }

                // Snapshot current tick's IDs so set comparisons stay deterministic.
                var nextReceivedMessageIds = new HashSet<uint>(receivedMessageIds);
                var receivedSetChanged = !nextReceivedMessageIds.SetEquals(currentlyReceivedMessageIds);
                var addedDialectMessage = false;

                if (receivedSetChanged)
                {
                    currentlyReceivedMessageIds.Clear();
                    currentlyReceivedMessageIds.UnionWith(nextReceivedMessageIds);
                }

                // Include runtime-only IDs if they appear, while retaining sorted display order.
                foreach (var messageId in nextReceivedMessageIds)
                {
                    if (allDialectMessageIds.Contains(messageId))
                    {
                        continue;
                    }

                    allDialectMessageIds.Add(messageId);
                    addedDialectMessage = true;
                }

                if (addedDialectMessage)
                {
                    allDialectMessageIds.Sort();
                }

                // No data/catalog changes: keep current tree and scrollbar position.
                if (!receivedSetChanged && !addedDialectMessage)
                {
                    return;
                }

                ReconcileMessageTree();
            }

            private void BuildDialectMessageIndex()
            {
                // Seed with known MAVLink enum IDs so show-all can render before traffic arrives.
                var seen = new HashSet<uint>();
                foreach (int value in Enum.GetValues(typeof(MAVLink.MAVLINK_MSG_ID)))
                {
                    var messageId = (uint) value;
                    if (seen.Add(messageId))
                    {
                        allDialectMessageIds.Add(messageId);
                    }
                }

                allDialectMessageIds.Sort();
            }

            private void showAllMessagesCheckBox_CheckedChanged(object sender, EventArgs e)
            {
                ReconcileMessageTree();
            }

            private void searchTextBox_TextChanged(object sender, EventArgs e)
            {
                ReconcileMessageTree();
            }

            private void ReconcileMessageTree()
            {
                // Reconcile visible message rows incrementally to avoid collapsing expanded branches.
                var showAll = showAllMessagesCheckBox.Checked;
                var query = (searchTextBox.Text ?? string.Empty).Trim();
                var hasQuery = query.Length > 0;
                var desiredMessageIds = new List<uint>();
                var desiredMessageSet = new HashSet<uint>();

                foreach (var messageId in allDialectMessageIds)
                {
                    var isReceived = currentlyReceivedMessageIds.Contains(messageId);
                    if (!showAll && !isReceived)
                    {
                        continue;
                    }

                    var messageName = ResolveMessageName(messageId);
                    if (hasQuery && messageName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    desiredMessageIds.Add(messageId);
                    desiredMessageSet.Add(messageId);
                }

                treeView.BeginUpdate();
                RemoveStatusNode();

                var messageIdsToRemove = new List<uint>();
                foreach (var pair in visibleMessageNodes)
                {
                    if (!desiredMessageSet.Contains(pair.Key))
                    {
                        treeView.Nodes.Remove(pair.Value);
                        messageIdsToRemove.Add(pair.Key);
                    }
                }

                foreach (var messageId in messageIdsToRemove)
                {
                    visibleMessageNodes.Remove(messageId);
                }

                foreach (var messageId in desiredMessageIds)
                {
                    var isReceived = currentlyReceivedMessageIds.Contains(messageId);
                    var node = GetOrCreateVisibleMessageNode(messageId);
                    ApplyMessageNodeColor(node, showAll, isReceived);
                }

                if (visibleMessageNodes.Count == 0)
                {
                    treeView.Nodes.Add(new TreeNode(showAll || hasQuery
                        ? "No messages match current filters."
                        : "Waiting for MAVLink messages...")
                    {
                        Tag = new StatusNodeTag()
                    });
                }

                treeView.EndUpdate();
            }

            private TreeNode GetOrCreateVisibleMessageNode(uint messageId)
            {
                if (visibleMessageNodes.TryGetValue(messageId, out var existingNode))
                {
                    return existingNode;
                }

                var createdNode = new TreeNode(ResolveMessageName(messageId))
                {
                    Name = "msg_" + messageId,
                    Tag = messageId
                };

                if (GetMessageFields(messageId).Length > 0)
                {
                    // Add a lightweight child so message rows show an expander before lazy field population.
                    createdNode.Nodes.Add(new TreeNode { Tag = new PlaceholderNodeTag() });
                }

                var insertIndex = GetInsertIndex(messageId);
                treeView.Nodes.Insert(insertIndex, createdNode);
                visibleMessageNodes[messageId] = createdNode;
                return createdNode;
            }

            private int GetInsertIndex(uint messageId)
            {
                var index = 0;
                foreach (TreeNode node in treeView.Nodes)
                {
                    if (node.Tag is uint existingMessageId && existingMessageId > messageId)
                    {
                        break;
                    }

                    index++;
                }

                return index;
            }

            private void RemoveStatusNode()
            {
                TreeNode statusNode = null;
                foreach (TreeNode node in treeView.Nodes)
                {
                    if (node.Tag is StatusNodeTag)
                    {
                        statusNode = node;
                        break;
                    }
                }

                if (statusNode != null)
                {
                    treeView.Nodes.Remove(statusNode);
                }
            }

            private void ApplyMessageNodeColor(TreeNode node, bool showAll, bool isReceived)
            {
                node.ForeColor = (showAll && !isReceived)
                    ? SystemColors.GrayText
                    : treeView.ForeColor;
            }

            public void UpdateFieldPreviewValues(MAVState mavState)
            {
                if (mavState == null || !treeView.Visible)
                {
                    return;
                }

                CaptureLatestBatteryPayloads(mavState);

                // Only expanded + visible branches are refreshed to keep UI work bounded.
                foreach (TreeNode messageNode in treeView.Nodes)
                {
                    if (!(messageNode.Tag is uint messageId) || !messageNode.IsExpanded || !IsNodeVisible(messageNode))
                    {
                        continue;
                    }

                    if (messageId == BatteryStatusMessageId)
                    {
                        EnsureBatteryInstanceNodes(messageNode);
                        foreach (TreeNode instanceNode in messageNode.Nodes)
                        {
                            if (!(instanceNode.Tag is InstanceNodeTag instanceTag) || !instanceNode.IsExpanded || !IsNodeVisible(instanceNode))
                            {
                                continue;
                            }

                            latestBatteryPayloadById.TryGetValue(instanceTag.InstanceId, out var batteryPacketData);
                            UpdateFieldPreviewForNode(instanceNode, messageId, instanceTag.InstanceId, batteryPacketData);
                        }

                        continue;
                    }

                    var packet = mavState.getPacketLast(messageId);
                    UpdateFieldPreviewForNode(messageNode, messageId, null, packet?.data);
                }
            }

            private void UpdateFieldPreviewForNode(TreeNode parentNode, uint messageId, int? instanceId, object packetData)
            {
                EnsureFieldNodes(parentNode, messageId, instanceId);

                foreach (TreeNode fieldNode in parentNode.Nodes)
                {
                    if (!(fieldNode.Tag is FieldNodeTag fieldTag) || !IsNodeVisible(fieldNode))
                    {
                        continue;
                    }

                    var previewValue = "--";
                    if (packetData != null)
                    {
                        previewValue = FormatFieldPreviewValue(fieldTag.FieldInfo.GetValue(packetData));
                    }

                    if (string.Equals(fieldTag.LastPreviewText, previewValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    fieldTag.LastPreviewText = previewValue;
                    fieldNode.Text = fieldTag.FieldName + " = " + previewValue;
                }
            }

            private void CaptureLatestBatteryPayloads(MAVState mavState)
            {
                // Cache the latest BATTERY_STATUS payload for battery id 0 and 1 only.
                if (!currentlyReceivedMessageIds.Contains(BatteryStatusMessageId))
                {
                    return;
                }

                var packet = mavState.getPacketLast(BatteryStatusMessageId);
                var packetData = packet?.data;
                if (packetData == null)
                {
                    return;
                }

                if (TryGetBatteryInstanceId(packetData, out var batteryId))
                {
                    latestBatteryPayloadById[batteryId] = packetData;
                }
            }

            private static string ResolveMessageName(uint messageId)
            {
                return Enum.IsDefined(typeof(MAVLink.MAVLINK_MSG_ID), (int) messageId)
                    ? ((MAVLink.MAVLINK_MSG_ID) messageId).ToString()
                    : "UNKNOWN";
            }

            private void treeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
            {
                if (e.Node?.Tag is uint messageId)
                {
                    if (messageId == BatteryStatusMessageId)
                    {
                        // BATTERY_STATUS expands to Battery 1 / Battery 2 nodes.
                        EnsureBatteryInstanceNodes(e.Node);
                    }
                    else
                    {
                        // Single-instance messages expand directly to fields.
                        EnsureFieldNodes(e.Node, messageId, null);
                    }

                    return;
                }

                if (!(e.Node?.Tag is InstanceNodeTag instanceTag))
                {
                    return;
                }

                EnsureFieldNodes(e.Node, instanceTag.MessageId, instanceTag.InstanceId);
            }

            private void EnsureBatteryInstanceNodes(TreeNode messageNode)
            {
                if (messageNode == null)
                {
                    return;
                }

                // Keep battery instance rows stable while enforcing exactly Battery 1 (id 0) and Battery 2 (id 1).
                var desiredInstanceIds = new[] { 0, 1 };
                var existingInstanceNodes = new Dictionary<int, TreeNode>();
                var replaceChildren = false;

                foreach (TreeNode childNode in messageNode.Nodes)
                {
                    if (childNode.Tag is PlaceholderNodeTag)
                    {
                        continue;
                    }

                    if (childNode.Tag is InstanceNodeTag instanceTag && instanceTag.MessageId == BatteryStatusMessageId)
                    {
                        existingInstanceNodes[instanceTag.InstanceId] = childNode;
                        continue;
                    }

                    replaceChildren = true;
                    break;
                }

                if (replaceChildren || (messageNode.Nodes.Count == 1 && messageNode.Nodes[0].Tag is PlaceholderNodeTag))
                {
                    messageNode.Nodes.Clear();
                    existingInstanceNodes.Clear();
                }

                var desiredInstanceSet = new HashSet<int>(desiredInstanceIds);
                var instanceIdsToRemove = new List<int>();
                foreach (var pair in existingInstanceNodes)
                {
                    if (desiredInstanceSet.Contains(pair.Key))
                    {
                        continue;
                    }

                    messageNode.Nodes.Remove(pair.Value);
                    instanceIdsToRemove.Add(pair.Key);
                }

                foreach (var instanceId in instanceIdsToRemove)
                {
                    existingInstanceNodes.Remove(instanceId);
                }

                for (var index = 0; index < desiredInstanceIds.Length; index++)
                {
                    var instanceId = desiredInstanceIds[index];
                    if (!existingInstanceNodes.TryGetValue(instanceId, out var instanceNode))
                    {
                        instanceNode = new TreeNode(BuildBatteryInstanceLabel(instanceId))
                        {
                            Tag = new InstanceNodeTag
                            {
                                MessageId = BatteryStatusMessageId,
                                InstanceId = instanceId
                            }
                        };

                        if (GetMessageFields(BatteryStatusMessageId).Length > 0)
                        {
                            instanceNode.Nodes.Add(new TreeNode { Tag = new PlaceholderNodeTag() });
                        }

                        messageNode.Nodes.Insert(index, instanceNode);
                        existingInstanceNodes[instanceId] = instanceNode;
                        continue;
                    }

                    instanceNode.Text = BuildBatteryInstanceLabel(instanceId);
                    var currentIndex = messageNode.Nodes.IndexOf(instanceNode);
                    if (currentIndex != index)
                    {
                        messageNode.Nodes.Remove(instanceNode);
                        messageNode.Nodes.Insert(index, instanceNode);
                    }
                }
            }

            private void EnsureFieldNodes(TreeNode parentNode, uint messageId, int? instanceId)
            {
                if (parentNode == null)
                {
                    return;
                }

                if (parentNode.Nodes.Count > 0)
                {
                    var firstTag = parentNode.Nodes[0].Tag;
                    if (firstTag is FieldNodeTag firstFieldTag &&
                        firstFieldTag.MessageId == messageId &&
                        firstFieldTag.InstanceId == instanceId)
                    {
                        return;
                    }

                    if (!(firstTag is PlaceholderNodeTag))
                    {
                        parentNode.Nodes.Clear();
                    }
                    else
                    {
                        parentNode.Nodes.Clear();
                    }
                }

                foreach (var fieldInfo in GetMessageFields(messageId))
                {
                    var fieldName = fieldInfo.Name;
                    parentNode.Nodes.Add(new TreeNode(fieldName + " = --")
                    {
                        Tag = new FieldNodeTag
                        {
                            MessageId = messageId,
                            InstanceId = instanceId,
                            FieldInfo = fieldInfo,
                            FieldName = fieldName,
                            LastPreviewText = "--"
                        }
                    });
                }
            }

            private bool TryGetBatteryInstanceId(object packetData, out int batteryId)
            {
                batteryId = 0;
                if (packetData == null)
                {
                    return false;
                }

                var packetType = packetData.GetType();
                var instanceField = packetType.GetField("id", BindingFlags.Instance | BindingFlags.Public);
                if (instanceField == null)
                {
                    return false;
                }

                if (!TryConvertInstanceValue(instanceField.GetValue(packetData), out batteryId))
                {
                    return false;
                }

                return batteryId == 0 || batteryId == 1;
            }

            private static bool TryConvertInstanceValue(object value, out int result)
            {
                result = 0;
                if (value == null)
                {
                    return false;
                }

                try
                {
                    result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static string BuildBatteryInstanceLabel(int instanceId)
            {
                return "Battery " + (instanceId + 1).ToString(CultureInfo.InvariantCulture);
            }

            private FieldInfo[] GetMessageFields(uint messageId)
            {
                if (messageFieldsById.TryGetValue(messageId, out var cachedFields))
                {
                    return cachedFields;
                }

                var type = MAVLink.MAVLINK_MESSAGE_INFOS.GetMessageInfo(messageId).type;
                if (type == null)
                {
                    cachedFields = Array.Empty<FieldInfo>();
                }
                else
                {
                    cachedFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                    Array.Sort(cachedFields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                }

                messageFieldsById[messageId] = cachedFields;
                return cachedFields;
            }

            private bool IsNodeVisible(TreeNode node)
            {
                var bounds = node.Bounds;
                return bounds.Width > 0 && bounds.Height > 0 && treeView.ClientRectangle.IntersectsWith(bounds);
            }

            private static string FormatFieldPreviewValue(object value)
            {
                if (value == null)
                {
                    return "--";
                }

                if (value is byte[] bytes)
                {
                    var text = System.Text.Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
                    return string.IsNullOrEmpty(text) ? "[" + bytes.Length.ToString(CultureInfo.InvariantCulture) + "]" : text;
                }

                if (value is Array array)
                {
                    return "[" + array.Length.ToString(CultureInfo.InvariantCulture) + "]";
                }

                if (value is IFormattable formattable)
                {
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                }

                return value.ToString() ?? "--";
            }
        }

        private sealed class EditModeOverlayControl : Control
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TRANSPARENT = 0x20;
                    var createParams = base.CreateParams;
                    // Let underlying controls paint first so only border strokes are visible.
                    createParams.ExStyle |= WS_EX_TRANSPARENT;
                    return createParams;
                }
            }

            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                // Keep the overlay background clear so only the border is rendered.
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var borderRect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
                if (borderRect.Width <= 0 || borderRect.Height <= 0)
                {
                    return;
                }

                using (var pen = new Pen(Color.Yellow))
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, borderRect);
                }
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_NCHITTEST = 0x84;
                const int HTTRANSPARENT = -1;

                if (m.Msg == WM_NCHITTEST)
                {
                    // Make overlay mouse-transparent so drag/drop and clicks reach tiles/buttons.
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }

                base.WndProc(ref m);
            }
        }
    }
}
