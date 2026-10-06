using BuytOi.Gtfs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuytOi.Catalog;

public static class CatalogModule
{
    private const int MaxRadiusMeters = 5_000;
    private const int MaxLimit = 100;

    // ponytail: snapshot nạp một lần lúc khởi động; hoán đổi (Interlocked.Exchange) khi có FeedPublished.
    public static IServiceCollection AddCatalog(this IServiceCollection services, string feedPath) =>
        services.AddSingleton(_ => CatalogSnapshot.From(GtfsReader.Read(feedPath)));

    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.ServiceProvider.GetRequiredService<CatalogSnapshot>(); // nạp ngay: feed lỗi thì dừng khi khởi động

        var api = app.MapGroup("/api").WithTags("Catalog");
        api.MapGet("/routes", (CatalogSnapshot catalog) => catalog.Routes);
        api.MapGet("/routes/{id}", Results<Ok<RouteDetail>, NotFound> (string id, CatalogSnapshot catalog) =>
            catalog.Route(id) is { } route ? TypedResults.Ok(route) : TypedResults.NotFound());
        api.MapGet("/stops", (CatalogSnapshot catalog) => catalog.Stops);
        api.MapGet("/stops/nearby", Results<Ok<IReadOnlyList<NearbyStop>>, ValidationProblem> (
            double lat, double lon, CatalogSnapshot catalog, int radius = 500, int limit = 20) =>
        {
            var errors = new Dictionary<string, string[]>();
            // NaN/Infinity parse được thành double và lọt qua so sánh khoảng → chặn riêng.
            if (!double.IsFinite(lat) || lat is < -90 or > 90) errors["lat"] = ["Phải trong khoảng -90..90."];
            if (!double.IsFinite(lon) || lon is < -180 or > 180) errors["lon"] = ["Phải trong khoảng -180..180."];
            if (radius is < 1 or > MaxRadiusMeters) errors["radius"] = [$"Phải trong khoảng 1..{MaxRadiusMeters} mét."];
            if (limit is < 1 or > MaxLimit) errors["limit"] = [$"Phải trong khoảng 1..{MaxLimit}."];
            return errors.Count > 0
                ? TypedResults.ValidationProblem(errors)
                : TypedResults.Ok(catalog.Nearby(lat, lon, radius, limit));
        });
        return app;
    }
}
