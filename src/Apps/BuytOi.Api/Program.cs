using System.Text.Json.Serialization;
using BuytOi.Catalog.Infrastructure;
using BuytOi.Gtfs;
using BuytOi.Routing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(); // lỗi (kể cả tham số sai kiểu/thiếu) trả JSON ProblemDetails
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Mặc định web cho phép số dạng chuỗi → OpenAPI sinh kiểu `number | string` cho client.
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
// Đường dẫn tương đối tính từ thư mục làm việc (src/Apps/BuytOi.Api khi `dotnet run --project`). Deploy: đặt Gtfs__Path.
AddModules(builder.Services, builder.Configuration["Gtfs:Path"] ?? throw new InvalidOperationException("Thiếu cấu hình Gtfs:Path"));

var app = builder.Build();

app.UseStatusCodePages(); // response lỗi rỗng (vd. 400 do bind tham số) → ProblemDetails
app.MapOpenApi();
app.MapGet("/health", () => "ok");
app.MapCatalog();
app.MapJourneyPlanner();

app.Run();

// Đọc feed một lần cho mọi module (feed lỗi → dừng ngay khi khởi động). Biến cục bộ của hàm riêng
// nên feed (~1 triệu StopTime) được giải phóng sau khi các module dựng xong dữ liệu của mình.
static void AddModules(IServiceCollection services, string feedPath)
{
    var feed = GtfsReader.Read(feedPath);
    services.AddCatalog(feed);
    services.AddJourneyPlanner(feed);
}
