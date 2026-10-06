# ADR 0002: Tự viết RAPTOR in-memory cho tìm đường

- Trạng thái: Đã chấp nhận
- Ngày: 2026-10-06

## Bối cảnh

Tìm đường là điểm khác biệt chính so với BusMap, GoBus và Google Maps. Lựa chọn:

1. **OpenTripPlanner / MOTIS:** đầy đủ tính năng, nhưng là service Java/C++ riêng (RAM hàng GB với OSM),
   khó tuỳ biến tiêu chí riêng (độ tin cậy tuyến, giờ nội suy) và đi ngược ADR 0001.
2. **Truy vấn đồ thị trong DB (pgRouting):** chậm với lịch theo giờ, cần PostGIS (ADR 0004).
3. **Tự viết RAPTOR** (Delling, Pajor, Werneck, 2012) trong module Routing.

Dữ liệu nhỏ: ~5 900 trạm, ~21 000 chuyến, ~920 000 stop_times.

## Quyết định

- Tự viết RAPTOR trong `BuytOi.Routing.Domain` (thuần, không phụ thuộc DB hay web); dựng `Timetable`
  từ GTFS ở `Routing.Infrastructure`, use case `JourneyPlanner` ở `Routing.Application`.
- `Timetable` là dữ liệu bất biến dạng **struct-of-arrays** (mảng `int` thay cho object) dựng một lần
  từ feed. Hoán đổi bằng `Interlocked.Exchange` khi có feed mới; real-time sau này là lớp overlay riêng.
- **Pattern** = các chuyến cùng tuyến, cùng hướng, cùng dãy trạm và **không vượt nhau**. Chuyến vượt
  chuyến trước (do giờ nội suy, ADR 0003) được tách sang pattern khác, để tìm chuyến sớm nhất bằng
  tìm kiếm nhị phân.
- Kết quả là tập Pareto theo (giờ đến, số lần đổi tuyến), tối đa 4 chuyến xe.
- Tham số khởi đầu: đi bộ tới/từ trạm ≤ 800 m chim bay, đổi trạm đi bộ ≤ 400 m, tốc độ 1,2 m/s ×
  hệ số đường vòng 1,3 (chưa có mạng đường OSM), tối thiểu 60 giây khi đổi xe tại cùng trạm.
- API: `GET /api/journeys?fromLat&fromLon&toLat&toLon&departAt`, giờ là giờ địa phương của feed.

## Hệ quả

- Truy vấn trên feed đầy đủ mất vài ms, chạy chung process với Catalog, không tăng RAM đáng kể.
- Tự chịu trách nhiệm đúng/sai của thuật toán → test với feed nhỏ dựng tay cho từng tình huống
  (đổi tuyến, đi bộ đổi trạm, chuyến vượt nhau, tuyến vòng, lịch không chạy).
- Giới hạn hiện tại, làm khi cần:
  - Chỉ xét chuyến của ngày phục vụ đang hỏi; chuyến qua nửa đêm của hôm trước (giờ ≥ 24:00) bị bỏ qua.
  - Chỉ tối ưu giờ đến với giờ xuất phát cố định; chưa có range-RAPTOR ("đi muộn nhất vẫn kịp").
  - Đi bộ theo đường chim bay; thay bằng khoảng cách OSM khi có.
  - Chưa có tiêu chí độ tin cậy tuyến, giá vé, hay ưu tiên metro (McRAPTOR).
