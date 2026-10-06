using System.ComponentModel.DataAnnotations;

namespace BuytOi.Gtfs;

// Struct để mảng ~1 triệu điểm shape không tốn object; [Required] để OpenAPI sinh `lat`/`lon` bắt buộc
// (struct luôn có constructor rỗng nên mặc định bị coi là tuỳ chọn).
public readonly record struct GeoPoint([property: Required] double Lat, [property: Required] double Lon);

public static class Geo
{
    /// <summary>Khoảng cách đường chim bay (haversine), đơn vị mét.</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        static double Rad(double deg) => deg * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lon2 - lon1) / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    public static double DistanceMeters(GeoPoint a, GeoPoint b) => DistanceMeters(a.Lat, a.Lon, b.Lat, b.Lon);
}

public static class ShapeMatching
{
    /// <summary>
    /// Trạm xa shape hơn ngưỡng này → coi như map-match hỏng (vd. pfaedle không tìm được đường, shape chỉ 2 điểm),
    /// bỏ shape. Shape tốt từ pfaedle cho feed TP.HCM: trạm xa nhất ~200 m (trạm nằm trong hẻm/bến xe).
    /// </summary>
    public const double MaxStopDistanceMeters = 300;

    /// <summary><see cref="StopPositions"/>, hoặc null khi có trạm xa shape hơn <see cref="MaxStopDistanceMeters"/>.</summary>
    public static int[]? TryStopPositions(IReadOnlyList<GeoPoint> shape, IReadOnlyList<GeoPoint> stops)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(stops);
        if (shape.Count == 0) return null;
        var positions = StopPositions(shape, stops);
        return positions.Select((p, i) => Geo.DistanceMeters(stops[i], shape[p])).All(d => d <= MaxStopDistanceMeters)
            ? positions
            : null;
    }

    /// <summary>
    /// Vị trí (chỉ số điểm trên <paramref name="shape"/>) của từng trạm theo thứ tự chạy: không giảm và có
    /// tổng khoảng cách trạm–điểm nhỏ nhất. Nhờ ràng buộc không giảm, tuyến đi rồi quay lại trên cùng con đường
    /// (hoặc tuyến vòng, trạm đầu = trạm cuối) vẫn gán trạm lượt về vào phần lượt về của shape.
    /// </summary>
    /// <remarks>Quy hoạch động O(số trạm × số điểm): ~40 trạm × vài nghìn điểm cho mỗi lượt tuyến.</remarks>
    public static int[] StopPositions(IReadOnlyList<GeoPoint> shape, IReadOnlyList<GeoPoint> stops)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(stops);
        if (shape.Count == 0) throw new ArgumentException("Shape rỗng", nameof(shape));
        if (stops.Count == 0) return [];

        int n = stops.Count, m = shape.Count;
        // back[i][j]: vị trí tốt nhất của trạm i-1 khi trạm i ở điểm j.
        var back = new int[n][];
        var cost = new double[m];
        for (var j = 0; j < m; j++) cost[j] = Geo.DistanceMeters(stops[0], shape[j]);

        for (var i = 1; i < n; i++)
        {
            back[i] = new int[m];
            var next = new double[m];
            var best = 0; // argmin cost[0..j]
            for (var j = 0; j < m; j++)
            {
                if (cost[j] < cost[best]) best = j;
                back[i][j] = best;
                next[j] = cost[best] + Geo.DistanceMeters(stops[i], shape[j]);
            }
            cost = next;
        }

        var positions = new int[n];
        positions[n - 1] = Array.IndexOf(cost, cost.Min());
        for (var i = n - 1; i > 0; i--) positions[i - 1] = back[i][positions[i]];
        return positions;
    }
}
