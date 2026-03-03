using MissionPlanner.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlanner.MavlinkDashboard
{
    public enum DashboardValueSourcePreference
    {
        PreferCurrentState,
        PreferRaw
    }

    public class DashboardConfig
    {
        public List<DashboardTileConfig> Tiles { get; set; } = new List<DashboardTileConfig>();
        public DashboardLayoutConfig Layout { get; set; } = new DashboardLayoutConfig();
        public DashboardGlobalOptions GlobalOptions { get; set; } = new DashboardGlobalOptions();
    }

    public class DashboardTileConfig
    {
        public FieldKey FieldKey { get; set; } = new FieldKey();
        public bool IsVisible { get; set; } = true;
        public string LabelOverride { get; set; }
        public string UnitsOverride { get; set; }
        public int? DecimalPlaces { get; set; }
        public DashboardThresholdConfig Thresholds { get; set; } = new DashboardThresholdConfig();
        public string TileType { get; set; } = "Value";
    }

    public class DashboardThresholdConfig
    {
        public string WarningOperator { get; set; } = ">";
        public double? Warning { get; set; }
        public string CriticalOperator { get; set; } = ">";
        public double? Critical { get; set; }
    }

    public class DashboardLayoutConfig
    {
        public int ColumnsHint { get; set; } = 4;
        public int TileWidth { get; set; } = 170;
        public int TileHeight { get; set; } = 90;
    }

    public class DashboardGlobalOptions
    {
        public DashboardValueSourcePreference ValueSourcePreference { get; set; } = DashboardValueSourcePreference.PreferCurrentState;
    }

    public static class DashboardConfigStore
    {
        private const string ConfigFileName = "mavlink-dashboard.json";

        public static DashboardConfig LoadOrCreateDefault(Func<DashboardConfig> defaultFactory)
        {
            var path = GetConfigPath();

            if (File.Exists(path))
            {
                try
                {
                    var loaded = JsonConvert.DeserializeObject<DashboardConfig>(File.ReadAllText(path));
                    if (IsUsable(loaded))
                    {
                        return Normalize(loaded);
                    }
                }
                catch
                {
                }
            }

            var defaults = Normalize(defaultFactory());
            Save(defaults);
            return defaults;
        }

        public static void Save(DashboardConfig config)
        {
            if (config == null)
            {
                return;
            }

            var path = GetConfigPath();
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(Normalize(config), Formatting.Indented));
        }

        private static string GetConfigPath()
        {
            return Path.Combine(Settings.GetUserDataDirectory(), ConfigFileName);
        }

        private static bool IsUsable(DashboardConfig config)
        {
            return config != null && config.Tiles != null && config.Tiles.Count > 0;
        }

        private static DashboardConfig Normalize(DashboardConfig config)
        {
            if (config == null)
            {
                config = new DashboardConfig();
            }

            config.Tiles = config.Tiles ?? new List<DashboardTileConfig>();
            config.Layout = config.Layout ?? new DashboardLayoutConfig();
            config.GlobalOptions = config.GlobalOptions ?? new DashboardGlobalOptions();

            for (int i = config.Tiles.Count - 1; i >= 0; i--)
            {
                var tile = config.Tiles[i];
                if (tile?.FieldKey == null || string.IsNullOrWhiteSpace(tile.FieldKey.Field))
                {
                    config.Tiles.RemoveAt(i);
                    continue;
                }

                tile.Thresholds = tile.Thresholds ?? new DashboardThresholdConfig();
                if (string.IsNullOrWhiteSpace(tile.Thresholds.WarningOperator))
                {
                    tile.Thresholds.WarningOperator = ">";
                }

                if (string.IsNullOrWhiteSpace(tile.Thresholds.CriticalOperator))
                {
                    tile.Thresholds.CriticalOperator = ">";
                }

                if (string.IsNullOrWhiteSpace(tile.TileType))
                {
                    tile.TileType = "Value";
                }
            }

            return config;
        }
    }
}
