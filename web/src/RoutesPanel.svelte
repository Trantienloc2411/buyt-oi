<script lang="ts">
  import type { GeoJSONSource, Map } from 'maplibre-gl'
  import type { Feature } from 'geojson'
  import { api, type RouteDetail, type RouteSummary, type StopInfo } from './lib/api'
  import { color, fold, textOn } from './lib/format'

  let { map, routes, fit }: { map: Map; routes: RouteSummary[]; fit: (coords: [number, number][]) => void } = $props()

  let query = $state('')
  let selected = $state<RouteDetail | null>(null)
  let directionId = $state(0)
  let error = $state<string | null>(null)

  const filtered = $derived.by(() => {
    const q = fold(query.trim())
    return q ? routes.filter((r) => fold(`${r.shortName} ${r.longName}`).includes(q)) : routes
  })
  const direction = $derived(
    selected?.directions.find((d) => d.directionId === directionId) ?? selected?.directions[0],
  )

  const point = (s: StopInfo, c: string): Feature => ({
    type: 'Feature',
    geometry: { type: 'Point', coordinates: [s.lon, s.lat] },
    properties: { name: s.name, code: s.code ?? '', color: c },
  })

  async function select(id: string) {
    try {
      selected = await api.route(id)
      directionId = selected.directions[0]?.directionId ?? 0
    } catch (e) {
      error = `Không tải được tuyến: ${(e as Error).message}`
    }
  }

  // Vẽ tuyến đang chọn: đường nối các trạm (chưa có shape thật) + các trạm. Rời panel → xoá.
  $effect(() => {
    const src = map.getSource<GeoJSONSource>('route')!
    if (selected && direction && direction.stops.length > 0) {
      const c = color(selected.color)
      const coords = direction.stops.map((s) => [s.lon, s.lat] as [number, number])
      src.setData({
        type: 'FeatureCollection',
        features: [
          { type: 'Feature', geometry: { type: 'LineString', coordinates: coords }, properties: { color: c } },
          ...direction.stops.map((s) => point(s, c)),
        ],
      })
      fit(coords)
    }
    return () => src.setData({ type: 'FeatureCollection', features: [] })
  })
</script>

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

<style>
  input[type='search'] {
    width: 100%;
    padding: 0.6rem 0.75rem;
    font: inherit;
    border: 1px solid var(--line);
    border-radius: 8px;
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

  .empty {
    color: var(--muted);
  }
</style>
