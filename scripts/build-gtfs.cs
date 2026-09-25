#:project ../src/Modules/Ingestion/BuytOi.Ingestion/BuytOi.Ingestion.csproj

// Sinh GTFS từ dữ liệu crawl.
// Dùng: dotnet run scripts/build-gtfs.cs [thư-mục-crawl=data] [thư-mục-ra=artifacts/gtfs]
using System.IO.Compression;
using BuytOi.Gtfs;
using BuytOi.Ingestion;

var input = args.Length > 0 ? args[0] : "data";
var output = args.Length > 1 ? args[1] : "artifacts/gtfs";

// Lịch không có EndDate: coi như còn hiệu lực 1 năm kể từ ngày sinh feed.
var feed = CrawlToGtfs.Convert(input, DateOnly.FromDateTime(DateTime.Today).AddYears(1));
GtfsWriter.Write(feed, output);

var zip = output + ".zip";
File.Delete(zip);
ZipFile.CreateFromDirectory(output, zip);

Console.WriteLine($"{feed.Routes.Count} routes, {feed.Stops.Count} stops, {feed.Trips.Count} trips, " +
                  $"{feed.StopTimes.Count} stop_times, {feed.Calendars.Count} calendars → {zip}");
