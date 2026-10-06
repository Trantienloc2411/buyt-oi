using BuytOi.Gtfs;
using BuytOi.Routing.Contracts;
using BuytOi.Routing.Domain;

namespace BuytOi.Routing.Application;

/// <summary>Port: lịch chạy hiện hành (Infrastructure cài đặt; sau này hoán đổi khi có FeedPublished).</summary>
public interface ITimetableSource
{
    Timetable Current { get; }
}

/// <summary>Use case tìm đường: điểm đi/đến → trạm gần → RAPTOR → hành trình theo Contracts.</summary>
public sealed class JourneyPlanner(ITimetableSource source, TimeProvider time) : IJourneyPlanner
{
    /// <summary>Quãng đường chim bay tối đa đi bộ từ điểm đi tới trạm (và từ trạm tới điểm đến).</summary>
    public const double MaxAccessMeters = 800;

    /// <summary>
    /// Giờ khởi hành theo giờ địa phương của feed: không truyền → bây giờ; không có múi giờ → giữ nguyên;
    /// có múi giờ (vd. "Z") → đổi sang giờ địa phương.
    /// </summary>
    public DateTime LocalDeparture(DateTime? departAt) => departAt switch
    {
        null => TimeZoneInfo.ConvertTime(time.GetUtcNow(), source.Current.TimeZone).DateTime,
        { Kind: DateTimeKind.Unspecified } d => d,
        { } d => TimeZoneInfo.ConvertTime(d, source.Current.TimeZone),
    };

    /// <returns>Lỗi theo tên tham số; rỗng khi hợp lệ.</returns>
    public static Dictionary<string, string[]> Validate(double fromLat, double fromLon, double toLat, double toLon)
    {
        var errors = new Dictionary<string, string[]>();
        CheckCoordinate(errors, nameof(fromLat), fromLat, 90);
        CheckCoordinate(errors, nameof(fromLon), fromLon, 180);
        CheckCoordinate(errors, nameof(toLat), toLat, 90);
        CheckCoordinate(errors, nameof(toLon), toLon, 180);
        return errors;
    }

    public IReadOnlyList<Journey> Plan(JourneyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tt = source.Current;

        var date = DateOnly.FromDateTime(query.DepartAt);
        var serviceDay = date.ToDateTime(TimeOnly.MinValue);
        DateTime At(int seconds) => serviceDay.AddSeconds(seconds);
        var origin = new LegPlace(null, "Điểm đi", query.FromLat, query.FromLon);
        var destination = new LegPlace(null, "Điểm đến", query.ToLat, query.ToLon);

        // ponytail: chỉ xét chuyến của ngày phục vụ hiện tại; chuyến qua nửa đêm của hôm trước (giờ ≥ 24:00) bị bỏ qua.
        var raw = new Raptor(tt).Search(
            tt.StopsNear(query.FromLat, query.FromLon, MaxAccessMeters),
            tt.StopsNear(query.ToLat, query.ToLon, MaxAccessMeters),
            (int)(query.DepartAt - serviceDay).TotalSeconds,
            tt.ActiveServices(date));

        var journeys = raw.Select(j => MergeWalks(j.Legs.Select(leg => ToLeg(tt, leg, At, origin, destination))))
            .Select((legs, i) => new Journey(legs[0].Departure, legs[^1].Arrival, raw[i].Transfers, legs))
            .ToList();

        var direct = Geo.DistanceMeters(query.FromLat, query.FromLon, query.ToLat, query.ToLon);
        if (direct <= MaxAccessMeters)
        {
            // Đi bộ luôn là phương án ít chuyến xe nhất; chỉ giữ phương án đi xe đến sớm hơn đi bộ.
            var arrival = query.DepartAt.AddSeconds(Walking.Seconds(direct));
            journeys.RemoveAll(j => j.Arrival >= arrival);
            journeys.Insert(0, new Journey(query.DepartAt, arrival, 0,
                [new JourneyLeg(LegMode.Walk, origin, destination, query.DepartAt, arrival, Walking.Meters(direct))]));
        }
        return journeys;
    }

