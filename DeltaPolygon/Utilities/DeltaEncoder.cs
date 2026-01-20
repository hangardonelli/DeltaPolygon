namespace DeltaPolygon.Utilities;

/// <summary>
/// Utilities for delta encoding and range compression
/// </summary>
public static class DeltaEncoder
{
    /// <summary>
    /// Encodes a list of consecutive IDs as a range
    /// Example: [1, 2, 3, 4] -> "1-4"
    /// </summary>
    public static string EncodeRange(IEnumerable<int> ids)
    {
        var sortedIds = ids.OrderBy(id => id).ToList();
        if (sortedIds.Count == 0)
        {
            return string.Empty;
        }

        var ranges = new List<string>();
        int? rangeStart = null;
        int? rangeEnd = null;

        foreach (var id in sortedIds)
        {
            if (!rangeStart.HasValue)
            {
                rangeStart = id;
                rangeEnd = id;
            }
            else if (id == rangeEnd!.Value + 1)
            {
                rangeEnd = id;
            }
            else
            {
                // Close the current range
                if (rangeStart == rangeEnd)
                {
                    ranges.Add(rangeStart.Value.ToString());
                }
                else
                {
                    ranges.Add($"{rangeStart}-{rangeEnd}");
                }

                rangeStart = id;
                rangeEnd = id;
            }
        }

        // Close the last range
        if (rangeStart.HasValue)
        {
            if (rangeStart == rangeEnd)
            {
                ranges.Add(rangeStart.Value.ToString());
            }
            else
            {
                ranges.Add($"{rangeStart}-{rangeEnd}");
            }
        }

        return string.Join(", ", ranges);
    }

    /// <summary>
    /// Decodes an encoded range back to a list of IDs
    /// Example: "1-4" -> [1, 2, 3, 4]
    /// </summary>
    public static List<int> DecodeRange(string encodedRange)
    {
        var ids = new List<int>();
        var parts = encodedRange.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0].Trim(), out var start) &&
                    int.TryParse(rangeParts[1].Trim(), out var end))
                {
                    for (int i = start; i <= end; i++)
                    {
                        ids.Add(i);
                    }
                }
            }
            else
            {
                if (int.TryParse(part.Trim(), out var id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Calculates the delta between two points
    /// </summary>
    public static Models.Point CalculateDelta(Models.Point from, Models.Point to)
    {
        return to - from;
    }

    /// <summary>
    /// Applies a delta to a base point
    /// </summary>
    public static Models.Point ApplyDelta(Models.Point basePoint, Models.Point delta)
    {
        return basePoint + delta;
    }
}
