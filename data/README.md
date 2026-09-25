# Dữ liệu

Nguồn: https://buyttphcm.com.vn/Route (Trung tâm Quản lý Giao thông công cộng TP.HCM).

Dữ liệu crawl đầy đủ **không được commit** (chưa có sự đồng ý của Trung tâm) — `.gitignore` chặn
mọi thứ trong `data/` trừ file này và `data/samples/`.

Cấu trúc và các vấn đề dữ liệu: xem [`docs/data-model.md`](../docs/data-model.md).

`data/samples/` là 5 tuyến trích từ bản crawl, dùng cho test (`tests/BuytOi.Ingestion.Tests`):
01 (tuyến thường), 155 (tuyến vòng), 172 (chuyến qua nửa đêm), 127 (lịch cũ/mới chồng nhau), MRT1 (metro).
