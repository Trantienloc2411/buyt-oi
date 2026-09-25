namespace BuytOi.Gtfs;

// Tập con GTFS Schedule mà dự án cần. Tên trường theo https://gtfs.org/schedule/reference/

public sealed record Agency(string Id, string Name, string Url, string Timezone, string? Phone = null);

public enum RouteType { Tram = 0, Subway = 1, Rail = 2, Bus = 3 }

public sealed record Route(string Id, string AgencyId, string ShortName, string LongName, RouteType Type, string? Color = null);

public enum WheelchairBoarding { Unknown = 0, Accessible = 1, NotAccessible = 2 }

public sealed record Stop(string Id, string? Code, string Name, double Lat, double Lon,
    WheelchairBoarding Wheelchair = WheelchairBoarding.Unknown);

public sealed record Trip(string Id, string RouteId, string ServiceId, int DirectionId,
    string? Headsign = null, string? ShapeId = null);

/// <summary>Giờ GTFS tính từ nửa đêm ngày phục vụ; có thể vượt 24:00:00 (chuyến qua nửa đêm).</summary>
public readonly record struct GtfsTime(int TotalSeconds)
{
    public static GtfsTime FromHm(int hours, int minutes) => new(hours * 3600 + minutes * 60);

    public override string ToString() =>
        $"{TotalSeconds / 3600:00}:{TotalSeconds / 60 % 60:00}:{TotalSeconds % 60:00}";
}

/// <param name="Timepoint">false = giờ nội suy, không phải giờ chính xác.</param>
public sealed record StopTime(string TripId, int Sequence, string StopId, GtfsTime Arrival, GtfsTime Departure,
    bool Timepoint = true);

[Flags]
public enum ServiceDays
{
    None = 0,
    Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64,
}

public sealed record Calendar(string ServiceId, ServiceDays Days, DateOnly StartDate, DateOnly EndDate);

public sealed record ShapePoint(string ShapeId, int Sequence, double Lat, double Lon);

public sealed record GtfsFeed(
    IReadOnlyList<Agency> Agencies,
    IReadOnlyList<Route> Routes,
    IReadOnlyList<Stop> Stops,
    IReadOnlyList<Trip> Trips,
    IReadOnlyList<StopTime> StopTimes,
    IReadOnlyList<Calendar> Calendars,
    IReadOnlyList<ShapePoint> Shapes);
