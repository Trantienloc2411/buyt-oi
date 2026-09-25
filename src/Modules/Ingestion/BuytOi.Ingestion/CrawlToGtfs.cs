using System.Globalization;
using System.Net;
using System.Text.Json;
using BuytOi.Gtfs;
using Calendar = BuytOi.Gtfs.Calendar;
using Microsoft.VisualBasic.FileIO;

namespace BuytOi.Ingestion;

/// <summary>
/// Chuyển dữ liệu crawl từ buyttphcm.com.vn sang GTFS.
/// Cấu trúc nguồn: docs/data-model.md. Cách sinh chuyến / giờ tại trạm: docs/adr/0003.
/// </summary>
public static class CrawlToGtfs
{
    public const string Timezone = "Asia/Ho_Chi_Minh";
    private const string SourceUrl = "https://buyttphcm.com.vn";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Variant(bool Outbound, string Headsign, List<string> StopIds, double[] CumulativeMeters);

    private sealed record Timetable(string RouteId, string RouteVarId, string TimeTableId, ServiceDays Days,
        DateOnly Start, DateOnly End);

    /// <param name="crawlDir">Thư mục chứa routes.csv, stops.csv, ... và routes_raw.json.</param>
    /// <param name="defaultEndDate">end_date cho lịch không có EndDate.</param>
    public static GtfsFeed Convert(string crawlDir, DateOnly defaultEndDate)
    {
        List<Dictionary<string, string>> Read(string name) => ReadCsv(Path.Combine(crawlDir, name));

        var stopRows = Read("stops.csv").ToDictionary(s => s["StopId"]);
        var colors = ReadColors(Path.Combine(crawlDir, "routes_raw.json"));

        var agencies = new Dictionary<string, Agency>();
        var routes = Read("routes.csv")
            .Select(r => new Route(
                r["RouteId"], AgencyOf(r["Orgs"], agencies), r["RouteNo"], r["RouteName"],
                r["RouteNo"].StartsWith("MRT", StringComparison.Ordinal) ? RouteType.Subway : RouteType.Bus,
                colors.GetValueOrDefault(r["RouteId"])))
            .ToList();

        var variants = Read("route_stops.csv")
            .GroupBy(r => (r["RouteId"], r["RouteVarId"]))
            .Join(Read("route_variants.csv"), g => g.Key, v => (v["RouteId"], v["RouteVarId"]), (g, v) =>
            {
                var stopIds = g.OrderBy(r => int.Parse(r["StopOrder"], Inv)).Select(r => r["StopId"]).ToList();
                return (g.Key, Variant: new Variant(v["Outbound"] == "True", v["RouteVarShortName"], stopIds,
                    CumulativeMeters(stopIds.Select(id => stopRows[id]).ToList())));
            })
            .ToDictionary(x => x.Key, x => x.Variant);

        var timetables = ClipOverlaps(Read("timetables.csv").Select(t => new Timetable(
                t["RouteId"], t["RouteVarId"], t["TimeTableId"], ParseDays(t["ApplyDates"]), ParseDate(t["StartDate"]),
                t["EndDate"].Length == 0 ? defaultEndDate : ParseDate(t["EndDate"]))))
            .ToDictionary(t => (t.RouteId, t.TimeTableId));

        var calendars = new Dictionary<string, Calendar>();
        var trips = new List<Trip>();
        var stopTimes = new List<StopTime>();
        foreach (var row in Read("trips.csv"))
        {
            var tt = timetables[(row["RouteId"], row["TimeTableId"])];
            if (tt.End < tt.Start) continue; // lịch bị lịch mới thay thế hoàn toàn
            var variant = variants[(tt.RouteId, tt.RouteVarId)];

            var serviceId = $"{(int)tt.Days}_{tt.Start:yyyyMMdd}_{tt.End:yyyyMMdd}";
            calendars.TryAdd(serviceId, new Calendar(serviceId, tt.Days, tt.Start, tt.End));

            var tripId = $"{row["RouteId"]}_{row["TripId"]}";
            trips.Add(new Trip(tripId, row["RouteId"], serviceId, variant.Outbound ? 0 : 1, variant.Headsign));

            var start = ParseTime(row["StartTime"]);
            var end = ParseTime(row["EndTime"]);
            if (end < start) end += 24 * 3600; // qua nửa đêm

            var n = variant.StopIds.Count;
            var total = variant.CumulativeMeters[^1];
            for (var i = 0; i < n; i++)
            {
                var fraction = total > 0 ? variant.CumulativeMeters[i] / total : (double)i / (n - 1);
                var time = new GtfsTime(start + (int)Math.Round((end - start) * fraction));
                stopTimes.Add(new StopTime(tripId, i + 1, variant.StopIds[i], time, time, Timepoint: i == 0 || i == n - 1));
            }
        }

        var usedStops = variants.Values.SelectMany(v => v.StopIds).ToHashSet();
        var stops = stopRows.Values
            .Where(s => usedStops.Contains(s["StopId"]))
            .Select(s => new Stop(s["StopId"], s["Code"].Length == 0 ? null : s["Code"], s["Name"],
                double.Parse(s["Lat"], Inv), double.Parse(s["Lng"], Inv),
                s["SupportDisability"] == "Có" ? WheelchairBoarding.Accessible : WheelchairBoarding.Unknown))
            .ToList();

        return new GtfsFeed([.. agencies.Values], routes, stops, trips, stopTimes, [.. calendars.Values], []);
    }

