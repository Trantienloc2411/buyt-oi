using System.Text.Json.Serialization;
using BuytOi.Catalog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Đường dẫn tương đối tính từ thư mục làm việc (src/BuytOi.Host khi `dotnet run --project`). Deploy: đặt Gtfs__Path.
builder.Services.AddCatalog(builder.Configuration["Gtfs:Path"] ?? throw new InvalidOperationException("Thiếu cấu hình Gtfs:Path"));

var app = builder.Build();

app.MapOpenApi();
app.MapGet("/health", () => "ok");
app.MapCatalog();

app.Run();
