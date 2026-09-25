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
    public void Tuyen_khong_ton_tai_tra_ve_null()
    {
        Assert.Null(Catalog.Route("khong-co"));
    }
}