    private static JourneyLeg ToLeg(Timetable tt, RawLeg leg, Func<int, DateTime> at, LegPlace origin, LegPlace destination)
    {
        var from = leg.FromStop < 0 ? origin : Place(tt, leg.FromStop);
        var to = leg.ToStop < 0 ? destination : Place(tt, leg.ToStop);
        if (leg.Kind != RawLegKind.Transit)
        {
            var meters = leg.Kind == RawLegKind.Transfer
                ? Walking.Meters(Geo.DistanceMeters(from.Lat, from.Lon, to.Lat, to.Lon))
                : leg.Meters;
            return new JourneyLeg(LegMode.Walk, from, to, at(leg.Departure), at(leg.Arrival), meters);
        }

        var trip = tt.Trips[leg.Trip];
        var route = tt.Routes[tt.PatternRoute[tt.TripPattern[leg.Trip]]];
        var times = tt.TripTimeStart[leg.Trip];
        var stops = Enumerable.Range(leg.BoardPosition, leg.AlightPosition - leg.BoardPosition + 1)
            .Select(i => tt.PatternStop(tt.TripPattern[leg.Trip], i))
            .ToList();
        var distance = stops.Zip(stops.Skip(1)).Sum(p =>
            Geo.DistanceMeters(tt.Stops[p.First].Lat, tt.Stops[p.First].Lon, tt.Stops[p.Second].Lat, tt.Stops[p.Second].Lon));
        return new JourneyLeg(LegMode.Transit, from, to, at(leg.Departure), at(leg.Arrival), (int)Math.Round(distance),
            route.Id, route.ShortName, route.Color, trip.Headsign, leg.AlightPosition - leg.BoardPosition,
            Approximate: !tt.Timepoints[times + leg.BoardPosition] || !tt.Timepoints[times + leg.AlightPosition],
            Stops: stops.Select(s => Place(tt, s)).ToList(),
            Path: tt.Path(tt.TripPattern[leg.Trip], leg.BoardPosition, leg.AlightPosition));
    }

    /// <summary>
    /// Bỏ chặng đi bộ 0 m; gộp các chặng đi bộ liền nhau (xuống xe → đi bộ đổi trạm → đi bộ tới đích, khi hoà giờ với đi thẳng)
    /// thành một chặng đi thẳng: đường chim bay không dài hơn tổng hai chặng nên giờ đến không muộn hơn.
    /// </summary>
    private static List<JourneyLeg> MergeWalks(IEnumerable<JourneyLeg> legs)
    {
        var result = new List<JourneyLeg>();
        // Điểm đi/đến trùng trạm → chặng đi bộ 0 m, bỏ.
        foreach (var leg in legs.Where(l => l.Mode != LegMode.Walk || l.DistanceMeters > 0))
        {
            if (leg.Mode == LegMode.Walk && result.Count > 0 && result[^1].Mode == LegMode.Walk)
            {
                var prev = result[^1];
                var d = Geo.DistanceMeters(prev.From.Lat, prev.From.Lon, leg.To.Lat, leg.To.Lon);
                result[^1] = prev with
                {
                    To = leg.To,
                    Arrival = prev.Departure.AddSeconds(Walking.Seconds(d)),
                    DistanceMeters = Walking.Meters(d),
                };
            }
            else
            {
                result.Add(leg);
            }
        }
        return result;
    }

    private static LegPlace Place(Timetable tt, int stop) =>
        new(tt.Stops[stop].Id, tt.Stops[stop].Name, tt.Stops[stop].Lat, tt.Stops[stop].Lon);

    private static void CheckCoordinate(Dictionary<string, string[]> errors, string name, double value, double max)
    {
        // NaN/Infinity parse được thành double và lọt qua so sánh khoảng → chặn riêng.
        if (!double.IsFinite(value) || value < -max || value > max) errors[name] = [$"Phải trong khoảng -{max}..{max}."];
    }
}
