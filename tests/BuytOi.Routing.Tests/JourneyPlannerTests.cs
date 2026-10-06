using BuytOi.Gtfs;
using BuytOi.Ingestion.Infrastructure;
using BuytOi.Routing.Application;
using BuytOi.Routing.Contracts;
using BuytOi.Routing.Domain;
using BuytOi.Routing.Infrastructure;

namespace BuytOi.Routing.Tests;

public sealed class JourneyPlannerTests
{
    // Các trạm cách nhau ~5,5 km (quá xa để đi bộ), riêng B2 cách B ~170 m.
    private static readonly Stop[] Stops =
    [
        new("A", null, "Trạm A", 10.70, 106.60),
        new("B2", null, "Trạm B2", 10.7015, 106.65), // đứng trước B: hoà giờ thì nhãn đi bộ được xét trước
        new("B", null, "Trạm B", 10.70, 106.65),
        new("C", null, "Trạm C", 10.70, 106.70),
        new("D", null, "Trạm D", 10.70, 106.75),
    ];

    private static readonly DateOnly ThuTu = new(2026, 10, 7);
    private static readonly DateOnly ChuNhat = new(2026, 10, 11);

    private sealed record T(string Route, params (string Stop, int H, int M)[] Times)
    {
        public string Service { get; init; } = "ALL";
    }

