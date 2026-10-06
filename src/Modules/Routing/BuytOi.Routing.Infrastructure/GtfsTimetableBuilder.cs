using BuytOi.Gtfs;
using BuytOi.Routing.Domain;

namespace BuytOi.Routing.Infrastructure;

/// <summary>Adapter: dựng <see cref="Timetable"/> từ feed GTFS.</summary>
public static class GtfsTimetableBuilder
{
    public static Timetable Build(GtfsFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        var stops = feed.Stops.ToArray();
        var stopIndex = Index(stops, s => s.Id);
        var routes = feed.Routes.ToArray();
        var routeIndex = Index(routes, r => r.Id);
        var services = feed.Calendars.ToArray();
        var serviceIndex = Index(services, c => c.ServiceId);
        var stopTimesByTrip = feed.StopTimes.ToLookup(st => st.TripId);

        // Gom chuyến theo (tuyến, hướng, dãy trạm).
        var groups = new Dictionary<string, List<(Trip Trip, StopTime[] Times)>>();
        foreach (var trip in feed.Trips)
        {
            var times = stopTimesByTrip[trip.Id].OrderBy(st => st.Sequence).ToArray();
            if (times.Length < 2) continue;
            var key = $"{trip.RouteId}|{trip.DirectionId}|{string.Join(',', times.Select(st => st.StopId))}";
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add((trip, times));
        }

        // Tách mỗi nhóm thành các pattern FIFO: chuyến chạy nhanh vượt chuyến trước sang pattern khác.
        var patterns = new List<List<(Trip Trip, StopTime[] Times)>>();
        foreach (var group in groups.Values)
        {
            var lanes = new List<List<(Trip Trip, StopTime[] Times)>>();
            foreach (var trip in group.OrderBy(x => x.Times[0].Departure.TotalSeconds).ThenBy(x => x.Times[^1].Arrival.TotalSeconds))
            {
                var lane = lanes.FirstOrDefault(l => !Overtakes(l[^1].Times, trip.Times));
                if (lane is null) lanes.Add(lane = []);
                lane.Add(trip);
            }
            patterns.AddRange(lanes);
        }

        var patternStopStart = new int[patterns.Count + 1];
        var patternTripStart = new int[patterns.Count + 1];
        var patternRoute = new int[patterns.Count];
        var patternStops = new List<int>();
        var shapes = feed.Shapes.GroupBy(sp => sp.ShapeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(sp => sp.Sequence).Select(sp => new GeoPoint(sp.Lat, sp.Lon)).ToArray());
        var shapePoints = new List<GeoPoint>();
        var patternStopShape = new List<int>();
        var trips = new List<Trip>();
        var tripPattern = new List<int>();
        var tripService = new List<int>();
        var tripTimeStart = new List<int>();
        var arrivals = new List<int>();
        var departures = new List<int>();
        var timepoints = new List<bool>();
        var patternsByStop = Enumerable.Range(0, stops.Length).Select(_ => new List<int>()).ToArray();

        for (var p = 0; p < patterns.Count; p++)
        {
            var first = patterns[p][0];
            patternStopStart[p] = patternStops.Count;
            patternTripStart[p] = trips.Count;
            patternRoute[p] = routeIndex[first.Trip.RouteId];
            // Pattern dùng shape của chuyến đầu (cùng dãy trạm nên cùng đường); gán vị trí trạm lên shape.
            var stopPoints = first.Times.Select(st => stops[stopIndex[st.StopId]]).Select(s => new GeoPoint(s.Lat, s.Lon)).ToList();
            if (first.Trip.ShapeId is { } shapeId && shapes.TryGetValue(shapeId, out var shape)
                && ShapeMatching.TryStopPositions(shape, stopPoints) is { } positions)
            {
                var offset = shapePoints.Count;
                shapePoints.AddRange(shape);
                patternStopShape.AddRange(positions.Select(i => offset + i));
            }
            else
            {
                patternStopShape.AddRange(Enumerable.Repeat(-1, first.Times.Length));
            }

            foreach (var st in first.Times)
            {
                var s = stopIndex[st.StopId];
                patternStops.Add(s);
                if (patternsByStop[s] is var list && (list.Count == 0 || list[^1] != p)) list.Add(p); // tuyến vòng: trạm lặp lại
            }

            foreach (var (trip, times) in patterns[p])
            {
                trips.Add(trip);
                tripPattern.Add(p);
                tripService.Add(serviceIndex[trip.ServiceId]);
                tripTimeStart.Add(arrivals.Count);
                foreach (var st in times)
                {
                    arrivals.Add(st.Arrival.TotalSeconds);
                    departures.Add(st.Departure.TotalSeconds);
                    timepoints.Add(st.Timepoint);
                }
            }
        }
        patternStopStart[^1] = patternStops.Count;
        patternTripStart[^1] = trips.Count;

        var (stopPatternStart, stopPatterns) = Flatten(patternsByStop);
        var (transferStart, transfers) = Flatten(Transfers(stops));

        return new Timetable
        {
            Stops = stops,
            Routes = routes,
            Services = services,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(feed.Agencies[0].Timezone),
            PatternStopStart = patternStopStart,
            PatternStops = [.. patternStops],
            ShapePoints = [.. shapePoints],
            PatternStopShape = [.. patternStopShape],
            PatternTripStart = patternTripStart,
            PatternRoute = patternRoute,
            Trips = [.. trips],
            TripPattern = [.. tripPattern],
            TripService = [.. tripService],
            TripTimeStart = [.. tripTimeStart],
            Arrivals = [.. arrivals],
            Departures = [.. departures],
            Timepoints = [.. timepoints],
            StopPatternStart = stopPatternStart,
            StopPatterns = stopPatterns,
            TransferStart = transferStart,
            TransferTo = transfers.Select(t => t.To).ToArray(),
            TransferSeconds = transfers.Select(t => t.Seconds).ToArray(),
        };
    }

