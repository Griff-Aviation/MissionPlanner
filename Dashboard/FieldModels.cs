using System;

namespace MissionPlanner.Dashboard
{
    public enum FieldState
    {
        Normal,
        Warning,
        DisconnectWarning,
        Critical,
        Inactive,
        Selected
    }

    public class FieldKey
    {
        public string Message { get; set; }
        public string Field { get; set; }
    }

    public class FieldValue
    {
        public string Label { get; set; }
        public string FormattedValue { get; set; }
        public string Units { get; set; }
        public DateTime TimestampUtc { get; set; }
        public FieldState State { get; set; }
    }

    public interface IFieldValueSource
    {
        bool TryGetValue(FieldKey key, out FieldValue value);
    }

    public class SyntheticFieldValueSource : IFieldValueSource
    {
        public bool TryGetValue(FieldKey key, out FieldValue value)
        {
            value = null;

            if (key == null || key.Message != "SYNTHETIC" || key.Field != "VALUE")
            {
                return false;
            }

            var t = DateTime.UtcNow.TimeOfDay.TotalSeconds;
            var syntheticValue = 50.0 + (45.0 * Math.Sin(t));

            value = new FieldValue
            {
                Label = "Mode",
                FormattedValue = syntheticValue.ToString("0.0"),
                Units = string.Empty,
                TimestampUtc = DateTime.UtcNow,
                State = FieldState.Normal
            };

            return true;
        }
    }
}
