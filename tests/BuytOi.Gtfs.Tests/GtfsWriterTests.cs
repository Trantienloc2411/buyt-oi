using BuytOi.Gtfs;

namespace BuytOi.Gtfs.Tests;

public sealed class GtfsWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "buytoi-gtfs-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static GtfsFeed MauFeed(IReadOnlyList<ShapePoint>? shapes = null) => new(
        [new Agency("A1", "Công ty \"Phương Trang\", FUTA", "https://example.org", "Asia/Ho_Chi_Minh", "1900638494")],
        [new Route("172", "A1", "172", "Bến xe Miền Tây - KCN", RouteType.Bus, "FFB07C")],
        [
            new Stop("9742", "BX 01-1", "Điểm đầu cuối Nguyễn Siêu", 10.779948, 106.7064, WheelchairBoarding.Accessible),
            new Stop("1192", null, "Ngô Văn Năm", 10.779807, 106.707609),
        ],
        [new Trip("172_5", "172", "S1", 0)],
        [
            new StopTime("172_5", 1, "9742", GtfsTime.FromHm(23, 45), GtfsTime.FromHm(23, 45)),
            new StopTime("172_5", 2, "1192", GtfsTime.FromHm(24, 30), GtfsTime.FromHm(24, 30), Timepoint: false),
        ],
        [new Calendar("S1", ServiceDays.Monday | ServiceDays.Sunday, new DateOnly(2026, 2, 22), new DateOnly(2026, 12, 31))],
        shapes ?? []);

    private string[] Doc(string file) => File.ReadAllLines(Path.Combine(_dir, file));

    [Fact]
    public void Ghi_feed_sinh_dung_cac_file_va_header()
    {
        GtfsWriter.Write(MauFeed(), _dir);

        Assert.Equal(
            ["agency.txt", "calendar.txt", "routes.txt", "stop_times.txt", "stops.txt", "trips.txt"],
            Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
        Assert.Equal("stop_id,stop_code,stop_name,stop_lat,stop_lon,wheelchair_boarding", Doc("stops.txt")[0]);
    }

    [Fact]
    public void Gio_qua_nua_dem_ghi_vuot_24h()
    {
        GtfsWriter.Write(MauFeed(), _dir);

        Assert.Equal("172_5,2,1192,24:30:00,24:30:00,0", Doc("stop_times.txt")[2]);
    }

    [Fact]
    public void Chuoi_co_dau_phay_va_ngoac_kep_duoc_escape()
    {
        GtfsWriter.Write(MauFeed(), _dir);

        Assert.Equal(
            "A1,\"Công ty \"\"Phương Trang\"\", FUTA\",https://example.org,Asia/Ho_Chi_Minh,1900638494",
            Doc("agency.txt")[1]);
    }

    [Fact]
    public void Toa_do_va_ngay_dung_dinh_dang_bat_ke_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("vi-VN");
        try
        {
            GtfsWriter.Write(MauFeed(), _dir);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }

        Assert.Equal("9742,BX 01-1,Điểm đầu cuối Nguyễn Siêu,10.779948,106.7064,1", Doc("stops.txt")[1]);
        Assert.Equal("1192,,Ngô Văn Năm,10.779807,106.707609,0", Doc("stops.txt")[2]);
        Assert.Equal("S1,1,0,0,0,0,0,1,20260222,20261231", Doc("calendar.txt")[1]);
    }

    [Fact]
    public void Co_shape_thi_ghi_shapes_txt()
    {
        GtfsWriter.Write(MauFeed([new ShapePoint("SH1", 1, 10.5, 106.5)]), _dir);

        Assert.Equal("SH1,1,10.5,106.5", Doc("shapes.txt")[1]);
    }
}
