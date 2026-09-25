using System.Globalization;
using System.Text;

namespace BuytOi.Gtfs;

/// <summary>Ghi <see cref="GtfsFeed"/> ra thư mục gồm các file .txt (CSV, UTF-8).</summary>
public static class GtfsWriter
{
    public static void Write(GtfsFeed feed, string directory)
    {
        ArgumentNullException.ThrowIfNull(feed);
        Directory.CreateDirectory(directory);

        WriteFile(directory, "agency.txt", ["agency_id", "agency_name", "agency_url", "agency_timezone", "agency_phone"],
            feed.Agencies.Select(a => new[] { a.Id, a.Name, a.Url, a.Timezone, a.Phone }));

        WriteFile(directory, "routes.txt", ["route_id", "agency_id", "route_short_name", "route_long_name", "route_type", "route_color"],
            feed.Routes.Select(r => new[] { r.Id, r.AgencyId, r.ShortName, r.LongName, Int((int)r.Type), r.Color }));

        WriteFile(directory, "stops.txt", ["stop_id", "stop_code", "stop_name", "stop_lat", "stop_lon", "wheelchair_boarding"],
            feed.Stops.Select(s => new[] { s.Id, s.Code, s.Name, Coord(s.Lat), Coord(s.Lon), Int((int)s.Wheelchair) }));

        WriteFile(directory, "trips.txt", ["trip_id", "route_id", "service_id", "direction_id", "trip_headsign", "shape_id"],
            feed.Trips.Select(t => new[] { t.Id, t.RouteId, t.ServiceId, Int(t.DirectionId), t.Headsign, t.ShapeId }));

        WriteFile(directory, "stop_times.txt", ["trip_id", "stop_sequence", "stop_id", "arrival_time", "departure_time", "timepoint"],
            feed.StopTimes.Select(st => new[]
            {
                st.TripId, Int(st.Sequence), st.StopId, st.Arrival.ToString(), st.Departure.ToString(), st.Timepoint ? "1" : "0",
            }));

        WriteFile(directory, "calendar.txt",
            ["service_id", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday", "start_date", "end_date"],
            feed.Calendars.Select(c => new[]
            {
                c.ServiceId,
                Day(c, ServiceDays.Monday), Day(c, ServiceDays.Tuesday), Day(c, ServiceDays.Wednesday), Day(c, ServiceDays.Thursday),
                Day(c, ServiceDays.Friday), Day(c, ServiceDays.Saturday), Day(c, ServiceDays.Sunday),
                Date(c.StartDate), Date(c.EndDate),
            }));

        // shapes.txt là tuỳ chọn: chỉ ghi khi có dữ liệu.
        if (feed.Shapes.Count > 0)
        {
            WriteFile(directory, "shapes.txt", ["shape_id", "shape_pt_sequence", "shape_pt_lat", "shape_pt_lon"],
                feed.Shapes.Select(p => new[] { p.ShapeId, Int(p.Sequence), Coord(p.Lat), Coord(p.Lon) }));
        }
    }

    private static void WriteFile(string directory, string name, string[] header, IEnumerable<string?[]> rows)
    {
        // UTF-8 không BOM: GTFS cho phép BOM nhưng nhiều công cụ đọc sai cột đầu.
        using var writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false));
        writer.NewLine = "\n";
        writer.WriteLine(string.Join(',', header));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', row.Select(Escape)));
        }
    }

    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Coord(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Date(DateOnly value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static string Day(Calendar c, ServiceDays day) => c.Days.HasFlag(day) ? "1" : "0";
}
