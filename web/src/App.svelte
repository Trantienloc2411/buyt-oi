<script lang="ts">
  import { onMount } from 'svelte'
  import { LngLatBounds, Map, NavigationControl, Popup, setWorkerUrl, type GeoJSONSource } from 'maplibre-gl'
  // MapLibre 6 tìm worker cạnh file JS của nó; Vite gộp lại nên đường dẫn sai → chỉ đường cho nó.
  import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
  import type { Feature, FeatureCollection } from 'geojson'
  import { api, type RouteDetail, type RouteSummary, type StopInfo } from './lib/api'

  // ponytail: nền bản đồ công cộng OpenFreeMap; chuyển sang PMTiles tự host (Protomaps) khi deploy.
  const STYLE = 'https://tiles.openfreemap.org/styles/liberty'
  const HCMC: [number, number] = [106.7, 10.78]
  const EMPTY: FeatureCollection = { type: 'FeatureCollection', features: [] }

  let mapEl: HTMLDivElement
  let panelEl: HTMLElement
  let map: Map | undefined
  let routes = $state<RouteSummary[]>([])
  let query = $state('')
  let selected = $state<RouteDetail | null>(null)
  let directionId = $state(0)
  let error = $state<string | null>(null)

  // Tìm không dấu: "ben thanh" khớp "Bến Thành".
  const fold = (s: string) => s.normalize('NFD').replace(/\p{M}/gu, '').replace(/đ/gi, 'd').toLowerCase()
  const filtered = $derived.by(() => {
    const q = fold(query.trim())
    return q ? routes.filter((r) => fold(`${r.shortName} ${r.longName}`).includes(q)) : routes
  })
  const direction = $derived(
    selected?.directions.find((d) => d.directionId === directionId) ?? selected?.directions[0],
  )

  const color = (hex: string | null | undefined) => `#${hex ?? '0b7a4b'}`
  // Chữ đen trên nền sáng, chữ trắng trên nền tối (độ sáng tương đối).
  function textOn(hex: string | null | undefined) {
    const n = parseInt(hex ?? '0b7a4b', 16)
    const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255]
    return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? '#000' : '#fff'
  }

  const point = (s: StopInfo, props: Record<string, string> = {}): Feature => ({
    type: 'Feature',
    geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
    properties: { name: s.name, code: s.code ?? '', ...props },
  })

  async function select(id: string) {
    try {
      selected = await api.route(id)
      directionId = selected.directions[0]?.directionId ?? 0
    } catch (e) {
      error = `Không tải được tuyến: ${(e as Error).message}`
    }
  }

  // Vẽ tuyến đang chọn: đường nối các trạm (chưa có shape thật) + các trạm.
  $effect(() => {
    const src = map?.getSource<GeoJSONSource>('route')
    if (!src) return
    if (!selected || !direction || direction.stops.length === 0) {
      src.setData(EMPTY)
      return
    }
    const c = color(selected.color)
    const coords = direction.stops.map((s) => [s.lon, s.lat] as [number, number])
    src.setData({
      type: 'FeatureCollection',
      features: [
        { type: 'Feature', geometry: { type: 'LineString', coordinates: coords }, properties: { color: c } },
        ...direction.stops.map((s) => point(s, { color: c })),
      ],
    })
    const bounds = coords.reduce((b, p) => b.extend(p), new LngLatBounds(coords[0], coords[0]))
    map!.fitBounds(bounds, { padding: { top: 40, left: 40, right: 40, bottom: panelEl.offsetHeight + 20 }, maxZoom: 15 })
  })

  onMount(() => {
    setWorkerUrl(workerUrl)
    const m = new Map({ container: mapEl, style: STYLE, center: HCMC, zoom: 11 })
    m.addControl(new NavigationControl({ showCompass: false }), 'top-right')

    m.on('load', async () => {
      try {
        const [rs, stops] = await Promise.all([api.routes(), api.stops()])
        m.addSource('stops', { type: 'geojson', data: { type: 'FeatureCollection', features: stops.map((s) => point(s)) } })
        m.addLayer({
          id: 'stops',
          type: 'circle',
          source: 'stops',
          minzoom: 13,
          paint: { 'circle-radius': 3, 'circle-color': '#777', 'circle-stroke-width': 1, 'circle-stroke-color': '#fff' },
        })
        m.addSource('route', { type: 'geojson', data: EMPTY })
        m.addLayer({
          id: 'route-line',
          type: 'line',
          source: 'route',
          filter: ['==', '$type', 'LineString'],
          layout: { 'line-join': 'round', 'line-cap': 'round' },
          paint: { 'line-color': ['get', 'color'], 'line-width': 5 },
        })
        m.addLayer({
          id: 'route-stops',
          type: 'circle',
          source: 'route',
          filter: ['==', '$type', 'Point'],
          paint: { 'circle-radius': 5, 'circle-color': '#fff', 'circle-stroke-width': 2, 'circle-stroke-color': ['get', 'color'] },
        })

        for (const layer of ['stops', 'route-stops']) {
          m.on('click', layer, (e) => {
            const f = e.features?.[0]
            if (!f) return
            const { name, code } = f.properties as { name: string; code: string }
            const el = document.createElement('div')
            el.innerHTML = '<strong></strong><br><small></small>'
            el.querySelector('strong')!.textContent = name
            el.querySelector('small')!.textContent = code
            new Popup({ closeButton: false }).setLngLat(e.lngLat).setDOMContent(el).addTo(m)
          })
          m.on('mouseenter', layer, () => (m.getCanvas().style.cursor = 'pointer'))
          m.on('mouseleave', layer, () => (m.getCanvas().style.cursor = ''))
        }

        map = m
        routes = rs
      } catch (e) {
        error = `Không tải được dữ liệu: ${(e as Error).message}`
      }
    })

    return () => m.remove()
  })
