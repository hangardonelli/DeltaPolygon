namespace DeltaPolygon.Models;

/// <summary>
/// Represents a temporal validity interval [start, end) with support for infinity
/// </summary>
public class TimeInterval : IEquatable<TimeInterval>
{
    public DateTime Start { get; }
    public DateTime? End { get; } // null represents infinity

    public TimeInterval(DateTime start, DateTime? end = null)
    {
        if (end.HasValue && end.Value <= start)
        {
            throw new ArgumentException("End time must be greater than start time", nameof(end));
        }

        Start = start;
        End = end;
    }

    /// <summary>
    /// Checks if a given time is within the interval
    /// </summary>
    public bool Contains(DateTime time)
    {
        return time >= Start && (!End.HasValue || time < End.Value);
    }

    /// <summary>
    /// Checks if the interval is open (without end)
    /// </summary>
    public bool IsOpen => !End.HasValue;

    public bool Equals(TimeInterval? other)
    {
        if (other is null) return false;
        return Start == other.Start && End == other.End;
    }

    public override bool Equals(object? obj)
    {
        return obj is TimeInterval other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Start, End);
    }

    public override string ToString()
    {
        var endStr = End.HasValue ? End.Value.ToString("yyyy-MM-dd HH:mm:ss") : "∞";
        return $"[{Start:yyyy-MM-dd HH:mm:ss}, {endStr})";
    }
}
