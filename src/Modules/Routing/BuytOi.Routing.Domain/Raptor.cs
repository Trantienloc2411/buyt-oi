namespace BuytOi.Routing.Domain;

public enum RawLegKind { Access, Transit, Transfer, Egress }

/// <summary>Chặng thô theo chỉ số của <see cref="Timetable"/>; trạm -1 = điểm đi/đến của người dùng.</summary>
public readonly record struct RawLeg(RawLegKind Kind, int FromStop, int ToStop, int Departure, int Arrival,
    int Meters = 0, int Trip = -1, int BoardPosition = -1, int AlightPosition = -1);

public sealed record RawJourney(int Transfers, IReadOnlyList<RawLeg> Legs);

/// <summary>
/// RAPTOR (Delling và cộng sự, 2012): vòng k tìm giờ đến sớm nhất tại mỗi trạm khi đi tối đa k chuyến xe.
/// Mỗi lời gọi <see cref="Search"/> dùng mảng nhãn riêng nên an toàn khi chạy song song.
/// </summary>
public sealed class Raptor(Timetable tt)
{
    /// <summary>Số chuyến xe tối đa trong một hành trình (tối đa 3 lần đổi tuyến).</summary>
    public const int MaxRounds = 4;

    /// <summary>Thời gian tối thiểu để xuống xe rồi lên xe khác tại cùng trạm.</summary>
    public const int TransferSlackSeconds = 60;

    private const int Inf = int.MaxValue;

    private enum Label : byte { None, Access, Transit, Walk }

