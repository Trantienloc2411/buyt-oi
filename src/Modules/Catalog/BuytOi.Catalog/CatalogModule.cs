using BuytOi.Gtfs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuytOi.Catalog;

public static class CatalogModule
{
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
        return app;
    }
}
