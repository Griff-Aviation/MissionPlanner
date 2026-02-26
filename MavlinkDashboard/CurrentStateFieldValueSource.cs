using System;
using System.Collections.Generic;

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

            if (!TryReadField(currentState, key.Field, out var label, out var formattedValue, out var units))
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
                case "GROUND_SPEED":
                    formattedValue = currentState.groundspeed.ToString("0.0");
                    units = GetSpeedUnits();
                    return true;
                default:
                    return false;
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
                case "GROUND_SPEED":
                    return "Groundspeed";
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
