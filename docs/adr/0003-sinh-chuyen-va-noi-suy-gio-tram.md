# ADR 0003: Sinh chuyến cụ thể và nội suy giờ tại trạm

- Trạng thái: Đã chấp nhận
- Ngày: 2026-09-25

## Bối cảnh

Dữ liệu crawl (xem `docs/data-model.md`) có danh sách chuyến với giờ xuất bến và về bến
(`trips.csv`, ~21 000 chuyến), nhưng **không có giờ tại từng trạm** và không có shape.
GTFS cần `stop_times.txt` cho mọi chuyến. Có hai cách:

1. `frequencies.txt`: mô tả "mỗi 8–20 phút một chuyến" từ `Headway_min`.
2. Sinh từng chuyến trong `trips.txt` + `stop_times.txt`.

## Quyết định

- **Sinh từng chuyến cụ thể** từ `trips.csv`. Dữ liệu đã có giờ xuất bến thật; `frequencies.txt`
  làm mất thông tin đó và `Headway_min` chỉ là khoảng (`"8 - 20"`), không đủ chính xác.
- **Nội suy giờ tại trạm theo khoảng cách** đường chim bay cộng dồn giữa các trạm liên tiếp:
  `t_i = t_đầu + (t_cuối − t_đầu) × d_i / d_tổng`. Trạm đầu/cuối `timepoint=1`, trạm giữa `timepoint=0`.
- Chuyến có giờ về < giờ xuất bến được coi là qua nửa đêm (cộng 24h, ghi dạng `24:30:00`).
- Lịch (`timetables`) của cùng một lượt có ngày chạy trùng nhau: lịch bắt đầu trước bị cắt
  `end_date` về ngày trước khi lịch sau bắt đầu (vd. tuyến 127).
- `EndDate` trống → dùng ngày kết thúc mặc định do người chạy converter truyền vào.

## Hệ quả

- Feed lớn hơn `frequencies.txt` (~20 000 chuyến × ~40 trạm ≈ 800 000 dòng `stop_times`) — chấp nhận được.
- Giờ tại trạm giữa là ước tính: sai số lớn ở đoạn kẹt xe hoặc đường vòng. RAPTOR và UI phải
  hiểu `timepoint=0` là giờ gần đúng. Khi có shape (map-match OSM) hoặc GPS cộng đồng, thay
  khoảng cách chim bay bằng khoảng cách dọc shape / thời gian đo được mà không đổi mô hình.
