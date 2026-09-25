using BuytOi.Gtfs;
using BuytOi.Ingestion;

namespace BuytOi.Ingestion.Tests;

// Dữ liệu: data/samples — tuyến 01 (thường), 155 (vòng), 172 (qua nửa đêm), 127 (lịch chồng nhau), MRT1 (metro).
public sealed class CrawlToGtfsTests
{
    private static readonly DateOnly MacDinhKetThuc = new(2027, 9, 30);
    private static readonly GtfsFeed Feed = CrawlToGtfs.Convert(Path.Combine(AppContext.BaseDirectory, "samples"), MacDinhKetThuc);

    private static List<StopTime> StopTimesOf(string tripId) => Feed.StopTimes.Where(st => st.TripId == tripId).ToList();

    [Fact]
    public void Tham_chieu_giua_cac_bang_deu_ton_tai()
    {
        var agencyIds = Feed.Agencies.Select(a => a.Id).ToHashSet();
        var routeIds = Feed.Routes.Select(r => r.Id).ToHashSet();
        var stopIds = Feed.Stops.Select(s => s.Id).ToHashSet();
        var tripIds = Feed.Trips.Select(t => t.Id).ToHashSet();
        var serviceIds = Feed.Calendars.Select(c => c.ServiceId).ToHashSet();

        Assert.Equal(5, Feed.Routes.Count);
        Assert.All(Feed.Routes, r => Assert.Contains(r.AgencyId, agencyIds));
        Assert.All(Feed.Trips, t => { Assert.Contains(t.RouteId, routeIds); Assert.Contains(t.ServiceId, serviceIds); });
        Assert.All(Feed.StopTimes, st => { Assert.Contains(st.TripId, tripIds); Assert.Contains(st.StopId, stopIds); });
        Assert.Equal(tripIds.Count, Feed.Trips.Count); // trip_id duy nhất (đã ghép RouteId)
        Assert.Equal(tripIds, Feed.StopTimes.Select(st => st.TripId).ToHashSet()); // mọi chuyến đều có stop_times
        Assert.Equal(stopIds, Feed.StopTimes.Select(st => st.StopId).ToHashSet()); // không có trạm thừa
    }

    [Fact]
    public void Gio_tai_tram_tang_dan_va_khop_gio_dau_cuoi()
    {
        foreach (var trip in Feed.Trips)
        {
            var sts = StopTimesOf(trip.Id);
            Assert.Equal(Enumerable.Range(1, sts.Count), sts.Select(st => st.Sequence));
            Assert.True(sts.Zip(sts.Skip(1)).All(p => p.First.Arrival.TotalSeconds <= p.Second.Arrival.TotalSeconds), trip.Id);
            Assert.True(sts[0].Timepoint && sts[^1].Timepoint);
            Assert.All(sts.Skip(1).SkipLast(1), st => Assert.False(st.Timepoint));
        }

        // Tuyến 01, chuyến đầu tiên: 05:00 → 05:40.
        var dau = StopTimesOf("1_2");
        Assert.Equal("05:00:00", dau[0].Departure.ToString());
        Assert.Equal("05:40:00", dau[^1].Arrival.ToString());
    }

    [Fact]
    public void Chuyen_qua_nua_dem_co_gio_vuot_24h()
    {
        var qua = Feed.Trips.Where(t => t.RouteId == "404")
            .Select(t => StopTimesOf(t.Id))
            .Where(sts => sts[^1].Arrival.TotalSeconds >= 24 * 3600)
            .ToList();

        Assert.NotEmpty(qua);
        Assert.Contains(qua, sts => sts[^1].Arrival.ToString() == "24:30:00");
    }

    [Fact]
    public void Tuyen_vong_giu_tram_dau_trung_tram_cuoi()
    {
        var sts = StopTimesOf(Feed.Trips.First(t => t.RouteId == "345").Id);

        Assert.Equal(sts[0].StopId, sts[^1].StopId);
    }

    [Fact]
    public void Lich_cu_ket_thuc_truoc_khi_lich_moi_bat_dau()
    {
        var lich127 = Feed.Trips.Where(t => t.RouteId == "178").Select(t => t.ServiceId).Distinct()
            .Select(id => Feed.Calendars.Single(c => c.ServiceId == id))
            .OrderBy(c => c.StartDate)
            .ToList();

        Assert.Equal(2, lich127.Count);
        Assert.Equal(new DateOnly(2026, 3, 10), lich127[0].EndDate);
        Assert.Equal(new DateOnly(2026, 3, 11), lich127[1].StartDate);
        Assert.Equal(MacDinhKetThuc, lich127[1].EndDate);
    }

    [Fact]
    public void Lich_chia_theo_ngay_trong_tuan_khong_bi_cat()
    {
        var metro = Feed.Trips.Where(t => t.RouteId == "384").Select(t => t.ServiceId).Distinct()
            .Select(id => Feed.Calendars.Single(c => c.ServiceId == id))
            .ToList();

        Assert.Equal(
            [ServiceDays.Monday | ServiceDays.Tuesday | ServiceDays.Wednesday | ServiceDays.Thursday, ServiceDays.Friday,
             ServiceDays.Saturday | ServiceDays.Sunday],
            metro.Select(c => c.Days).Order());
        Assert.All(metro, c => Assert.Equal(MacDinhKetThuc, c.EndDate));
    }

    [Fact]
    public void Route_lay_mau_loai_va_don_vi_van_hanh()
    {
        var r01 = Feed.Routes.Single(r => r.ShortName == "01");
        var metro = Feed.Routes.Single(r => r.ShortName == "MRT1");

        Assert.Equal(RouteType.Bus, r01.Type);
        Assert.Equal("FFB07C", r01.Color);
        Assert.Equal(RouteType.Subway, metro.Type);

        var futa = Feed.Agencies.Single(a => a.Id == r01.AgencyId);
        Assert.Equal("Công ty Cổ phần Xe khách Phương Trang FutaBusLines", futa.Name);
        Assert.Equal("1900638494", futa.Phone);
        Assert.Equal(3, Feed.Agencies.Count); // FUTA dùng chung cho 01, 155, 172
    }
}
