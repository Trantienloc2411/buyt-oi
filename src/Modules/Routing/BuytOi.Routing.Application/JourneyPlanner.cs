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

        var journeys = raw.Select(j => new Journey(At(j.Legs[0].Departure), At(j.Legs[^1].Arrival), j.Transfers,
            j.Legs.Select(leg => ToLeg(tt, leg, At, origin, destination)).ToList())).ToList();

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
        var distance = 0.0;
        for (var i = leg.BoardPosition; i < leg.AlightPosition; i++)
        {
            var a = tt.Stops[tt.PatternStop(tt.TripPattern[leg.Trip], i)];
            var b = tt.Stops[tt.PatternStop(tt.TripPattern[leg.Trip], i + 1)];
            distance += Geo.DistanceMeters(a.Lat, a.Lon, b.Lat, b.Lon);
        }
        return new JourneyLeg(LegMode.Transit, from, to, at(leg.Departure), at(leg.Arrival), (int)Math.Round(distance),
            route.Id, route.ShortName, route.Color, trip.Headsign, leg.AlightPosition - leg.BoardPosition,
            Approximate: !tt.Timepoints[times + leg.BoardPosition] || !tt.Timepoints[times + leg.AlightPosition]);
    }

    private static LegPlace Place(Timetable tt, int stop) =>
        new(tt.Stops[stop].Id, tt.Stops[stop].Name, tt.Stops[stop].Lat, tt.Stops[stop].Lon);

    private static void CheckCoordinate(Dictionary<string, string[]> errors, string name, double value, double max)
    {
        // NaN/Infinity parse được thành double và lọt qua so sánh khoảng → chặn riêng.
        if (!double.IsFinite(value) || value < -max || value > max) errors[name] = [$"Phải trong khoảng -{max}..{max}."];
    }
}
