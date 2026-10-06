using BuytOi.Catalog.Domain;
using BuytOi.Gtfs;

namespace BuytOi.Catalog.Infrastructure;

/// <summary>Adapter: dựng <see cref="CatalogSnapshot"/> từ feed GTFS.</summary>
public static class GtfsCatalogBuilder
{
    public static CatalogSnapshot Build(GtfsFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        var stops = feed.Stops.Select(s => new StopInfo(s.Id, s.Code, s.Name, s.Lat, s.Lon)).ToList();
        var stopById = stops.ToDictionary(s => s.Id);
        var agencyName = feed.Agencies.ToDictionary(a => a.Id, a => a.Name);
        var stopTimesByTrip = feed.StopTimes.ToLookup(st => st.TripId);
        var tripsByRoute = feed.Trips.ToLookup(t => t.RouteId);
        var shapes = feed.Shapes.GroupBy(sp => sp.ShapeId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<GeoPoint>)g.OrderBy(sp => sp.Sequence).Select(sp => new GeoPoint(sp.Lat, sp.Lon)).ToList());

        var details = feed.Routes.ToDictionary(r => r.Id, r => new RouteDetail(
            r.Id, r.ShortName, r.LongName, r.Type, r.Color, agencyName[r.AgencyId],
            tripsByRoute[r.Id]
                .GroupBy(t => t.DirectionId)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var longest = g.MaxBy(t => stopTimesByTrip[t.Id].Count())!;
                    var directionStops = stopTimesByTrip[longest.Id].OrderBy(st => st.Sequence).Select(st => stopById[st.StopId]).ToList();
                    var path = longest.ShapeId is { } shapeId ? shapes.GetValueOrDefault(shapeId) : null;
                    var shapeOk = path is not null && ShapeMatching.TryStopPositions(
                        path, directionStops.Select(s => new GeoPoint(s.Lat, s.Lon)).ToList()) is not null;
                    return new RouteDirection(g.Key, longest.Headsign, directionStops, shapeOk ? path : null);
                })
                .ToList()));

        var routes = feed.Routes.Select(r => new RouteSummary(r.Id, r.ShortName, r.LongName, r.Type, r.Color)).ToList();
        return new CatalogSnapshot(routes, stops, details);
    }
}
