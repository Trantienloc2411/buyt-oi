# Mô hình dữ liệu crawl (buyttphcm.com.vn)

Khảo sát bản crawl ngày 14/09/2026 từ https://buyttphcm.com.vn/Route.
Dữ liệu nằm trong `data/` (không commit, xem `data/README.md`).

## Tổng quan

| File | Số dòng | Khoá | Ghi chú |
|---|---|---|---|
| `routes.csv` | 178 | `RouteId` (1-1 với `RouteNo`) | Tuyến |
| `route_variants.csv` | 335 | `(RouteId, RouteVarId)` | Lượt đi/về. 157 tuyến có 2 lượt, 21 tuyến có 1 lượt |
| `route_stops.csv` | 13 851 | `(RouteId, RouteVarId, StopOrder)` | Thứ tự trạm của mỗi lượt, `StopOrder` liên tục từ 1 |
| `stops.csv` | 5 904 | `StopId` | Trạm, có toạ độ |
| `timetables.csv` | 359 | `(RouteId, TimeTableId)` | Biểu đồ giờ của mỗi lượt |
| `trips.csv` | 20 949 | `(RouteId, TripId)` | Giờ xuất bến / giờ về bến của mỗi chuyến |
| `routes_raw.json` | 178 tuyến | | JSON gốc, lồng nhau: `Detail`, `Variants[].Stops[]`, `Timetables[].Trips[]` |

**Chú ý khoá:** `RouteVarId`, `TimeTableId`, `TripId` **chỉ duy nhất trong phạm vi một tuyến**
(vd. `RouteVarId` = 1/2 ở mọi tuyến; `TripId` trùng 11 664 lần nếu xét toàn cục).
Khi sang GTFS phải ghép với `RouteId`.

## Ánh xạ sang GTFS

| GTFS | Nguồn | Cách làm |
|---|---|---|
| `agency.txt` | `routes.Orgs` | Tách chuỗi theo ` \| `/`<br/>`, bỏ phần `ĐT: …` sang `agency_phone`. Một tuyến có thể có nhiều đơn vị → GTFS chỉ cho 1 `agency_id`/tuyến, lấy đơn vị đầu tiên |
| `routes.txt` | `routes` | `route_id`=`RouteId`, `route_short_name`=`RouteNo`, `route_long_name`=`RouteName`, `route_type`=3 (bus), `route_color` lấy từ `Detail.Color` trong JSON (CSV không có) |
| `stops.txt` | `stops` | `stop_id`=`StopId`, `stop_code`=`Code`, `stop_lat/lon`, `wheelchair_boarding` từ `SupportDisability` (`Có`→1, trống→0) |
| `trips.txt` | `trips` + `timetables` | `trip_id`=`{RouteId}_{TripId}`, `direction_id` từ `route_variants.Outbound` (True→0, False→1), `service_id` từ `ApplyDates` + `StartDate/EndDate` |
| `stop_times.txt` | `route_stops` + `trips` | **Không có giờ tại từng trạm**, chỉ có giờ đầu/cuối chuyến → phải nội suy (xem Vấn đề 1) |
| `calendar.txt` | `timetables.ApplyDates/StartDate/EndDate` | 12 tổ hợp ngày khác nhau (`T2…T7, CN`). Ngày dạng `dd/MM/yyyy`. `EndDate` trống ở 355/359 → đặt một ngày kết thúc mặc định |
| `frequencies.txt` | `Headway_min` | Dạng khoảng `"8 - 20"` — chỉ để tham khảo, đã có danh sách chuyến cụ thể |
| `shapes.txt` | — | **Không có** dữ liệu đường đi (polyline) |
| `fare_*` | `routes.Tickets` | HTML (`<br/>&nbsp…`), cần parse. Để sau |

## Vấn đề dữ liệu

1. **Không có giờ tại từng trạm.** Chỉ có `StartTime`/`EndTime` của chuyến. Hướng xử lý: nội suy
   theo khoảng cách dọc tuyến (tính từ toạ độ trạm vì không có shape), đặt `timepoint=0` cho trạm giữa.
   Đây là quyết định cho ADR 0003 (cùng câu hỏi `frequencies.txt` hay sinh chuyến).
2. **Không có shape.** Tạm nối thẳng các trạm; sau này có thể map-match bằng OSM.
3. **Chuyến qua nửa đêm:** 32 chuyến (vd. tuyến 172: `21:00 → 00:30`). GTFS cần ghi `24:30:00`.
4. **Biểu đồ giờ cũ lẫn mới:** 28/359 timetable có `IsCurrent=false` (chỉ có trong JSON).
   24 lượt có 2 timetable, 2 lượt có 3. `StartDate` có cả ngày trong tương lai (vd. `03/09/2026`),
   4 timetable có `EndDate=30/09/2026`. → Ưu tiên lọc theo khoảng ngày trong `calendar.txt`, không
   bỏ timetable chỉ vì `IsCurrent=false`.
5. **4 lượt không có timetable:** tuyến `RouteId` 336/2, 371/2, 406/2, 409/1 → không sinh chuyến.
6. **Tuyến vòng:** 27 lượt có trạm đầu = trạm cuối (vd. 155, 156D, 158). Hợp lệ trong GTFS vì
   `stop_sequence` vẫn tăng dần; RAPTOR phải chịu được một trạm xuất hiện 2 lần trong 1 chuyến.
7. **`Code` trạm trùng:** `BD-0908`, `Q9 163`, `Q9 164` gán cho nhiều trạm khác nhau → luôn dùng `StopId`, không dùng `Code` làm khoá.
8. **Trạng thái trạm:** ngoài 5 717 `Đang khai thác` còn `Chưa khai thác` (149), `Tạm ngưng` (20),
   `Không có` (10), `Đã hủy` (8). Cần quyết định có đưa vào GTFS hay không (các trạm này vẫn nằm trong lộ trình).
9. **Loại trạm:** có 14 `Ga Metro Số 1` — điểm nối buýt ↔ metro, hữu ích cho transfers sau này.
10. **Phạm vi địa lý rộng:** lat 8.68–11.52, lng 106.09–107.55 (gồm Bình Dương, Bà Rịa – Vũng Tàu,
    Cần Giờ, Côn Đảo sau sáp nhập). Không phải toạ độ lỗi — đừng lọc theo bbox TP.HCM cũ.
11. **Chuỗi bẩn:** ký tự `¿` ở cuối `OutBoundName/InBoundName/...Description` trong JSON; HTML trong `Orgs`, `Tickets`.
    `RouteNo` có dạng chữ (`156D`, `156V`, `60-1`…) → lưu là string.
12. **Loại tuyến** (`Type`): Có trợ giá 114, Không trợ giá 48, Du lịch 7, Học sinh 7, Buýt nhanh 2.
    Tuyến Học sinh có lịch riêng (thường `T2–T6`) — có thể ẩn khỏi tìm đường mặc định.

## Dữ liệu có sẵn, chưa dùng

- `route_variants.Distance_m`, `RunningTime_min` — kiểm tra chéo khi nội suy giờ.
- `routes.NumOfSeats`, `Headway_min`, `OperationTime` — hiển thị thông tin tuyến.
- `stops.Zone`, `Ward`, `Street`, `AddressNo` — tìm kiếm trạm theo địa chỉ.
- `Detail.OutBoundDescription/InBoundDescription` — mô tả lộ trình theo tên đường.