    /// <summary>"Tên, ĐT: số | Tên 2 | " → thêm các đơn vị vào <paramref name="agencies"/>, trả về id đơn vị đầu tiên.</summary>
    private static string AgencyOf(string orgs, Dictionary<string, Agency> agencies)
    {
        string? first = null;
        foreach (var part in orgs.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var text = WebUtility.HtmlDecode(part);
            var phoneAt = text.IndexOf(", ĐT:", StringComparison.Ordinal);
            var name = (phoneAt < 0 ? text : text[..phoneAt]).Trim();
            if (!agencies.TryGetValue(name, out var agency))
            {
                agency = new Agency($"A{agencies.Count + 1}", name, SourceUrl, Timezone,
                    phoneAt < 0 ? null : text[(phoneAt + 5)..].Trim());
                agencies.Add(name, agency);
            }
            first ??= agency.Id;
        }
        return first ?? throw new InvalidDataException($"Tuyến không có đơn vị vận hành: '{orgs}'");
    }

    /// <summary>Cùng một lượt, ngày chạy trùng nhau: lịch bắt đầu trước kết thúc ngay trước khi lịch sau bắt đầu.</summary>
    private static IEnumerable<Timetable> ClipOverlaps(IEnumerable<Timetable> timetables)
    {
        foreach (var group in timetables.GroupBy(t => (t.RouteId, t.RouteVarId)))
        {
            foreach (var t in group)
            {
                var end = t.End;
                foreach (var next in group)
                {
                    if (next.Start > t.Start && (next.Days & t.Days) != 0 && next.Start <= end)
                        end = next.Start.AddDays(-1);
                }
                yield return t with { End = end };
            }
        }
    }

    private static double[] CumulativeMeters(List<Dictionary<string, string>> stops)
    {
        var result = new double[stops.Count];
        for (var i = 1; i < stops.Count; i++)
        {
            result[i] = result[i - 1] + Haversine(
                double.Parse(stops[i - 1]["Lat"], Inv), double.Parse(stops[i - 1]["Lng"], Inv),
                double.Parse(stops[i]["Lat"], Inv), double.Parse(stops[i]["Lng"], Inv));
        }
        return result;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        double Rad(double deg) => deg * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lon2 - lon1) / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    private static ServiceDays ParseDays(string applyDates) =>
        applyDates.Split(',', StringSplitOptions.TrimEntries).Aggregate(ServiceDays.None, (days, d) => days | d switch
        {
            "T2" => ServiceDays.Monday,
            "T3" => ServiceDays.Tuesday,
            "T4" => ServiceDays.Wednesday,
            "T5" => ServiceDays.Thursday,
            "T6" => ServiceDays.Friday,
            "T7" => ServiceDays.Saturday,
            "CN" => ServiceDays.Sunday,
            _ => throw new InvalidDataException($"Ngày không hợp lệ: '{d}' trong '{applyDates}'"),
        });

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "dd/MM/yyyy", Inv);

    private static int ParseTime(string hhmm) => (int)TimeOnly.ParseExact(hhmm, "HH:mm", Inv).ToTimeSpan().TotalSeconds;

    private static Dictionary<string, string> ReadColors(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray().ToDictionary(
            r => r.GetProperty("RouteId").GetInt32().ToString(Inv),
            r => r.GetProperty("Detail").GetProperty("Color").GetString()!.TrimStart('#'));
    }

    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException($"File rỗng: {path}");
        var rows = new List<Dictionary<string, string>>();
        while (parser.ReadFields() is { } fields)
        {
            rows.Add(header.Zip(fields).ToDictionary(x => x.First, x => x.Second));
        }
        return rows;
    }
}