    /// <summary><paramref name="later"/> (xuất bến sau) tới trạm nào đó sớm hơn <paramref name="earlier"/>.</summary>
    private static bool Overtakes(StopTime[] earlier, StopTime[] later)
    {
        for (var i = 0; i < earlier.Length; i++)
        {
            if (later[i].Arrival.TotalSeconds < earlier[i].Arrival.TotalSeconds ||
                later[i].Departure.TotalSeconds < earlier[i].Departure.TotalSeconds) return true;
        }
        return false;
    }

    /// <summary>Cặp trạm cách nhau tối đa <see cref="Timetable.MaxTransferMeters"/>, quét theo vĩ độ đã sắp xếp.</summary>
    private static List<(int To, int Seconds)>[] Transfers(Stop[] stops)
    {
        const double MetersPerDegreeLat = 111_320;
        var result = Enumerable.Range(0, stops.Length).Select(_ => new List<(int To, int Seconds)>()).ToArray();
        var byLat = Enumerable.Range(0, stops.Length).OrderBy(s => stops[s].Lat).ToArray();
        for (var i = 0; i < byLat.Length; i++)
        {
            var a = byLat[i];
            for (var j = i + 1; j < byLat.Length && (stops[byLat[j]].Lat - stops[a].Lat) * MetersPerDegreeLat <= Timetable.MaxTransferMeters; j++)
            {
                var b = byLat[j];
                var d = Geo.DistanceMeters(stops[a].Lat, stops[a].Lon, stops[b].Lat, stops[b].Lon);
                if (d > Timetable.MaxTransferMeters) continue;
                var seconds = Walking.Seconds(d);
                result[a].Add((b, seconds));
                result[b].Add((a, seconds));
            }
        }
        return result;
    }

    private static (int[] Start, T[] Items) Flatten<T>(List<T>[] lists)
    {
        var start = new int[lists.Length + 1];
        for (var i = 0; i < lists.Length; i++) start[i + 1] = start[i] + lists[i].Count;
        return (start, lists.SelectMany(l => l).ToArray());
    }

    private static Dictionary<string, int> Index<T>(T[] items, Func<T, string> id) =>
        items.Select((item, i) => (Id: id(item), i)).ToDictionary(x => x.Id, x => x.i);
}
