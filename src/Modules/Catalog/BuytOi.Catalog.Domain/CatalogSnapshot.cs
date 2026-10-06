using BuytOi.Gtfs;

namespace BuytOi.Catalog.Domain;

public sealed record RouteSummary(string Id, string ShortName, string LongName, RouteType Type, string? Color);

public sealed record StopInfo(string Id, string? Code, string Name, double Lat, double Lon);

public sealed record NearbyStop(string Id, string? Code, string Name, double Lat, double Lon, int DistanceMeters);

/// <param name="Stops">Trạm theo thứ tự của chuyến có nhiều trạm nhất trong hướng này.</param>
/// <param name="Path">Shape của chuyến đó (map-match OSM); null khi feed không có shape → nối thẳng các trạm.</param>
public sealed record RouteDirection(int DirectionId, string? Headsign, IReadOnlyList<StopInfo> Stops,
    IReadOnlyList<GeoPoint>? Path = null);

public sealed record RouteDetail(string Id, string ShortName, string LongName, RouteType Type, string? Color,
    string AgencyName, IReadOnlyList<RouteDirection> Directions);

/// <summary>Dữ liệu tra cứu bất biến (dựng từ feed GTFS ở Infrastructure).</summary>
public sealed class CatalogSnapshot(IReadOnlyList<RouteSummary> routes, IReadOnlyList<StopInfo> stops,
    IReadOnlyDictionary<string, RouteDetail> details)
{
    public IReadOnlyList<RouteSummary> Routes { get; } = routes;
    public IReadOnlyList<StopInfo> Stops { get; } = stops;

    public RouteDetail? Route(string id) => details.GetValueOrDefault(id);

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
}
