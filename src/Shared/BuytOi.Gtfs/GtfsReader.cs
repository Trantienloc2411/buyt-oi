using System.Globalization;
using System.IO.Compression;
using Microsoft.VisualBasic.FileIO;

namespace BuytOi.Gtfs;

/// <summary>Đọc feed GTFS từ file .zip hoặc thư mục các file .txt (cùng tập con với <see cref="GtfsWriter"/>).</summary>
public static class GtfsReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static GtfsFeed Read(string path)
    {
        if (Directory.Exists(path))
        {
            return Read(name =>
            {
                var file = Path.Combine(path, name);
                return File.Exists(file) ? new StreamReader(file) : null;
            });
        }

        using var zip = ZipFile.OpenRead(path);
        return Read(name => zip.GetEntry(name) is { } entry ? new StreamReader(entry.Open()) : null);
    }

    private static GtfsFeed Read(Func<string, TextReader?> open)
    {
        // Đọc từng dòng (không nạp cả file): stop_times có ~1 triệu dòng.
        IEnumerable<Dictionary<string, string>> Rows(string name, bool required = true)
        {
            using var reader = open(name);
            if (reader is null)
            {
                if (required) throw new InvalidDataException($"Feed thiếu {name}");
                yield break;
            }
            foreach (var row in ReadCsv(reader)) yield return row;
        }

        return new GtfsFeed(
            Rows("agency.txt").Select(r => new Agency(
                r["agency_id"], r["agency_name"], r["agency_url"], r["agency_timezone"], Opt(r, "agency_phone"))).ToList(),
            Rows("routes.txt").Select(r => new Route(
                r["route_id"], r["agency_id"], r["route_short_name"], r["route_long_name"],
                (RouteType)Int(r["route_type"]), Opt(r, "route_color"))).ToList(),
            Rows("stops.txt").Select(r => new Stop(
                r["stop_id"], Opt(r, "stop_code"), r["stop_name"], Double(r["stop_lat"]), Double(r["stop_lon"]),
                Opt(r, "wheelchair_boarding") is { } w ? (WheelchairBoarding)Int(w) : WheelchairBoarding.Unknown)).ToList(),
            Rows("trips.txt").Select(r => new Trip(
                r["trip_id"], r["route_id"], r["service_id"], Opt(r, "direction_id") is { } d ? Int(d) : 0,
                Opt(r, "trip_headsign"), Opt(r, "shape_id"))).ToList(),
            Rows("stop_times.txt").Select(r => new StopTime(
                r["trip_id"], Int(r["stop_sequence"]), r["stop_id"], Time(r["arrival_time"]), Time(r["departure_time"]),
                Opt(r, "timepoint") != "0")).ToList(),
            Rows("calendar.txt").Select(r => new Calendar(
                r["service_id"], Days(r), Date(r["start_date"]), Date(r["end_date"]))).ToList(),
            Rows("shapes.txt", required: false).Select(r => new ShapePoint(
                r["shape_id"], Int(r["shape_pt_sequence"]), Double(r["shape_pt_lat"]), Double(r["shape_pt_lon"]))).ToList());
    }

    private static IEnumerable<Dictionary<string, string>> ReadCsv(TextReader reader)
    {
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? [];
        while (parser.ReadFields() is { } fields)
        {
            yield return header.Zip(fields).ToDictionary(x => x.First, x => x.Second);
        }
    }

    private static string? Opt(Dictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) && value.Length > 0 ? value : null;

    private static int Int(string value) => int.Parse(value, Inv);
    private static double Double(string value) => double.Parse(value, Inv);
    private static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyyMMdd", Inv);

    private static GtfsTime Time(string value)
    {
        var parts = value.Split(':');
        return new GtfsTime(Int(parts[0]) * 3600 + Int(parts[1]) * 60 + Int(parts[2]));
    }

    private static ServiceDays Days(Dictionary<string, string> row) =>
        new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" }
            .Select((day, i) => row[day] == "1" ? (ServiceDays)(1 << i) : ServiceDays.None)
            .Aggregate(ServiceDays.None, (all, day) => all | day);
}
