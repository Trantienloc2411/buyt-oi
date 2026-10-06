using BuytOi.Catalog;
using BuytOi.Gtfs;
using BuytOi.Ingestion;

namespace BuytOi.Catalog.Tests;

public sealed class CatalogSnapshotTests
{
    private static readonly CatalogSnapshot Catalog = CatalogSnapshot.From(
        CrawlToGtfs.Convert(Path.Combine(AppContext.BaseDirectory, "samples"), new DateOnly(2027, 9, 30)));

    [Fact]
    public void Danh_sach_tuyen_va_tram_lay_tu_feed()
    {
        Assert.Equal(["01", "127", "155", "172", "MRT1"], Catalog.Routes.Select(r => r.ShortName).Order());
        Assert.Contains(Catalog.Stops, s => s.Code == "BX 01-1" && s.Name == "Điểm đầu cuối Nguyễn Siêu");
    }

    [Fact]
    public void Chi_tiet_tuyen_co_hai_huong_voi_tram_theo_thu_tu()
    {
        var r01 = Catalog.Route("1")!;

        Assert.Equal("Công ty Cổ phần Xe khách Phương Trang FutaBusLines", r01.AgencyName);
        Assert.Equal([0, 1], r01.Directions.Select(d => d.DirectionId));
        Assert.Equal("Bến xe buýt Chợ Lớn", r01.Directions[0].Headsign);
        Assert.Equal("Điểm đầu cuối Nguyễn Siêu", r01.Directions[0].Stops[0].Name);
        Assert.Equal(30, r01.Directions[0].Stops.Count); // lượt đi tuyến 01 có 30 trạm
    }

    [Fact]
    public void Tram_gan_sap_theo_khoang_cach_va_trong_ban_kinh()
    {
        // Đứng đúng tại trạm 9742 (Điểm đầu cuối Nguyễn Siêu).
        var gan = Catalog.Nearby(10.779948, 106.7064, radiusMeters: 500, limit: 20);

        Assert.Equal("9742", gan[0].Id);
        Assert.Equal(0, gan[0].DistanceMeters);
        Assert.All(gan, s => Assert.InRange(s.DistanceMeters, 0, 500));
        Assert.Equal(gan.Select(s => s.DistanceMeters).Order(), gan.Select(s => s.DistanceMeters));
        Assert.True(gan.Count > 1);
    }

    [Fact]
    public void Tram_gan_ton_trong_gioi_han_va_tra_rong_khi_xa()
    {
        Assert.Single(Catalog.Nearby(10.779948, 106.7064, radiusMeters: 500, limit: 1));
        Assert.Empty(Catalog.Nearby(21.0285, 105.8542, radiusMeters: 5000, limit: 20)); // Hà Nội
    }

    [Fact]
    public void Tuyen_khong_ton_tai_tra_ve_null()
    {
        Assert.Null(Catalog.Route("khong-co"));
    }
}
