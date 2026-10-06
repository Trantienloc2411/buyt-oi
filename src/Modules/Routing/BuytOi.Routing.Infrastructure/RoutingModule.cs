using BuytOi.Gtfs;
using BuytOi.Routing.Application;
using BuytOi.Routing.Contracts;
using BuytOi.Routing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuytOi.Routing.Infrastructure;

// ponytail: Timetable dựng một lần lúc khởi động; hoán đổi (Interlocked.Exchange) khi có FeedPublished.
internal sealed class StaticTimetableSource(Timetable timetable) : ITimetableSource
{
    public Timetable Current => timetable;
}

/// <summary>Ghép module Routing: DI và endpoint HTTP (adapter phía vào).</summary>
public static class RoutingModule
{
    // Tên tránh trùng IServiceCollection.AddRouting() của ASP.NET Core.
    public static IServiceCollection AddJourneyPlanner(this IServiceCollection services, GtfsFeed feed)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddSingleton<ITimetableSource>(new StaticTimetableSource(GtfsTimetableBuilder.Build(feed)))
            .AddSingleton<JourneyPlanner>()
            .AddSingleton<IJourneyPlanner>(sp => sp.GetRequiredService<JourneyPlanner>());
    }

    public static IEndpointRouteBuilder MapJourneyPlanner(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGroup("/api").WithTags("Routing").MapGet("/journeys",
            Results<Ok<IReadOnlyList<Journey>>, ValidationProblem> (
                double fromLat, double fromLon, double toLat, double toLon,
                JourneyPlanner planner, DateTime? departAt = null) =>
                JourneyPlanner.Validate(fromLat, fromLon, toLat, toLon) is { Count: > 0 } errors
                    ? TypedResults.ValidationProblem(errors)
                    : TypedResults.Ok(planner.Plan(
                        new JourneyQuery(fromLat, fromLon, toLat, toLon, planner.LocalDeparture(departAt)))));
        return app;
    }
}
