using BuytOi.Gtfs;

namespace BuytOi.Catalog;

public sealed record RouteSummary(string Id, string ShortName, string LongName, RouteType Type, string? Color);

public sealed record StopInfo(string Id, string? Code, string Name, double Lat, double Lon);

public sealed record NearbyStop(string Id, string? Code, string Name, double Lat, double Lon, int DistanceMeters);

/// <param name="Stops">Trạm theo thứ tự của chuyến có nhiều trạm nhất trong hướng này.</param>
public sealed record RouteDirection(int DirectionId, string? Headsign, IReadOnlyList<StopInfo> Stops);

public sealed record RouteDetail(string Id, string ShortName, string LongName, RouteType Type, string? Color,
    string AgencyName, IReadOnlyList<RouteDirection> Directions);

/// <summary>Dữ liệu tra cứu bất biến, dựng một lần từ feed GTFS.</summary>
public sealed class CatalogSnapshot
{
    private readonly Dictionary<string, RouteDetail> _details;

    private CatalogSnapshot(IReadOnlyList<RouteSummary> routes, IReadOnlyList<StopInfo> stops,
        Dictionary<string, RouteDetail> details)
    {
        Routes = routes;
        Stops = stops;
        _details = details;
    }

    public IReadOnlyList<RouteSummary> Routes { get; }
    public IReadOnlyList<StopInfo> Stops { get; }

    public RouteDetail? Route(string id) => _details.GetValueOrDefault(id);

    /// <summary>Trạm trong bán kính <paramref name="radiusMeters"/>, gần nhất trước.</summary>
    // ponytail: duyệt hết ~6 000 trạm (vài chục µs); thêm lưới/chỉ mục không gian khi số trạm tăng nhiều.
    public IReadOnlyList<NearbyStop> Nearby(double lat, double lon, int radiusMeters, int limit) =>
        Stops
            .Select(s => (Stop: s, Distance: Geo.DistanceMeters(lat, lon, s.Lat, s.Lon)))
            .Where(x => x.Distance <= radiusMeters)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => new NearbyStop(x.Stop.Id, x.Stop.Code, x.Stop.Name, x.Stop.Lat, x.Stop.Lon,
                (int)Math.Round(x.Distance)))
            .ToList();

    public static CatalogSnapshot From(GtfsFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        var stops = feed.Stops.Select(s => new StopInfo(s.Id, s.Code, s.Name, s.Lat, s.Lon)).ToList();
        var stopById = stops.ToDictionary(s => s.Id);
        var agencyName = feed.Agencies.ToDictionary(a => a.Id, a => a.Name);
        var stopTimesByTrip = feed.StopTimes.ToLookup(st => st.TripId);
        var tripsByRoute = feed.Trips.ToLookup(t => t.RouteId);

        var details = feed.Routes.ToDictionary(r => r.Id, r => new RouteDetail(
            r.Id, r.ShortName, r.LongName, r.Type, r.Color, agencyName[r.AgencyId],
            tripsByRoute[r.Id]
                .GroupBy(t => t.DirectionId)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var longest = g.MaxBy(t => stopTimesByTrip[t.Id].Count())!;
                    return new RouteDirection(g.Key, longest.Headsign,
                        stopTimesByTrip[longest.Id].OrderBy(st => st.Sequence).Select(st => stopById[st.StopId]).ToList());
                })
                .ToList()));

        var routes = feed.Routes.Select(r => new RouteSummary(r.Id, r.ShortName, r.LongName, r.Type, r.Color)).ToList();
        return new CatalogSnapshot(routes, stops, details);
    }
}
