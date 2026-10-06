using BuytOi.Catalog.Domain;

namespace BuytOi.Catalog.Application;

/// <summary>Port: nơi lấy snapshot hiện hành (Infrastructure cài đặt; sau này hoán đổi khi có FeedPublished).</summary>
public interface ICatalogSource
{
    CatalogSnapshot Current { get; }
}

/// <summary>Use case tra cứu tuyến/trạm, không phụ thuộc HTTP.</summary>
public sealed class CatalogQueries(ICatalogSource source)
{
    public const int MaxRadiusMeters = 5_000;
    public const int MaxLimit = 100;

    public IReadOnlyList<RouteSummary> Routes() => source.Current.Routes;

    public RouteDetail? Route(string id) => source.Current.Route(id);

    public IReadOnlyList<StopInfo> Stops() => source.Current.Stops;

    public IReadOnlyList<NearbyStop> Nearby(double lat, double lon, int radiusMeters, int limit) =>
        source.Current.Nearby(lat, lon, radiusMeters, limit);

    /// <returns>Lỗi theo tên tham số; rỗng khi hợp lệ.</returns>
    public static Dictionary<string, string[]> ValidateNearby(double lat, double lon, int radiusMeters, int limit)
    {
        var errors = new Dictionary<string, string[]>();
        // NaN/Infinity parse được thành double và lọt qua so sánh khoảng → chặn riêng.
        if (!double.IsFinite(lat) || lat is < -90 or > 90) errors["lat"] = ["Phải trong khoảng -90..90."];
        if (!double.IsFinite(lon) || lon is < -180 or > 180) errors["lon"] = ["Phải trong khoảng -180..180."];
        if (radiusMeters is < 1 or > MaxRadiusMeters) errors["radius"] = [$"Phải trong khoảng 1..{MaxRadiusMeters} mét."];
        if (limit is < 1 or > MaxLimit) errors["limit"] = [$"Phải trong khoảng 1..{MaxLimit}."];
        return errors;
    }
}
