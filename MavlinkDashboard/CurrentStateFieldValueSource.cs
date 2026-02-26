using System;

namespace MissionPlanner.MavlinkDashboard
{
    public class CurrentStateFieldValueSource : IFieldValueSource
    {
        public bool TryGetValue(FieldKey key, out FieldValue value)
        {
            value = null;

            if (key == null || string.IsNullOrEmpty(key.Field))
            {
                return false;
            }

            var currentState = MainV2.comPort?.MAV?.cs;
            if (currentState == null || !IsConnected(currentState))
            {
                value = CreateInactiveValue(key.Field);
                return true;
            }

            switch (key.Field.ToUpperInvariant())
            {
                case "ARMED":
                    value = CreateValue("Armed", currentState.armed ? "Armed" : "Disarmed", string.Empty);
                    return true;
                case "MODE":
                    value = CreateValue("Mode", string.IsNullOrWhiteSpace(currentState.mode) ? "-" : currentState.mode, string.Empty);
                    return true;
                case "ROLL":
                    value = CreateValue("Roll", currentState.roll.ToString("0.0"), "deg");
                    return true;
                case "PITCH":
                    value = CreateValue("Pitch", currentState.pitch.ToString("0.0"), "deg");
                    return true;
                case "YAW":
                    value = CreateValue("Yaw", currentState.yaw.ToString("0.0"), "deg");
                    return true;
                case "REL_ALT":
                    value = CreateValue("Rel Alt", currentState.alt.ToString("0.0"), GetAltUnits());
                    return true;
                case "GROUND_SPEED":
                    value = CreateValue("Groundspeed", currentState.groundspeed.ToString("0.0"), GetSpeedUnits());
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsConnected(CurrentState currentState)
        {
            try
            {
                return currentState.connected;
            }
            catch
            {
                return false;
            }
        }

        private static FieldValue CreateValue(string label, string formattedValue, string units)
        {
            return new FieldValue
            {
                Label = label,
                FormattedValue = formattedValue,
                Units = units,
                TimestampUtc = DateTime.UtcNow,
                State = FieldState.Normal
            };
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
    }
}