    public List<RawJourney> Search(IReadOnlyList<StopAccess> access, IReadOnlyList<StopAccess> egress,
        int departure, bool[] activeServices)
    {
        var n = tt.Stops.Length;
        // ponytail: cấp mảng mới mỗi truy vấn (~1 MB với 6 000 trạm); dùng ArrayPool khi tải cao.
        var arrival = new int[MaxRounds + 1][];   // giờ đến sớm nhất khi đi tối đa k chuyến
        var ready = new int[MaxRounds + 1][];     // giờ sẵn sàng lên xe = arrival + slack nếu vừa xuống xe
        var label = new Label[MaxRounds + 1][];   // cách tới trạm ở đúng vòng k (None = kế thừa vòng trước)
        var transitArrival = new int[MaxRounds + 1][];
        var transitTrip = new int[MaxRounds + 1][];
        var boardPosition = new int[MaxRounds + 1][];
        var alightPosition = new int[MaxRounds + 1][];
        var walkFrom = new int[MaxRounds + 1][];
        var bestArrival = Filled(n, Inf);
        var accessByStop = new Dictionary<int, StopAccess>();

        var marked = new bool[n];
        var markedStops = new List<int>();
        void Mark(int s)
        {
            if (marked[s]) return;
            marked[s] = true;
            markedStops.Add(s);
        }

        arrival[0] = Filled(n, Inf);
        label[0] = new Label[n];
        foreach (var a in access)
        {
            var t = departure + a.Seconds;
            if (t >= arrival[0][a.Stop]) continue;
            arrival[0][a.Stop] = bestArrival[a.Stop] = t;
            label[0][a.Stop] = Label.Access;
            accessByStop[a.Stop] = a;
            Mark(a.Stop);
        }
        ready[0] = (int[])arrival[0].Clone();

        var results = new List<RawJourney>();
        var bestTarget = Inf;
        var patternStart = Filled(tt.PatternRoute.Length, Inf);
        var queuedPatterns = new List<int>();

        for (var k = 1; k <= MaxRounds && markedStops.Count > 0; k++)
        {
            arrival[k] = (int[])arrival[k - 1].Clone();
            label[k] = new Label[n];
            transitArrival[k] = new int[n];
            transitTrip[k] = new int[n];
            boardPosition[k] = new int[n];
            alightPosition[k] = new int[n];
            walkFrom[k] = new int[n];

            // Mỗi pattern đi qua trạm vừa cải thiện: quét từ vị trí sớm nhất của các trạm đó.
            foreach (var s in markedStops)
            {
                marked[s] = false;
                for (var i = tt.StopPatternStart[s]; i < tt.StopPatternStart[s + 1]; i++)
                {
                    var p = tt.StopPatterns[i];
                    var position = FirstPosition(p, s);
                    if (patternStart[p] == Inf) queuedPatterns.Add(p);
                    patternStart[p] = Math.Min(patternStart[p], position);
                }
            }
            markedStops.Clear();

            foreach (var p in queuedPatterns)
            {
                int trip = -1, board = -1;
                for (var position = patternStart[p]; position < tt.PatternStopCount(p); position++)
                {
                    var s = tt.PatternStop(p, position);
                    if (trip >= 0)
                    {
                        var t = tt.Arrival(trip, position);
                        if (t < bestArrival[s] && t < bestTarget)
                        {
                            arrival[k][s] = bestArrival[s] = transitArrival[k][s] = t;
                            label[k][s] = Label.Transit;
                            transitTrip[k][s] = trip;
                            boardPosition[k][s] = board;
                            alightPosition[k][s] = position;
                            Mark(s);
                        }
                    }

                    // Bắt được chuyến sớm hơn tại trạm này?
                    var readyAt = ready[k - 1][s];
                    if (readyAt == Inf || (trip >= 0 && readyAt >= tt.Departure(trip, position))) continue;
                    var earlier = EarliestTrip(p, position, readyAt, trip >= 0 ? trip : tt.PatternTripStart[p + 1], activeServices);
                    if (earlier >= 0)
                    {
                        trip = earlier;
                        board = position;
                    }
                }
                patternStart[p] = Inf;
            }
            queuedPatterns.Clear();

            // Đi bộ đổi trạm, chỉ từ trạm vừa xuống xe ở vòng này (không nối hai chặng đi bộ).
            foreach (var s in markedStops.ToArray())
            {
                for (var i = tt.TransferStart[s]; i < tt.TransferStart[s + 1]; i++)
                {
                    var to = tt.TransferTo[i];
                    var t = transitArrival[k][s] + tt.TransferSeconds[i];
                    if (t >= bestArrival[to] || t >= bestTarget) continue;
                    arrival[k][to] = bestArrival[to] = t;
                    label[k][to] = Label.Walk;
                    walkFrom[k][to] = s;
                    Mark(to);
                }
            }

            ready[k] = (int[])ready[k - 1].Clone();
            foreach (var s in markedStops)
            {
                ready[k][s] = arrival[k][s] + (label[k][s] == Label.Transit ? TransferSlackSeconds : 0);
            }

            StopAccess? improved = null;
            foreach (var e in egress)
            {
                if (label[k][e.Stop] == Label.None) continue;
                var t = arrival[k][e.Stop] + e.Seconds;
                if (t >= bestTarget) continue;
                bestTarget = t;
                improved = e;
            }
            if (improved is { } target) results.Add(new RawJourney(k - 1, Reconstruct(k, target)));
        }
        return results;

        List<RawLeg> Reconstruct(int round, StopAccess target)
        {
            var legs = new List<RawLeg>
            {
                new(RawLegKind.Egress, target.Stop, -1, arrival[round][target.Stop],
                    arrival[round][target.Stop] + target.Seconds, target.Meters),
            };
            var s = target.Stop;
            while (true)
            {
                while (label[round][s] == Label.None) round--; // nhãn kế thừa từ vòng trước
                if (label[round][s] == Label.Access)
                {
                    var a = accessByStop[s];
                    var boardAt = legs[^1].Departure; // đi bộ vừa kịp chuyến đầu tiên
                    legs.Add(new RawLeg(RawLegKind.Access, -1, s, boardAt - a.Seconds, boardAt, a.Meters));
                    break;
                }
                if (label[round][s] == Label.Walk)
                {
                    var from = walkFrom[round][s];
                    legs.Add(new RawLeg(RawLegKind.Transfer, from, s, transitArrival[round][from], arrival[round][s]));
                    s = from; // `from` luôn có nhãn đi xe ở cùng vòng
                }
                var trip = transitTrip[round][s];
                var board = boardPosition[round][s];
                var alight = alightPosition[round][s];
                var pattern = tt.TripPattern[trip];
                var boardStop = tt.PatternStop(pattern, board);
                legs.Add(new RawLeg(RawLegKind.Transit, boardStop, s, tt.Departure(trip, board), tt.Arrival(trip, alight),
                    Trip: trip, BoardPosition: board, AlightPosition: alight));
                s = boardStop;
                round--;
            }
            legs.Reverse();
            return legs;
        }
    }

    /// <summary>Chuyến chạy sớm nhất trong pattern xuất bến tại <paramref name="position"/> từ <paramref name="readyAt"/>.</summary>
    /// <param name="endTrip">Chỉ xét chuyến trước chuyến này (không tính nó).</param>
    private int EarliestTrip(int pattern, int position, int readyAt, int endTrip, bool[] activeServices)
    {
        // Pattern FIFO → giờ xuất bến tại mỗi vị trí tăng dần theo chuyến: tìm nhị phân chuyến đầu tiên đủ giờ.
        int lo = tt.PatternTripStart[pattern], hi = endTrip;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (tt.Departure(mid, position) < readyAt) lo = mid + 1;
            else hi = mid;
        }
        for (var t = lo; t < endTrip; t++)
        {
            if (activeServices[tt.TripService[t]]) return t;
        }
        return -1;
    }

    private int FirstPosition(int pattern, int stop)
    {
        for (var i = 0; ; i++)
        {
            if (tt.PatternStop(pattern, i) == stop) return i;
        }
    }

    private static int[] Filled(int length, int value)
    {
        var array = new int[length];
        Array.Fill(array, value);
        return array;
    }
}
