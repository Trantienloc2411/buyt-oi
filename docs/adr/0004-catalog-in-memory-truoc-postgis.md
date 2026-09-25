# ADR 0004: Catalog đọc GTFS vào bộ nhớ, chưa dùng PostGIS

- Trạng thái: Đã chấp nhận
- Ngày: 2026-09-25

## Bối cảnh

Kế hoạch ban đầu: Catalog nạp GTFS vào PostgreSQL + PostGIS. Nhưng:
- Dữ liệu nhỏ: ~5 900 trạm, 178 tuyến. Tìm trạm gần nhất bằng duyệt tuyến tính mất vài chục µs.
- Chưa có dữ liệu cần ghi (Community), chưa cần lịch sử phiên bản feed trong DB (Ingestion).
- Docker không pull được image ở mạng công ty → PostGIS chặn việc phát triển.

## Quyết định

- Catalog đọc `gtfs.zip` lúc khởi động thành `CatalogSnapshot` bất biến trong bộ nhớ (cùng hướng với RAPTOR).
- Catalog chỉ phụ thuộc `BuytOi.Gtfs` (đọc feed), không phụ thuộc Ingestion.
- Host dùng workstation GC: RAM ~210 MB thay vì ~415 MB với Server GC (đo trên feed đầy đủ).

## Hệ quả

- Không cần DB để chạy API; deploy chỉ cần 1 process + file feed.
- Thêm PostGIS khi có nhu cầu thật: dữ liệu cộng đồng cần ghi, lịch sử feed, hoặc truy vấn không gian
  phức tạp. Khi đó mới tách interface đọc dữ liệu (port) và thêm adapter PostGIS; endpoint không đổi.
- Nạp feed đầy đủ mất ~5 s lúc khởi động (TextFieldParser chậm). Chấp nhận được; tối ưu khi cần.
