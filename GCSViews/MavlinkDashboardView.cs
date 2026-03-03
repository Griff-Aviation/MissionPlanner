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
            RefreshExplorerFieldSelectionState();
        }

        private void AddTile(DashboardTileConfig tileConfig)
        {
            if (tileConfig?.FieldKey == null || string.IsNullOrWhiteSpace(tileConfig.FieldKey.Field))
            {
                return;
            }

            var fieldName = tileConfig.FieldKey.Field;
            var instanceSuffix = tileConfig.FieldKey.InstanceId.HasValue
                ? "_" + tileConfig.FieldKey.InstanceId.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            var tile = new TelemetryTileControl
            {
                Name = "tile_" + fieldName.ToLowerInvariant() + instanceSuffix
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
            RefreshExplorerFieldSelectionState();
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
                explorerWindow = new ExplorerWindowForm(IsFieldTileSelected, HandleExplorerFieldCheckedChanged);
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
            RefreshExplorerFieldSelectionState();
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

            AddTile(new DashboardTileConfig
            {
                FieldKey = CloneFieldKey(fieldKey),
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
                && string.Equals(leftField, rightField, StringComparison.OrdinalIgnoreCase)
                && left.InstanceId == right.InstanceId;
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
                Field = fieldKey.Field,
                InstanceId = fieldKey.InstanceId
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

                public override string ToString()
                {
                    return DisplayName;
                }
            }

            private readonly Label searchLabel = new Label();
            private readonly TextBox searchTextBox = new TextBox();
            private readonly CheckedListBox fieldList = new CheckedListBox();
            private readonly List<FieldEntry> allFields = new List<FieldEntry>();
            private readonly List<FieldEntry> visibleFields = new List<FieldEntry>();
            private readonly Func<FieldKey, bool> isFieldTileSelected;
            private readonly Action<FieldKey, bool> fieldCheckedChanged;
            private bool suppressFieldListEvents;
            private int lastCustomFieldNameCount = -1;
            private bool hasCurrentStateDisplayNames;

            public ExplorerWindowForm(Func<FieldKey, bool> isFieldTileSelected, Action<FieldKey, bool> fieldCheckedChanged)
            {
                this.isFieldTileSelected = isFieldTileSelected;
                this.fieldCheckedChanged = fieldCheckedChanged;

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
                    Height = 44
                };

                searchLabel.Name = "explorerSearchLabel";
                searchLabel.Text = "Search";
                searchLabel.Dock = DockStyle.Top;
                searchLabel.Height = 16;
                searchLabel.TextAlign = ContentAlignment.BottomLeft;

                searchTextBox.Name = "explorerSearchTextBox";
                searchTextBox.Dock = DockStyle.Top;
                searchTextBox.Height = 22;
                searchTextBox.TextChanged += searchTextBox_TextChanged;
                searchTextBox.KeyDown += searchTextBox_KeyDown;

                fieldList.Name = "explorerFieldList";
                fieldList.Dock = DockStyle.Fill;
                fieldList.CheckOnClick = true;
                fieldList.IntegralHeight = false;
                fieldList.ItemCheck += fieldList_ItemCheck;
                fieldList.KeyDown += fieldList_KeyDown;

                // Keep the field picker lean: all tile options come from CurrentState numeric/bool properties.
                BuildFieldCatalog(MainV2.comPort?.MAV?.cs);
                ApplyFilterAndRebind();

                headerPanel.Controls.Add(searchTextBox);
                headerPanel.Controls.Add(searchLabel);
                rootPanel.Controls.Add(fieldList);
                rootPanel.Controls.Add(headerPanel);
                Controls.Add(rootPanel);
            }

            public void RefreshFieldSelectionState()
            {
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
                    if (!IsNumericPropertyType(propertyType) && propertyType != typeof(bool))
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

                visibleFields.Clear();
                foreach (var entry in allFields)
                {
                    if (!string.IsNullOrWhiteSpace(query) &&
                        entry.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        entry.FieldName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
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
                    Field = fieldName,
                    InstanceId = null
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
