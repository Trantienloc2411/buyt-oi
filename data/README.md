# Dữ liệu

Nguồn: https://buyttphcm.com.vn/Route (Trung tâm Quản lý Giao thông công cộng TP.HCM).

Dữ liệu crawl đầy đủ **không được commit** (chưa có sự đồng ý của Trung tâm) — `.gitignore` chặn
mọi thứ trong `data/` trừ file này và `data/samples/`.

Cấu trúc và các vấn đề dữ liệu: xem [`docs/data-model.md`](../docs/data-model.md).

`data/samples/` là 5 tuyến trích từ bản crawl, dùng cho test (`tests/BuytOi.Ingestion.Tests`):
01 (tuyến thường), 155 (tuyến vòng), 172 (chuyến qua nửa đêm), 127 (lịch cũ/mới chồng nhau), MRT1 (metro).

## Hình dạng tuyến (shapes) từ OpenStreetMap

Dữ liệu crawl không có đường đi của tuyến, chỉ có toạ độ trạm. Để vẽ bám đường, map-match feed lên
OSM bằng [pfaedle](https://github.com/ad-freiburg/pfaedle) (cần Docker), mỗi khi sinh lại feed:

```sh
# 1. Sinh feed từ dữ liệu crawl → artifacts/gtfs.zip
dotnet run scripts/build-gtfs.cs

# 2. Bản đồ OSM Việt Nam (Geofabrik, ~330 MB, ODbL) → artifacts/osm/
curl -L -o artifacts/osm/vietnam-latest.osm.pbf https://download.geofabrik.de/asia/vietnam-latest.osm.pbf

# 3. Map-match → artifacts/gtfs-shaped/ (feed GTFS có shapes.txt; đường dẫn tuyệt đối cho Docker trên Windows)
docker run --rm -v "$PWD/artifacts:/work" -v "$PWD/artifacts/gtfs-shaped:/gtfs-out" \
  ghcr.io/ad-freiburg/pfaedle:latest -x /work/osm/vietnam-latest.osm.pbf -i /work/gtfs.zip

# 4. Chạy API với feed có shape
Gtfs__Path=../../../artifacts/gtfs-shaped dotnet run --project src/Apps/BuytOi.Api
```

Không có shape thì API vẫn chạy: đường tuyến nối thẳng các trạm. Shape lấy từ OSM nên phải ghi công
"© OpenStreetMap contributors" (bản đồ nền đã ghi).
