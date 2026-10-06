# ADR 0001: Modular Monolith, module phân lớp, nói chuyện qua Contracts

- Trạng thái: Đã chấp nhận
- Ngày: 2026-10-06

## Bối cảnh

Một người làm cùng AI, chạy trên một VPS nhỏ. Các phần của hệ thống (tra cứu tuyến/trạm, tìm đường,
real-time, dữ liệu cộng đồng, nhập dữ liệu) có nhịp thay đổi khác nhau nhưng cùng đọc một feed GTFS
và cần trả lời trong vài ms. Microservices thêm mạng, triển khai, giám sát mà chưa đem lại gì;
một project lớn duy nhất thì ranh giới mờ dần và khó tách sau này. Sẽ còn thêm nhiều tính năng
(real-time, cộng đồng, worker nhập dữ liệu) nên cần chỗ đặt rõ ràng cho từng thứ ngay từ đầu.

## Quyết định

### Cấu trúc

```
src/
  Apps/BuytOi.Api/                 điểm chạy: ghép module (sau này: worker Ingestion, ...)
  Modules/<Module>/
    BuytOi.<Module>.Contracts      phần công khai cho module khác: DTO, interface, event
    BuytOi.<Module>.Domain         mô hình + thuật toán thuần (vd. RAPTOR), không IO, không web
    BuytOi.<Module>.Application    use case + port (interface) cho thứ bên ngoài cung cấp
    BuytOi.<Module>.Infrastructure adapter: đọc GTFS/DB/API ngoài, endpoint HTTP, đăng ký DI
  Shared/BuytOi.Gtfs/              mô hình GTFS dùng chung (shared kernel)
```

- **Một process** ghép các module: Catalog, Routing, Realtime, Community, Ingestion.
  Không microservices, không Kubernetes ở giai đoạn này.
- Ports & Adapters: Application khai báo port (vd. `ITimetableSource`, sau này `IVehiclePositionSource`),
  Infrastructure cài đặt. Endpoint HTTP cũng là adapter nên nằm ở Infrastructure.
- Chỉ tạo lớp khi có code thật cho lớp đó, không tạo project rỗng: Catalog chưa có Contracts
  (chưa module nào gọi nó); Ingestion mới có Infrastructure (`CrawlToGtfs` là adapter đọc nguồn crawl),
  thêm Domain/Application khi làm ETL có phiên bản, so sánh khác biệt, rollback.
- Host đọc feed GTFS **một lần** lúc khởi động rồi đưa cho các module dựng dữ liệu riêng.

### Luật phụ thuộc (kiểm tra bởi `tests/BuytOi.ArchitectureTests`)

| Project | Được tham chiếu |
|---|---|
| Domain | Shared |
| Application | Shared, Domain, Contracts cùng module, Contracts module khác |
| Infrastructure | Shared, Application, Domain, Contracts cùng module, Contracts module khác |
| Contracts | Shared, Contracts module khác |
| Apps | Shared, Infrastructure và Contracts của module |

- Domain, Application, Contracts không được dùng ASP.NET Core (`FrameworkReference`).
- Test đọc `ProjectReference` trong mọi `.csproj`, nên module/app mới tự được kiểm tra;
  tên project phải theo `BuytOi.<Module>.<Lớp>`.
- Giao tiếp giữa module: gọi interface trong Contracts, hoặc event in-process (vd. `FeedPublished`).

## Hệ quả

- Triển khai = 1 process + 1 file feed; debug và test đơn giản.
- Thêm tính năng = thêm module (hoặc lớp) mới, không sửa module cũ ngoài Contracts của nó.
- Ranh giới được giữ bằng test chứ không bằng mạng: tách một module ra service riêng sau này
  chỉ là thay cài đặt của interface trong Contracts bằng client HTTP.
- Nhiều project hơn (mỗi module 2–4), type trong Domain/Application phải `public` để lớp ngoài dùng;
  ranh giới giữa module vẫn được giữ vì module khác chỉ thấy Contracts.
- Các module chia sẻ CPU/RAM; một module nặng (vd. Realtime) có thể ảnh hưởng module khác.
  Chấp nhận ở quy mô hiện tại.
