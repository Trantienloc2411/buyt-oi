using BuytOi.Gtfs;

namespace BuytOi.Gtfs.Tests;

public sealed class ShapeMatchingTests
{
    private static GeoPoint P(double x, double y = 0) => new(10.7 + y * 0.001, 106.6 + x * 0.001);

    [Fact]
    public void Tram_gan_diem_gan_nhat_tren_shape()
    {
        var shape = Enumerable.Range(0, 11).Select(x => P(x)).ToList();

        Assert.Equal([0, 3, 7, 10], ShapeMatching.StopPositions(shape, [P(0), P(3.2, 0.1), P(6.8), P(10)]));
    }

    [Fact]
    public void Tuyen_di_roi_quay_lai_cung_duong_gan_tram_luot_ve_vao_phan_luot_ve()
    {
        // Đi về hướng đông (y = 0) tới x = 10 rồi quay lại phía bên kia đường (y = 0,05).
        var shape = Enumerable.Range(0, 11).Select(x => P(x))
            .Concat(Enumerable.Range(0, 11).Select(x => P(10 - x, 0.05)))
            .ToList();
        // Trạm lượt về ở x = 8 và x = 2 nằm gần cả phần lượt đi lẫn lượt về.
        var stops = new[] { P(2), P(8), P(8, 0.05), P(2, 0.05) };

        Assert.Equal([2, 8, 13, 19], ShapeMatching.StopPositions(shape, stops));
    }

    [Fact]
    public void Shape_xa_tram_bi_bo_qua()
    {
        // Shape chỉ 2 điểm ở hai đầu: trạm giữa cách shape ~550 m.
        var shape = new[] { P(0), P(10) };

        Assert.Null(ShapeMatching.TryStopPositions(shape, [P(0), P(5, 5), P(10)]));
        Assert.NotNull(ShapeMatching.TryStopPositions(shape, [P(0), P(10)]));
        Assert.Null(ShapeMatching.TryStopPositions([], [P(0)]));
    }

    [Fact]
    public void Tuyen_vong_tram_cuoi_trung_tram_dau()
    {
        var shape = new[] { P(0), P(5), P(5, 5), P(0, 5), P(0) };

        Assert.Equal([0, 2, 4], ShapeMatching.StopPositions(shape, [P(0), P(5, 5), P(0)]));
    }
}
