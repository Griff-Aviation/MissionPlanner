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
        private readonly ToolStripMenuItem configureTileMenuItem = new ToolStripMenuItem("Configure...");
        private readonly ToolStripMenuItem removeTileMenuItem = new ToolStripMenuItem("Remove");
        private static readonly string[] ThresholdOperators = { ">", "<", "==", "!=" };
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
            // Rebuild panel from config order so startup and reset are deterministic.
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
                explorerWindow = new ExplorerWindowForm(IsFieldTileSelected, HandleExplorerFieldCheckedChanged, GetExplorerFieldLabelOverride);
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
            // Resolve the owning tile from whichever inner child was right-clicked.
            contextMenuTargetTile = GetTileFromControl(tileContextMenu.SourceControl);
            configureTileMenuItem.Enabled = contextMenuTargetTile != null;
            removeTileMenuItem.Enabled = editMode && contextMenuTargetTile != null;

            if (contextMenuTargetTile == null)
            {
                e.Cancel = true;
            }
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

            RemoveTileForFieldKey(tiles[index].Config.FieldKey);
            contextMenuTargetTile = null;
            RefreshTiles();
            RefreshExplorerFieldSelectionState();
        }

        private void configureTileMenuItem_Click(object sender, EventArgs e)
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

            var config = tiles[index].Config;
            if (!ShowTileConfigurationDialog(config))
            {
                return;
            }

            RefreshTiles();
            SaveDashboardConfig();
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
                dialog.ClientSize = new Size(360, 230);

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
                    Text = config.Thresholds?.Warning?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
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
                    Text = config.Thresholds?.Critical?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                var comboInstance = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };

                var instanceOptions = BuildInstanceOptions(config.FieldKey);
                foreach (var option in instanceOptions)
                {
                    comboInstance.Items.Add(option);
                }

                comboInstance.SelectedIndex = FindInstanceOptionIndex(instanceOptions, config.FieldKey.InstanceId);

                var warningEditor = CreateThresholdEditor(comboWarningOperator, textWarning);
                var criticalEditor = CreateThresholdEditor(comboCriticalOperator, textCritical);

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    ColumnCount = 2,
                    RowCount = 7
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

                AddConfigRow(layout, 0, "Label", textLabel);
                AddConfigRow(layout, 1, "Units", textUnits);
                AddConfigRow(layout, 2, "Decimal pts", decimalsEditor);
                AddConfigRow(layout, 3, "Warning", warningEditor);
                AddConfigRow(layout, 4, "Critical", criticalEditor);
                AddConfigRow(layout, 5, "Instance", comboInstance);
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft
                };

                var buttonOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                var buttonCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttonPanel.Controls.Add(buttonOk);
                buttonPanel.Controls.Add(buttonCancel);

                layout.Controls.Add(buttonPanel, 0, 6);
                layout.SetColumnSpan(buttonPanel, 2);

                dialog.Controls.Add(layout);
                dialog.AcceptButton = buttonOk;
                dialog.CancelButton = buttonCancel;
                MissionPlanner.Utilities.ThemeManager.ApplyThemeTo(dialog);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                if (!TryParseNullableDouble(textWarning.Text, out var warningValue) ||
                    !TryParseNullableDouble(textCritical.Text, out var criticalValue))
                {
                    MessageBox.Show(this, "Warning/Critical thresholds must be numeric values.", "Invalid Threshold",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                var labelOverride = NormalizeOverrideText(textLabel.Text);
                var unitsOverride = NormalizeOverrideText(textUnits.Text);
                var normalizedDefaultLabel = NormalizeOverrideText(defaultLabelText);
                var normalizedDefaultUnits = NormalizeOverrideText(defaultUnitsText);
                config.LabelOverride = string.Equals(labelOverride, normalizedDefaultLabel, StringComparison.Ordinal) ? null : labelOverride;
                config.UnitsOverride = string.Equals(unitsOverride, normalizedDefaultUnits, StringComparison.Ordinal) ? null : unitsOverride;
                config.DecimalPlaces = checkAutoDecimals.Checked ? (int?)null : (int)inputCustomDecimals.Value;
                config.Thresholds = config.Thresholds ?? new DashboardThresholdConfig();
                config.Thresholds.WarningOperator = ResolveThresholdOperator(comboWarningOperator.SelectedItem as string);
                config.Thresholds.Warning = warningValue;
                config.Thresholds.CriticalOperator = ResolveThresholdOperator(comboCriticalOperator.SelectedItem as string);
                config.Thresholds.Critical = criticalValue;

                var selectedInstance = comboInstance.SelectedItem as InstanceOption;
                config.FieldKey.InstanceId = selectedInstance?.InstanceId;
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
            // Keep operator and numeric threshold value together on one row.
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

        private static List<InstanceOption> BuildInstanceOptions(FieldKey fieldKey)
        {
            var options = new List<InstanceOption>
            {
                new InstanceOption("Default", null)
            };

            if (!IsBatteryRelatedField(fieldKey?.Field))
            {
                return options;
            }

            // Store raw IDs while showing human-friendly battery names.
            options.Add(new InstanceOption("Battery 1", 0));
            options.Add(new InstanceOption("Battery 2", 1));

            var currentInstance = fieldKey?.InstanceId;
            if (currentInstance.HasValue && currentInstance.Value != 0 && currentInstance.Value != 1)
            {
                options.Add(new InstanceOption("Instance " + currentInstance.Value.ToString(CultureInfo.InvariantCulture), currentInstance.Value));
            }

            return options;
        }

        private static int FindInstanceOptionIndex(List<InstanceOption> options, int? instanceId)
        {
            if (options == null || options.Count == 0)
            {
                return -1;
            }

            var idx = options.FindIndex(x => x.InstanceId == instanceId);
            return idx >= 0 ? idx : 0;
        }

        private static bool IsBatteryRelatedField(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            var normalized = NormalizeCurrentStateFieldName(fieldName);
            return normalized.StartsWith("battery_voltage", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("battery_remaining", StringComparison.OrdinalIgnoreCase);
        }

        private static FieldState ApplyThresholdState(FieldValue fieldValue, DashboardThresholdConfig thresholds)
        {
            if (fieldValue == null || fieldValue.State == FieldState.Inactive)
            {
                return FieldState.Inactive;
            }

            if (thresholds == null || (!thresholds.Warning.HasValue && !thresholds.Critical.HasValue))
            {
                return fieldValue.State;
            }

            if (!TryParseNumericValue(fieldValue.FormattedValue, out var numericValue))
            {
                return fieldValue.State;
            }

            var thresholdState = FieldState.Normal;
            if (thresholds.Critical.HasValue &&
                ThresholdCrossed(numericValue, thresholds.Critical.Value, thresholds.CriticalOperator))
            {
                thresholdState = FieldState.Critical;
            }
            else if (thresholds.Warning.HasValue &&
                     ThresholdCrossed(numericValue, thresholds.Warning.Value, thresholds.WarningOperator))
            {
                thresholdState = FieldState.Warning;
            }

            return MaxState(fieldValue.State, thresholdState);
        }

        private static bool ThresholdCrossed(double value, double threshold, string comparisonOperator)
        {
            switch (ResolveThresholdOperator(comparisonOperator))
            {
                case "<":
                    return value < threshold;
                case "==":
                    return Math.Abs(value - threshold) <= 0.0000001d;
                case "!=":
                    return Math.Abs(value - threshold) > 0.0000001d;
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
                    Field = source.FieldKey.Field,
                    InstanceId = source.FieldKey.InstanceId
                },
                IsVisible = source.IsVisible,
                LabelOverride = source.LabelOverride,
                UnitsOverride = source.UnitsOverride,
                DecimalPlaces = source.DecimalPlaces,
                Thresholds = new DashboardThresholdConfig
                {
                    WarningOperator = source.Thresholds?.WarningOperator,
                    Warning = source.Thresholds?.Warning,
                    CriticalOperator = source.Thresholds?.CriticalOperator,
                    Critical = source.Thresholds?.Critical
                },
                TileType = source.TileType
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

        private sealed class InstanceOption
        {
            public InstanceOption(string label, int? instanceId)
            {
                Label = label;
                InstanceId = instanceId;
            }

            public string Label { get; }
            public int? InstanceId { get; }

            public override string ToString()
            {
                return Label;
            }
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

                headerPanel.Controls.Add(showAllFieldTypesCheckBox);
                headerPanel.Controls.Add(searchTextBox);
                headerPanel.Controls.Add(searchLabel);
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
                        entry.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        entry.FieldName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (entry.OverrideLabel == null ||
                         entry.OverrideLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
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
