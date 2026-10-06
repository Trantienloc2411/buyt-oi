using BuytOi.Gtfs;

namespace BuytOi.Routing.Domain;

public static class Walking
{
    private const double SpeedMetersPerSecond = 1.2;

    /// <summary>Đường đi bộ thật dài hơn đường chim bay (chưa có mạng đường OSM).</summary>
    public const double DetourFactor = 1.3;

    public static int Meters(double straightMeters) => (int)Math.Round(straightMeters * DetourFactor);

    public static int Seconds(double straightMeters) =>
        (int)Math.Ceiling(straightMeters * DetourFactor / SpeedMetersPerSecond);
}

/// <summary>
/// Lịch chạy dạng struct-of-arrays cho RAPTOR, bất biến (ADR 0002). Dựng từ feed GTFS ở Infrastructure.
/// Chỉ số nguyên thay cho id chuỗi: trạm, pattern, chuyến đều là vị trí trong mảng.
/// </summary>
/// <remarks>
/// Pattern = dãy chuyến cùng tuyến, cùng hướng, cùng dãy trạm và không vượt nhau (FIFO):
/// giờ tại mọi trạm tăng dần theo thứ tự chuyến, nên tìm chuyến sớm nhất bằng tìm kiếm nhị phân.
/// </remarks>
public sealed class Timetable
{
    /// <summary>Bán kính tối đa giữa hai trạm để đi bộ đổi tuyến.</summary>
    public const double MaxTransferMeters = 400;

    public required Stop[] Stops { get; init; }
    public required Route[] Routes { get; init; }
    public required Calendar[] Services { get; init; }
    public required TimeZoneInfo TimeZone { get; init; }

    // Pattern p: trạm PatternStops[PatternStopStart[p]..PatternStopStart[p+1]), chuyến PatternTripStart[p]..PatternTripStart[p+1).
    public required int[] PatternStopStart { get; init; }
    public required int[] PatternStops { get; init; }
    public required int[] PatternTripStart { get; init; }
    public required int[] PatternRoute { get; init; }

    // Hình dạng tuyến (map-match OSM, có thể không có). Trạm ở vị trí i của pattern p nằm tại
    // ShapePoints[PatternStopShape[PatternStopStart[p] + i]]; -1 = pattern không có shape → nối thẳng các trạm.
    public required GeoPoint[] ShapePoints { get; init; }
    public required int[] PatternStopShape { get; init; }

    // Chuyến t: giờ tại vị trí i của pattern nằm ở Arrivals/Departures/Timepoints[TripTimeStart[t] + i].
    public required Trip[] Trips { get; init; }
    public required int[] TripPattern { get; init; }
    public required int[] TripService { get; init; }
    public required int[] TripTimeStart { get; init; }
    public required int[] Arrivals { get; init; }
    public required int[] Departures { get; init; }
    public required bool[] Timepoints { get; init; }

    // Trạm s: các pattern đi qua StopPatterns[StopPatternStart[s]..StopPatternStart[s+1]).
    public required int[] StopPatternStart { get; init; }
    public required int[] StopPatterns { get; init; }

    // Trạm s: đi bộ tới TransferTo[i] mất TransferSeconds[i] giây, i trong TransferStart[s]..TransferStart[s+1).
    public required int[] TransferStart { get; init; }
    public required int[] TransferTo { get; init; }
    public required int[] TransferSeconds { get; init; }

    public int PatternStopCount(int pattern) => PatternStopStart[pattern + 1] - PatternStopStart[pattern];

    public int PatternStop(int pattern, int position) => PatternStops[PatternStopStart[pattern] + position];

    /// <summary>Đường xe chạy từ vị trí <paramref name="from"/> tới <paramref name="to"/> của pattern (gồm cả hai trạm).</summary>
    public List<GeoPoint> Path(int pattern, int from, int to)
    {
        GeoPoint At(int position) => new(Stops[PatternStop(pattern, position)].Lat, Stops[PatternStop(pattern, position)].Lon);
        var start = PatternStopShape[PatternStopStart[pattern] + from];
        if (start < 0) return Enumerable.Range(from, to - from + 1).Select(At).ToList();
        var end = PatternStopShape[PatternStopStart[pattern] + to];
        return [At(from), .. ShapePoints.AsSpan(start, end - start + 1), At(to)];
    }

    public int Arrival(int trip, int position) => Arrivals[TripTimeStart[trip] + position];

    public int Departure(int trip, int position) => Departures[TripTimeStart[trip] + position];

    public bool[] ActiveServices(DateOnly date)
    {
        var day = (ServiceDays)(1 << (((int)date.DayOfWeek + 6) % 7)); // DayOfWeek bắt đầu từ Chủ nhật
        return Services.Select(c => c.Days.HasFlag(day) && c.StartDate <= date && date <= c.EndDate).ToArray();
    }

    /// <summary>Trạm trong bán kính, kèm thời gian và quãng đường đi bộ.</summary>
    // ponytail: duyệt hết ~6 000 trạm như Catalog.Nearby; thêm lưới không gian khi cần.
    public List<StopAccess> StopsNear(double lat, double lon, double maxMeters)
    {
        var result = new List<StopAccess>();
        for (var s = 0; s < Stops.Length; s++)
        {
            var d = Geo.DistanceMeters(lat, lon, Stops[s].Lat, Stops[s].Lon);
            if (d <= maxMeters) result.Add(new StopAccess(s, Walking.Seconds(d), Walking.Meters(d)));
        }
        return result;
    }
}

public readonly record struct StopAccess(int Stop, int Seconds, int Meters);
