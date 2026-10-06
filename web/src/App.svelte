<script lang="ts">
  import { onMount } from 'svelte'
  import { LngLatBounds, Map, NavigationControl, Popup, setWorkerUrl } from 'maplibre-gl'
  // MapLibre 6 tìm worker cạnh file JS của nó; Vite gộp lại nên đường dẫn sai → chỉ đường cho nó.
  import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
  import type { FeatureCollection } from 'geojson'
  import { api, type RouteSummary, type StopInfo } from './lib/api'
  import JourneyPanel from './JourneyPanel.svelte'
  import RoutesPanel from './RoutesPanel.svelte'

  // ponytail: nền bản đồ công cộng OpenFreeMap; chuyển sang PMTiles tự host (Protomaps) khi deploy.
  const STYLE = 'https://tiles.openfreemap.org/styles/liberty'
  const HCMC: [number, number] = [106.7, 10.78]
  const EMPTY: FeatureCollection = { type: 'FeatureCollection', features: [] }

  type Mode = 'journey' | 'routes'
  const MODES: [Mode, string][] = [
    ['journey', 'Tìm đường'],
    ['routes', 'Tuyến'],
  ]

  let mapEl: HTMLDivElement
  let panelEl: HTMLElement
  let map = $state<Map>()
  let mode = $state<Mode>('journey')
  let routes = $state<RouteSummary[]>([])
  let stops = $state<StopInfo[]>([])
  let error = $state<string | null>(null)

  // Thu bản đồ vào vùng không bị bảng che: màn hẹp bảng ở đáy, màn rộng bảng bên trái.
  function fit(coords: [number, number][]) {
    if (!map || coords.length === 0) return
    const bounds = coords.reduce((b, p) => b.extend(p), new LngLatBounds(coords[0], coords[0]))
    const wide = window.matchMedia('(min-width: 768px)').matches
    const padding = wide
      ? { top: 60, right: 40, bottom: 40, left: panelEl.offsetWidth + 40 }
      : { top: 60, right: 40, bottom: panelEl.offsetHeight + 20, left: 40 }
    map.fitBounds(bounds, { padding, maxZoom: 15 })
  }

  onMount(() => {
    setWorkerUrl(workerUrl)
    const m = new Map({ container: mapEl, style: STYLE, center: HCMC, zoom: 11 })
    m.addControl(new NavigationControl({ showCompass: false }), 'top-right')

    m.on('load', async () => {
      try {
        const [rs, ss] = await Promise.all([api.routes(), api.stops()])
        m.addSource('stops', {
          type: 'geojson',
          data: {
            type: 'FeatureCollection',
            features: ss.map((s) => ({
              type: 'Feature',
              geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
              properties: { name: s.name, code: s.code ?? '' },
            })),
          },
        })
        m.addLayer({
          id: 'stops',
          type: 'circle',
          source: 'stops',
          minzoom: 13,
          paint: { 'circle-radius': 3, 'circle-color': '#777', 'circle-stroke-width': 1, 'circle-stroke-color': '#fff' },
        })

        // Tuyến đang xem (RoutesPanel) và hành trình đang chọn (JourneyPanel); mỗi panel tự ghi/xoá dữ liệu của mình.
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
        m.addSource('journey', { type: 'geojson', data: EMPTY })
        m.addLayer({
          id: 'journey-walk',
          type: 'line',
          source: 'journey',
          filter: ['all', ['==', '$type', 'LineString'], ['==', 'walk', true]],
          layout: { 'line-cap': 'round' },
          paint: { 'line-color': '#555', 'line-width': 3, 'line-dasharray': [0.5, 2] },
        })
        m.addLayer({
          id: 'journey-ride',
          type: 'line',
          source: 'journey',
          filter: ['all', ['==', '$type', 'LineString'], ['==', 'walk', false]],
          layout: { 'line-join': 'round', 'line-cap': 'round' },
          paint: { 'line-color': ['get', 'color'], 'line-width': 6 },
        })
        m.addLayer({
          id: 'journey-stops',
          type: 'circle',
          source: 'journey',
          filter: ['==', '$type', 'Point'],
          paint: { 'circle-radius': 6, 'circle-color': '#fff', 'circle-stroke-width': 3, 'circle-stroke-color': ['get', 'color'] },
        })

        // Popup tên trạm khi xem tuyến; ở chế độ tìm đường, chạm trạm là chọn điểm (JourneyPanel).
        for (const layer of ['stops', 'route-stops']) {
          m.on('click', layer, (e) => {
            const f = e.features?.[0]
            if (!f || mode !== 'routes') return
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
        stops = ss
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
    <div class="modes" role="tablist">
      {#each MODES as [m, label] (m)}
        <button role="tab" aria-selected={mode === m} onclick={() => (mode = m)}>{label}</button>
      {/each}
    </div>

    {#if error}
      <p class="error" role="alert">{error}</p>
    {/if}

    {#if !map}
      <p class="loading">Đang tải…</p>
    {:else if mode === 'journey'}
      <JourneyPanel {map} {stops} {fit} />
    {:else}
      <RoutesPanel {map} {routes} {fit} />
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
    max-height: 55%;
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
      width: 380px;
      max-height: calc(100% - 1.5rem);
      border-radius: 12px;
    }
  }

  .modes {
    display: flex;
    gap: 0.25rem;
    padding: 0.2rem;
    background: #f0f0f0;
    border-radius: 10px;
  }

  .modes button {
    flex: 1;
    padding: 0.4rem;
    font: inherit;
    font-weight: 600;
    color: var(--muted);
    background: none;
    border: 0;
    border-radius: 8px;
    cursor: pointer;
  }

  .modes button[aria-selected='true'] {
    color: var(--text);
    background: var(--bg);
    box-shadow: 0 1px 3px rgb(0 0 0 / 0.15);
  }

  .loading {
    color: var(--muted);
  }
</style>
