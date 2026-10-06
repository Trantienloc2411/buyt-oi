namespace BuytOi.Routing.Contracts;

// Giờ trong các kiểu dưới đây là giờ địa phương của feed (Asia/Ho_Chi_Minh), không kèm múi giờ.

/// <param name="DepartAt">Giờ muốn khởi hành (giờ địa phương).</param>
public sealed record JourneyQuery(double FromLat, double FromLon, double ToLat, double ToLon, DateTime DepartAt);

public enum LegMode { Walk, Transit }

/// <param name="StopId">null khi là điểm đi/điểm đến người dùng chọn, không phải trạm.</param>
public sealed record LegPlace(string? StopId, string Name, double Lat, double Lon);

/// <param name="Approximate">true khi giờ lên hoặc xuống là giờ nội suy (GTFS timepoint=0), không phải giờ chính xác.</param>
public sealed record JourneyLeg(
    LegMode Mode, LegPlace From, LegPlace To, DateTime Departure, DateTime Arrival, int DistanceMeters,
    string? RouteId = null, string? RouteShortName = null, string? RouteColor = null, string? Headsign = null,
    int? StopCount = null, bool Approximate = false);

/// <param name="Transfers">Số lần đổi tuyến (số chặng đi xe trừ 1).</param>
public sealed record Journey(DateTime Departure, DateTime Arrival, int Transfers, IReadOnlyList<JourneyLeg> Legs);

public interface IJourneyPlanner
{
    /// <summary>
    /// Các phương án Pareto theo (giờ đến, số lần đổi tuyến): ít đổi tuyến trước,
    /// phương án sau chỉ có mặt khi đến sớm hơn hẳn phương án trước.
    /// </summary>
    IReadOnlyList<Journey> Plan(JourneyQuery query);
}
