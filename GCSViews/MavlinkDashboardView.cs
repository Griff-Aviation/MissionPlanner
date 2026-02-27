using MissionPlanner.Controls;
using MissionPlanner.MavlinkDashboard;
using System;
using System.ComponentModel;
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
            private readonly TreeView treeView = new TreeView();
            private readonly SortedDictionary<uint, TreeNode> messageNodes = new SortedDictionary<uint, TreeNode>();

            public ExplorerWindowForm()
            {
                // Lightweight Step 12 shell hosted in a separate window.
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

                var searchTextBox = new TextBox
                {
                    Name = "explorerSearchTextBox",
                    Dock = DockStyle.Top,
                    Height = 22
                };

                var showAllMessagesCheckBox = new CheckBox
                {
                    Name = "showAllMessagesCheckBox",
                    Text = "Show All Messages",
                    Dock = DockStyle.Top,
                    Height = 22
                };

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
                // Initial status until live MAVLink traffic is observed.
                treeView.Nodes.Add("Waiting for MAVLink messages...");

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

                var addedAny = false;

                foreach (var messageId in receivedMessageIds)
                {
                    if (messageNodes.ContainsKey(messageId))
                    {
                        continue;
                    }

                    var messageName = Enum.IsDefined(typeof(MAVLink.MAVLINK_MSG_ID), (int) messageId)
                        ? ((MAVLink.MAVLINK_MSG_ID) messageId).ToString()
                        : "UNKNOWN";

                    var node = new TreeNode(messageName)
                    {
                        Name = "msg_" + messageId
                    };

                    messageNodes[messageId] = node;
                    addedAny = true;
                }

                if (!addedAny)
                {
                    return;
                }

                treeView.BeginUpdate();
                treeView.Nodes.Clear();

                // Keep deterministic ordering by MAVLink message ID.
                foreach (var node in messageNodes.Values)
                {
                    treeView.Nodes.Add(node);
                }

                treeView.EndUpdate();
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