</script>

<main>
  <div class="map" bind:this={mapEl}></div>

  <section class="panel" bind:this={panelEl}>
    {#if error}
      <p class="error" role="alert">{error}</p>
    {/if}

    {#if selected}
      <header>
        <button class="back" onclick={() => (selected = null)} aria-label="Quay lại danh sách tuyến">←</button>
        <span class="badge" style:background={color(selected.color)} style:color={textOn(selected.color)}>
          {selected.shortName}
        </span>
        <div>
          <strong>{selected.longName}</strong>
          <small>{selected.agencyName}</small>
        </div>
      </header>

      {#if selected.directions.length > 1}
        <div class="tabs" role="tablist">
          {#each selected.directions as d (d.directionId)}
            <button
              role="tab"
              aria-selected={d.directionId === direction?.directionId}
              onclick={() => (directionId = d.directionId)}
            >
              Đi {d.headsign}
            </button>
          {/each}
        </div>
      {/if}

      <ol class="list stops">
        {#each direction?.stops ?? [] as s, i (i)}
          <li>{s.name}</li>
        {/each}
      </ol>
    {:else}
      <input type="search" placeholder="Tìm tuyến: số hoặc tên" bind:value={query} aria-label="Tìm tuyến" />
      <ul class="list">
        {#each filtered as r (r.id)}
          <li>
            <button class="route" onclick={() => select(r.id)}>
              <span class="badge" style:background={color(r.color)} style:color={textOn(r.color)}>{r.shortName}</span>
              <span>{r.longName}</span>
            </button>
          </li>
        {:else}
          <li class="empty">{routes.length ? 'Không có tuyến phù hợp' : 'Đang tải…'}</li>
        {/each}
      </ul>
    {/if}
  </section>
</main>

<style>
  main {
    position: relative;
    height: 100%;
  }

  .map {
    position: absolute;
    inset: 0;
  }

  /* Mobile-first: bảng ở đáy màn hình; màn rộng thì nằm bên trái. */
  .panel {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    max-height: 45%;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    padding: 0.75rem 0.75rem calc(0.75rem + env(safe-area-inset-bottom));
    background: var(--bg);
    border-radius: 12px 12px 0 0;
    box-shadow: 0 -2px 12px rgb(0 0 0 / 0.15);
  }

  @media (min-width: 768px) {
    .panel {
      top: 0.75rem;
      left: 0.75rem;
      right: auto;
      bottom: auto;
      width: 360px;
      max-height: calc(100% - 1.5rem);
      border-radius: 12px;
    }
  }

  input[type='search'] {
    width: 100%;
    padding: 0.6rem 0.75rem;
    font: inherit;
    border: 1px solid var(--line);
    border-radius: 8px;
  }

  .list {
    margin: 0;
    padding: 0;
    overflow-y: auto;
    list-style: none;
  }

  .route {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    width: 100%;
    padding: 0.5rem 0.25rem;
    font: inherit;
    text-align: left;
    background: none;
    border: 0;
    border-bottom: 1px solid var(--line);
    cursor: pointer;
  }

  .badge {
    flex: none;
    min-width: 3.2rem;
    padding: 0.2rem 0.4rem;
    font-weight: 600;
    text-align: center;
    border-radius: 6px;
  }

  header {
    display: flex;
    align-items: center;
    gap: 0.6rem;
  }

  header small {
    display: block;
    color: var(--muted);
  }

  .back {
    font-size: 1.25rem;
    background: none;
    border: 0;
    cursor: pointer;
  }

  .tabs {
    display: flex;
    gap: 0.25rem;
  }

  .tabs button {
    flex: 1;
    padding: 0.4rem;
    font: inherit;
    font-size: 0.85rem;
    background: none;
    border: 1px solid var(--line);
    border-radius: 8px;
    cursor: pointer;
  }

  .tabs button[aria-selected='true'] {
    color: #fff;
    background: var(--brand);
    border-color: var(--brand);
  }

  .stops {
    padding-left: 1.75rem;
    list-style: decimal;
  }

  .stops li {
    padding: 0.2rem 0;
  }

  .empty,
  .error {
    color: var(--muted);
  }

  .error {
    color: #b00020;
  }
</style>
