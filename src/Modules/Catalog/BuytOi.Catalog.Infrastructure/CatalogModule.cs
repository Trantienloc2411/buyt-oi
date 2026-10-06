using BuytOi.Catalog.Application;
using BuytOi.Catalog.Domain;
using BuytOi.Gtfs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuytOi.Catalog.Infrastructure;

// ponytail: snapshot dựng một lần lúc khởi động; hoán đổi (Interlocked.Exchange) khi có FeedPublished.
internal sealed class SnapshotCatalogSource(CatalogSnapshot snapshot) : ICatalogSource
{
    public CatalogSnapshot Current => snapshot;
}

/// <summary>Ghép module Catalog: DI và endpoint HTTP (adapter phía vào).</summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalog(this IServiceCollection services, GtfsFeed feed) =>
        services
            .AddSingleton<ICatalogSource>(new SnapshotCatalogSource(GtfsCatalogBuilder.Build(feed)))
            .AddSingleton<CatalogQueries>();

    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup("/api").WithTags("Catalog");
        api.MapGet("/routes", (CatalogQueries catalog) => catalog.Routes());
        api.MapGet("/routes/{id}", Results<Ok<RouteDetail>, NotFound> (string id, CatalogQueries catalog) =>
            catalog.Route(id) is { } route ? TypedResults.Ok(route) : TypedResults.NotFound());
        api.MapGet("/stops", (CatalogQueries catalog) => catalog.Stops());
        api.MapGet("/stops/nearby", Results<Ok<IReadOnlyList<NearbyStop>>, ValidationProblem> (
            double lat, double lon, CatalogQueries catalog, int radius = 500, int limit = 20) =>
            CatalogQueries.ValidateNearby(lat, lon, radius, limit) is { Count: > 0 } errors
                ? TypedResults.ValidationProblem(errors)
                : TypedResults.Ok(catalog.Nearby(lat, lon, radius, limit)));
        return app;
    }
}
