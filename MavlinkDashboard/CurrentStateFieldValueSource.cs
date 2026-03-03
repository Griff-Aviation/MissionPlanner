using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace MissionPlanner.MavlinkDashboard
{
    public class CurrentStateFieldValueSource : IFieldValueSource
    {
        private static readonly TimeSpan SoftStaleThreshold = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan InactiveThreshold = TimeSpan.FromSeconds(5);
        private readonly object sync = new object();
        private readonly Dictionary<string, FieldTracker> fieldTrackers = new Dictionary<string, FieldTracker>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetValue(FieldKey key, out FieldValue value)
        {
            value = null;

            if (key == null || string.IsNullOrEmpty(key.Field))
            {
                return false;
            }

            var currentState = MainV2.comPort?.MAV?.cs;
            if (currentState == null)
            {
                value = CreateInactiveValue(key.Field);
                return true;
            }

            var resolvedField = ResolveFieldForInstance(key.Field, key.InstanceId);
            if (!TryReadField(currentState, resolvedField, out var label, out var formattedValue, out var units))
            {
                return false;
            }

            var sampleTimestampUtc = GetCurrentStateTimestampUtc(currentState);
            if (!sampleTimestampUtc.HasValue)
            {
                value = CreateInactiveValue(key.Field);
                return true;
            }

            var trackerKey = GetTrackerKey(key);
            var valueToken = formattedValue + "|" + units;
            DateTime lastUpdateUtc;

            lock (sync)
            {
                if (!fieldTrackers.TryGetValue(trackerKey, out var tracker))
                {
                    tracker = new FieldTracker
                    {
                        LastValueToken = valueToken,
                        LastSampleTimestampUtc = sampleTimestampUtc.Value,
                        LastUpdateTimestampUtc = sampleTimestampUtc.Value
                    };
                    fieldTrackers[trackerKey] = tracker;
                }
                else
                {
                    var sampleAdvanced = sampleTimestampUtc.Value > tracker.LastSampleTimestampUtc;
                    var valueChanged = !string.Equals(tracker.LastValueToken, valueToken, StringComparison.Ordinal);

                    if (sampleAdvanced)
                    {
                        tracker.LastSampleTimestampUtc = sampleTimestampUtc.Value;
                    }

                    if (sampleAdvanced || valueChanged)
                    {
                        tracker.LastValueToken = valueToken;
                        tracker.LastUpdateTimestampUtc = sampleTimestampUtc.Value;
                    }
                }

                lastUpdateUtc = tracker.LastUpdateTimestampUtc;
            }

            var age = DateTime.UtcNow - lastUpdateUtc;
            var state = FieldState.Normal;

            if (age >= InactiveThreshold)
            {
                state = FieldState.Inactive;
            }
            else if (age >= SoftStaleThreshold)
            {
                state = FieldState.Warning;
            }
            value = new FieldValue
            {
                Label = label,
                FormattedValue = formattedValue,
                Units = units,
                TimestampUtc = lastUpdateUtc,
                State = state
            };

            return true;
        }

        private static bool TryReadField(CurrentState currentState, string field, out string label, out string formattedValue, out string units)
        {
            label = GetLabel(field);
            formattedValue = "-";
            units = string.Empty;

            switch (field.ToUpperInvariant())
            {
                case "ARMED":
                    formattedValue = currentState.armed ? "Armed" : "Disarmed";
                    return true;
                case "MODE":
                    formattedValue = string.IsNullOrWhiteSpace(currentState.mode) ? "-" : currentState.mode;
                    return true;
                case "ROLL":
                    formattedValue = currentState.roll.ToString("0.0");
                    units = "deg";
                    return true;
                case "PITCH":
                    formattedValue = currentState.pitch.ToString("0.0");
                    units = "deg";
                    return true;
                case "YAW":
                    formattedValue = currentState.yaw.ToString("0.0");
                    units = "deg";
                    return true;
                case "REL_ALT":
                    formattedValue = currentState.alt.ToString("0.0");
                    units = GetAltUnits();
                    return true;
                case "AMSL_ALT":
                    formattedValue = currentState.altasl.ToString("0.0");
                    units = GetAltUnits();
                    return true;
                case "AIR_SPEED":
                    formattedValue = currentState.airspeed.ToString("0.0");
                    units = GetSpeedUnits();
                    return true;
                case "GROUND_SPEED":
                    formattedValue = currentState.groundspeed.ToString("0.0");
                    units = GetSpeedUnits();
                    return true;
                case "GPS_FIX":
                    formattedValue = FormatGpsFixType(currentState.gpsstatus);
                    return true;
                case "GPS_SATS":
                    formattedValue = ((int)Math.Round(currentState.satcount)).ToString();
                    units = "sat";
                    return true;
                case "BATTERY1_VOLTAGE":
                    if (currentState.battery_voltage > 0)
                    {
                        formattedValue = currentState.battery_voltage.ToString("0.0");
                        units = "V";
                    }
                    return true;
                case "BATTERY1_REMAINING":
                    if (HasBatteryData(currentState))
                    {
                        formattedValue = currentState.battery_remaining.ToString("0");
                        units = "%";
                    }
                    return true;
                case "BATTERY2_VOLTAGE":
                    if (currentState.battery_voltage2 > 0)
                    {
                        formattedValue = currentState.battery_voltage2.ToString("0.0");
                        units = "V";
                    }
                    return true;
                case "BATTERY2_REMAINING":
                    if (HasBattery2Data(currentState))
                    {
                        formattedValue = currentState.battery_remaining2.ToString("0");
                        units = "%";
                    }
                    return true;
                case "LINK_QUALITY":
                    if (currentState.linkqualitygcs > 0)
                    {
                        formattedValue = currentState.linkqualitygcs.ToString();
                        units = "%";
                    }
                    else if (Math.Abs(currentState.localsnrdb) > 0.001f)
                    {
                        formattedValue = currentState.localsnrdb.ToString("0.0");
                        units = "dB";
                    }
                    return true;
                case "RSSI":
                    var rssiValue = currentState.remrssi > 0 ? currentState.remrssi : currentState.rssi;
                    if (rssiValue > 0)
                    {
                        formattedValue = rssiValue.ToString("0");
                        units = "raw";
                    }
                    return true;
                default:
                    return TryReadGenericCurrentStateField(currentState, field, out label, out formattedValue, out units);
            }
        }

        private static bool TryReadGenericCurrentStateField(CurrentState currentState, string field, out string label, out string formattedValue, out string units)
        {
            label = GetLabel(field);
            formattedValue = "-";
            units = string.Empty;

            if (currentState == null || string.IsNullOrWhiteSpace(field))
            {
                return false;
            }

            // Match QuickView "Display This": bind directly to CurrentState properties.
            var property = typeof(CurrentState).GetProperty(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property == null || property.GetIndexParameters().Length > 0)
            {
                return false;
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType != typeof(bool) && !IsNumericType(propertyType))
            {
                return false;
            }

            object rawValue;
            try
            {
                rawValue = property.GetValue(currentState, null);
            }
            catch
            {
                return false;
            }

            if (rawValue == null)
            {
                return false;
            }

            label = currentState.GetFieldDesc(property.Name);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = property.Name;
            }

            if (rawValue is bool boolValue)
            {
                formattedValue = boolValue ? "1" : "0";
                return true;
            }

            if (rawValue is IFormattable formattable)
            {
                formattedValue = formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            else
            {
                formattedValue = rawValue.ToString() ?? "-";
            }

            return true;
        }

        private static string ResolveFieldForInstance(string field, int? instanceId)
        {
            if (string.IsNullOrWhiteSpace(field) || !instanceId.HasValue)
            {
                return field;
            }

            var normalized = field.ToUpperInvariant();
            var battery1 = instanceId.Value <= 0;

            if (normalized == "BATTERY1_VOLTAGE" || normalized == "BATTERY2_VOLTAGE")
            {
                return battery1 ? "BATTERY1_VOLTAGE" : "BATTERY2_VOLTAGE";
            }

            if (normalized == "BATTERY1_REMAINING" || normalized == "BATTERY2_REMAINING")
            {
                return battery1 ? "BATTERY1_REMAINING" : "BATTERY2_REMAINING";
            }

            if (normalized == "BATTERY_VOLTAGE" || normalized == "BATTERY_VOLTAGE2")
            {
                return battery1 ? "battery_voltage" : "battery_voltage2";
            }

            if (normalized == "BATTERY_REMAINING" || normalized == "BATTERY_REMAINING2")
            {
                return battery1 ? "battery_remaining" : "battery_remaining2";
            }

            return field;
        }

        private static bool IsNumericType(Type type)
        {
            switch (Type.GetTypeCode(type))
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

        private static bool HasBatteryData(CurrentState currentState)
        {
            return currentState.battery_voltage > 0 || currentState.battery_remaining > 0;
        }

        private static bool HasBattery2Data(CurrentState currentState)
        {
            return currentState.battery_voltage2 > 0 || currentState.battery_remaining2 > 0;
        }

        private static string FormatGpsFixType(float gpsStatus)
        {
            switch ((int)Math.Round(gpsStatus))
            {
                case 0:
                    return "No GPS";
                case 1:
                    return "No Fix";
                case 2:
                    return "2D";
                case 3:
                    return "3D";
                case 4:
                    return "DGPS";
                case 5:
                    return "RTK Float";
                case 6:
                    return "RTK Fixed";
                default:
                    return ((int)Math.Round(gpsStatus)).ToString();
            }
        }

        private static DateTime? GetCurrentStateTimestampUtc(CurrentState currentState)
        {
            var timestamp = currentState.datetime;
            if (timestamp <= DateTime.MinValue.AddSeconds(1))
            {
                return null;
            }

            if (timestamp.Kind == DateTimeKind.Utc)
            {
                return timestamp;
            }

            if (timestamp.Kind == DateTimeKind.Local)
            {
                return timestamp.ToUniversalTime();
            }

            return DateTime.SpecifyKind(timestamp, DateTimeKind.Local).ToUniversalTime();
        }

        private static string GetTrackerKey(FieldKey key)
        {
            var instance = key.InstanceId.HasValue ? key.InstanceId.Value.ToString() : "default";
            return (key.Message ?? string.Empty) + ":" + (key.Field ?? string.Empty) + ":" + instance;
        }

        private static FieldValue CreateInactiveValue(string field)
        {
            return new FieldValue
            {
                Label = GetLabel(field),
                FormattedValue = "-",
                Units = string.Empty,
                TimestampUtc = DateTime.UtcNow,
                State = FieldState.Inactive
            };
        }

        private static string GetAltUnits()
        {
            return string.IsNullOrWhiteSpace(CurrentState.AltUnit) ? "m" : CurrentState.AltUnit;
        }

        private static string GetSpeedUnits()
        {
            return string.IsNullOrWhiteSpace(CurrentState.SpeedUnit) ? "m/s" : CurrentState.SpeedUnit;
        }

        private static string GetLabel(string field)
        {
            switch (field.ToUpperInvariant())
            {
                case "ARMED":
                    return "Armed";
                case "MODE":
                    return "Mode";
                case "ROLL":
                    return "Roll";
                case "PITCH":
                    return "Pitch";
                case "YAW":
                    return "Yaw";
                case "REL_ALT":
                    return "Rel Alt";
                case "AMSL_ALT":
                    return "AMSL Alt";
                case "AIR_SPEED":
                    return "Airspeed";
                case "GROUND_SPEED":
                    return "Groundspeed";
                case "GPS_FIX":
                    return "GPS Fix";
                case "GPS_SATS":
                    return "GPS Sats";
                case "BATTERY1_VOLTAGE":
                    return "Battery 1 V";
                case "BATTERY1_REMAINING":
                    return "Battery 1 %";
                case "BATTERY2_VOLTAGE":
                    return "Battery 2 V";
                case "BATTERY2_REMAINING":
                    return "Battery 2 %";
                case "LINK_QUALITY":
                    return "Link";
                case "RSSI":
                    return "RSSI";
                default:
                    return field;
            }
        }

        private sealed class FieldTracker
        {
            public string LastValueToken { get; set; }
            public DateTime LastSampleTimestampUtc { get; set; }
            public DateTime LastUpdateTimestampUtc { get; set; }
        }
    }
}