    private static IJourneyPlanner Planner(params T[] trips)
    {
        var routes = trips.Select(t => t.Route).Distinct()
            .Select(r => new Route(r, "ag", r, $"Tuyến {r}", RouteType.Bus)).ToList();
        var gtfsTrips = trips.Select((t, i) => new Trip($"t{i}", t.Route, t.Service, 0, $"Về {t.Times[^1].Stop}")).ToList();
        var stopTimes = trips.SelectMany((t, i) => t.Times.Select((st, seq) =>
            new StopTime($"t{i}", seq + 1, st.Stop, GtfsTime.FromHm(st.H, st.M), GtfsTime.FromHm(st.H, st.M)))).ToList();
        var calendars = new List<Calendar>
        {
            new("ALL", (ServiceDays)127, new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)),
            new("WD", ServiceDays.Monday | ServiceDays.Tuesday | ServiceDays.Wednesday | ServiceDays.Thursday | ServiceDays.Friday,
                new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)),
        };
        var feed = new GtfsFeed([new Agency("ag", "Agency", "https://example.com", "Asia/Ho_Chi_Minh")],
            routes, Stops, gtfsTrips, stopTimes, calendars, []);
        return Planner(feed);
    }

    private sealed class FixedSource(Timetable timetable) : ITimetableSource
    {
        public Timetable Current => timetable;
    }

    private static JourneyPlanner Planner(GtfsFeed feed) =>
        new(new FixedSource(GtfsTimetableBuilder.Build(feed)), TimeProvider.System);

    private static JourneyQuery Query(string from, string to, int h, int m, DateOnly? date = null)
    {
        var a = Stops.Single(s => s.Id == from);
        var b = Stops.Single(s => s.Id == to);
        return new JourneyQuery(a.Lat, a.Lon, b.Lat, b.Lon, (date ?? ThuTu).ToDateTime(new TimeOnly(h, m)));
    }

    private static DateTime At(int h, int m) => ThuTu.ToDateTime(new TimeOnly(h, m));

    private static IEnumerable<JourneyLeg> Rides(Journey j) => j.Legs.Where(l => l.Mode == LegMode.Transit);

    [Fact]
    public void Di_thang_mot_tuyen_bat_chuyen_som_nhat_con_kip()
    {
        var planner = Planner(
            new T("R1", ("A", 7, 40), ("B", 7, 50), ("C", 8, 0)),
            new T("R1", ("A", 8, 0), ("B", 8, 10), ("C", 8, 20)),
            new T("R1", ("A", 8, 20), ("B", 8, 30), ("C", 8, 40)));

        var j = Assert.Single(planner.Plan(Query("A", "C", 7, 50)));

        Assert.Equal(0, j.Transfers);
        Assert.Equal([LegMode.Walk, LegMode.Transit, LegMode.Walk], j.Legs.Select(l => l.Mode));
        var ride = Rides(j).Single();
        Assert.Equal(("R1", "A", "C", 2), (ride.RouteShortName, ride.From.StopId, ride.To.StopId, ride.StopCount));
        Assert.Equal((At(8, 0), At(8, 20)), (j.Departure, j.Arrival));
        Assert.Equal("Về C", ride.Headsign);
        Assert.Equal(["A", "B", "C"], ride.Stops!.Select(s => s.StopId));
    }

    [Fact]
    public void Tra_ve_phuong_an_it_doi_tuyen_va_phuong_an_den_som_hon()
    {
        var planner = Planner(
            new T("R1", ("A", 8, 0), ("B", 8, 10)),
            new T("R2", ("B", 8, 15), ("D", 8, 30)),
            new T("R3", ("A", 8, 5), ("B", 8, 20), ("C", 9, 0), ("D", 9, 30)));

        var journeys = planner.Plan(Query("A", "D", 7, 55));

        Assert.Equal([(0, At(9, 30)), (1, At(8, 30))], journeys.Select(j => (j.Transfers, j.Arrival)));
        Assert.Equal(["R1", "R2"], Rides(journeys[1]).Select(l => l.RouteShortName));
    }

    [Fact]
    public void Doi_tuyen_tai_cung_tram_can_it_nhat_mot_phut()
    {
        var planner = Planner(
            new T("R1", ("A", 8, 0), ("B", 8, 10)),
            new T("R2", ("B", 8, 10), ("D", 8, 25)),
            new T("R2", ("B", 8, 30), ("D", 8, 45)));

        var j = Assert.Single(planner.Plan(Query("A", "D", 7, 55)));

        Assert.Equal(At(8, 45), j.Arrival);
    }

    [Fact]
    public void Di_bo_sang_tram_gan_de_doi_tuyen()
    {
        var planner = Planner(
            new T("R1", ("A", 8, 0), ("B", 8, 10)),
            new T("R2", ("B2", 8, 20), ("D", 8, 40)));

        var j = Assert.Single(planner.Plan(Query("A", "D", 7, 55)));

        Assert.Equal(1, j.Transfers);
        Assert.Equal([LegMode.Walk, LegMode.Transit, LegMode.Walk, LegMode.Transit, LegMode.Walk], j.Legs.Select(l => l.Mode));
        var walk = j.Legs[2];
        Assert.Equal(("B", "B2"), (walk.From.StopId, walk.To.StopId));
        Assert.InRange(walk.DistanceMeters, 200, 250); // ~167 m chim bay × 1,3
        Assert.Equal(At(8, 40), j.Arrival);
    }

    [Fact]
    public void Khong_noi_hai_chang_di_bo_lien_nhau()
    {
        // Đích ở B2: xuống B rồi đi bộ sang B2, hay xuống B rồi đi thẳng tới đích — hoà giờ, phải ra một chặng đi bộ.
        var planner = Planner(new T("R1", ("A", 8, 0), ("B", 8, 10)));

        var j = Assert.Single(planner.Plan(Query("A", "B2", 7, 55)));

        Assert.Equal([LegMode.Walk, LegMode.Transit, LegMode.Walk], j.Legs.Select(l => l.Mode));
        Assert.Equal(("B", null), (j.Legs[2].From.StopId, j.Legs[2].To.StopId));
        Assert.Equal(j.Legs[2].Arrival, j.Arrival);
    }

    [Fact]
    public void Chuyen_xuat_ben_sau_nhung_den_som_hon_van_duoc_chon()
    {
        // Cùng tuyến, cùng dãy trạm nhưng chuyến 08:10 vượt chuyến 08:00 → phải tách pattern.
        var planner = Planner(
            new T("R1", ("A", 8, 0), ("C", 9, 0)),
            new T("R1", ("A", 8, 10), ("C", 8, 30)));

        var j = Assert.Single(planner.Plan(Query("A", "C", 7, 55)));

        Assert.Equal((At(8, 10), At(8, 30)), (j.Departure, j.Arrival));
    }

    [Fact]
    public void Khong_dung_chuyen_khong_chay_trong_ngay()
    {
        var planner = Planner(new T("R1", ("A", 8, 0), ("C", 8, 30)) { Service = "WD" });

        Assert.Single(planner.Plan(Query("A", "C", 7, 55)));
        Assert.Empty(planner.Plan(Query("A", "C", 7, 55, ChuNhat)));
    }

    [Fact]
    public void Tuyen_vong_di_qua_tram_dau_hai_lan()
    {
        var planner = Planner(new T("R1", ("A", 8, 0), ("B", 8, 10), ("C", 8, 20), ("A", 8, 30)));

        var j = Assert.Single(planner.Plan(Query("C", "A", 8, 15)));

        Assert.Equal(At(8, 30), j.Arrival);
        Assert.Equal(1, Rides(j).Single().StopCount);
        Assert.Equal(["C", "A"], Rides(j).Single().Stops!.Select(s => s.StopId));
    }

    [Fact]
    public void Gan_thi_goi_y_di_bo()
    {
        var planner = Planner(new T("R1", ("A", 8, 0), ("C", 8, 30)));
        var a = Stops[0];

        var j = Assert.Single(planner.Plan(new JourneyQuery(a.Lat, a.Lon, a.Lat + 0.003, a.Lon, At(8, 0))));

        var walk = Assert.Single(j.Legs);
        Assert.Equal(LegMode.Walk, walk.Mode);
        Assert.Equal(At(8, 0), j.Departure);
        Assert.InRange(j.Arrival, At(8, 5), At(8, 7)); // ~334 m × 1,3 ở 1,2 m/s ≈ 6 phút
    }

    [Fact]
    public void Tim_duong_tren_feed_mau()
    {
        var feed = CrawlToGtfs.Convert(Path.Combine(AppContext.BaseDirectory, "samples"), new DateOnly(2027, 9, 30));
        var planner = Planner(feed);
        // Lượt đi tuyến 01: từ trạm đầu tới một trạm giữa (giờ nội suy) của chuyến dài nhất.
        var trip = feed.Trips.Where(t => t.RouteId == "1" && t.DirectionId == 0)
            .MaxBy(t => feed.StopTimes.Count(st => st.TripId == t.Id))!;
        var times = feed.StopTimes.Where(st => st.TripId == trip.Id).OrderBy(st => st.Sequence).ToList();
        var stop = feed.Stops.ToDictionary(s => s.Id);
        var (from, to) = (stop[times[0].StopId], stop[times[times.Count / 2].StopId]);
        var date = Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 10, 5).AddDays(i))
            .First(d => feed.Calendars.Single(c => c.ServiceId == trip.ServiceId).Days
                .HasFlag((ServiceDays)(1 << (((int)d.DayOfWeek + 6) % 7))));

        var journeys = planner.Plan(new JourneyQuery(from.Lat, from.Lon, to.Lat, to.Lon, date.ToDateTime(new TimeOnly(7, 0))));

        var j = journeys[0];
        Assert.True(j.Departure >= date.ToDateTime(new TimeOnly(7, 0)));
        Assert.True(j.Arrival > j.Departure);
        Assert.All(j.Legs.Zip(j.Legs.Skip(1)), p => Assert.True(p.First.Arrival <= p.Second.Departure));
        Assert.Contains(Rides(j), l => l.Approximate); // trạm giữa có giờ nội suy
    }
}
