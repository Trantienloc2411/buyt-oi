<script lang="ts">
  import { Marker, type GeoJSONSource, type Map, type MapMouseEvent } from 'maplibre-gl'
  import type { Feature, Point as GeoPoint } from 'geojson'
  import { api, type Journey, type JourneyLeg, type Point, type StopInfo } from './lib/api'
  import { color, fold, hhmm, meters, minutes, textOn } from './lib/format'

  let { map, stops, fit }: { map: Map; stops: StopInfo[]; fit: (coords: [number, number][]) => void } = $props()

  type End = 'from' | 'to'
  type Place = Point & { name: string }
  const ENDS: End[] = ['from', 'to']
  const LABEL: Record<End, string> = { from: 'Điểm đi', to: 'Điểm đến' }
  const ON_MAP = 'Điểm trên bản đồ'

  let places = $state<Record<End, Place | null>>({ from: null, to: null })
  let texts = $state<Record<End, string>>({ from: '', to: '' })
  let focused = $state<End | null>(null)
  let pick: End | null = null // ô vừa được chọn → nhận điểm khi chạm bản đồ
  let departAt = $state('') // rỗng = đi ngay
  let journeys = $state<Journey[] | null>(null)
  let selected = $state(0)
  let loading = $state(false)
  let error = $state<string | null>(null)

  const suggestions = $derived.by(() => {
    if (!focused || texts[focused] === places[focused]?.name) return []
    const q = fold(texts[focused].trim())
    return q.length < 2 ? [] : stops.filter((s) => fold(s.name).includes(q)).slice(0, 6)
  })

  function set(end: End, place: Place | null) {
    places[end] = place
    texts[end] = place?.name ?? ''
  }

  function swap() {
    const { from, to } = places
    set('from', to)
    set('to', from)
  }

  function locate() {
    navigator.geolocation.getCurrentPosition(
      (p) => set('from', { name: 'Vị trí của tôi', lat: p.coords.latitude, lon: p.coords.longitude }),
      () => (error = 'Không lấy được vị trí. Hãy cho phép truy cập vị trí hoặc chọn trên bản đồ.'),
      { enableHighAccuracy: true, timeout: 10_000 },
    )
  }

  // Chạm bản đồ: vào trạm thì lấy trạm, không thì lấy toạ độ. Ô nhận: ô vừa chọn, hoặc ô còn trống.
  $effect(() => {
    const onClick = (e: MapMouseEvent) => {
      const f = map.queryRenderedFeatures(e.point, { layers: ['stops'] })[0]
      const [lon, lat] = f ? (f.geometry as GeoPoint).coordinates : [e.lngLat.lng, e.lngLat.lat]
      set(pick ?? (places.from ? 'to' : 'from'), { name: f ? (f.properties.name as string) : ON_MAP, lat, lon })
      pick = null
    }
    map.on('click', onClick)
    return () => map.off('click', onClick)
  })

  // Ghim điểm đi/đến, kéo được để chỉnh.
  const markers: Record<End, Marker> = {
    from: new Marker({ color: '#0b7a4b', draggable: true }),
    to: new Marker({ color: '#c62828', draggable: true }),
  }
  for (const end of ENDS) {
    markers[end].on('dragend', () => {
      const { lat, lng } = markers[end].getLngLat()
      set(end, { name: ON_MAP, lat, lon: lng })
    })
  }
  $effect(() => {
    for (const end of ENDS) {
      const p = places[end]
      if (p) markers[end].setLngLat([p.lon, p.lat]).addTo(map)
      else markers[end].remove()
    }
  })
  $effect(() => () => ENDS.forEach((end) => markers[end].remove()))

  // Đủ hai điểm (hoặc đổi giờ) → tìm lại; huỷ request cũ nếu người dùng đổi tiếp.
  $effect(() => {
    const { from, to } = places
    const when = departAt
    journeys = null
    selected = 0
    error = null
    if (!from || !to) return
    const ctrl = new AbortController()
    loading = true
    // Tắt loading cùng lúc gán kết quả: hành trình được vẽ (và fit theo chiều cao bảng) khi danh sách đã hiện.
    api
      .journeys(from, to, when, ctrl.signal)
      .then((js) => {
        loading = false
        journeys = js
      })
      .catch((e) => {
        if (ctrl.signal.aborted) return
        loading = false
        error = `Không tìm được đường: ${(e as Error).message}`
      })
    return () => ctrl.abort()
  })

  const path = (leg: JourneyLeg): [number, number][] =>
    (leg.mode === 'Transit' && leg.stops ? leg.stops : [leg.from, leg.to]).map((p) => [p.lon, p.lat])

  // Vẽ hành trình đang chọn: chặng xe theo màu tuyến, chặng đi bộ nét đứt. Rời panel → xoá.
  $effect(() => {
    const src = map.getSource<GeoJSONSource>('journey')!
    const j = journeys?.[selected]
    if (j) {
      const rides = j.legs.filter((l) => l.mode === 'Transit')
      src.setData({
        type: 'FeatureCollection',
        features: [
          ...j.legs.map(
            (leg): Feature => ({
              type: 'Feature',
              geometry: { type: 'LineString', coordinates: path(leg) },
              properties: { walk: leg.mode === 'Walk', color: color(leg.routeColor) },
            }),
          ),
          ...rides.flatMap((leg) =>
            [leg.from, leg.to].map(
              (p): Feature => ({
                type: 'Feature',
                geometry: { type: 'Point', coordinates: [p.lon, p.lat] },
                properties: { color: color(leg.routeColor) },
              }),
            ),
          ),
        ],
      })
      fit(j.legs.flatMap(path))
    }
    return () => src.setData({ type: 'FeatureCollection', features: [] })
  })

  const rideCount = (j: Journey) => j.legs.filter((l) => l.mode === 'Transit').length
  const walkMinutes = (leg: JourneyLeg) => Math.max(1, minutes(leg.departure, leg.arrival))
</script>

{#if error}
  <p class="error" role="alert">{error}</p>
{/if}

<div class="ends">
  <div class="fields">
    {#each ENDS as end (end)}
      <label class="field">
        <span class="dot {end}" aria-hidden="true"></span>
        <input
          type="search"
          placeholder={`${LABEL[end]}: tên trạm hoặc chạm bản đồ`}
          aria-label={LABEL[end]}
          bind:value={texts[end]}
          onfocus={() => (focused = pick = end)}
          onblur={() => (focused = null)}
          oninput={() => {
            if (!texts[end].trim()) places[end] = null
          }}
        />
      </label>
    {/each}
  </div>
  <div class="actions">
    <button onclick={locate} title="Dùng vị trí của tôi làm điểm đi" aria-label="Vị trí của tôi">◎</button>
    <button onclick={swap} title="Đổi chiều" aria-label="Đổi điểm đi và điểm đến">⇅</button>
  </div>
</div>

{#if focused && suggestions.length}
  <ul class="list suggestions">
    {#each suggestions as s (s.id)}
      <li>
        <!-- mousedown giữ focus ở ô nhập để danh sách không biến mất trước khi click -->
        <button
          onmousedown={(e) => e.preventDefault()}
          onclick={() => {
            set(focused!, { name: s.name, lat: s.lat, lon: s.lon })
            ;(document.activeElement as HTMLElement | null)?.blur()
          }}
        >
          {s.name} <small>{s.code}</small>
        </button>
      </li>
    {/each}
  </ul>
{/if}

<div class="when">
  <input type="datetime-local" bind:value={departAt} aria-label="Giờ khởi hành" />
  {#if departAt}
    <button onclick={() => (departAt = '')}>Đi ngay</button>
  {:else}
    <span>Đi ngay</span>
  {/if}
</div>

{#if loading}
  <p class="muted">Đang tìm đường…</p>
{:else if journeys?.length === 0}
  <p class="muted">Không tìm thấy đường đi. Thử chọn điểm gần trạm hơn (trong khoảng 800 m) hoặc đổi giờ.</p>
{:else if journeys}
  <ul class="list results">
    {#each journeys as j, i (i)}
      <li>
        <button class="journey" aria-pressed={i === selected} onclick={() => (selected = i)}>
          <span class="time">
            <strong>{hhmm(j.departure)} → {hhmm(j.arrival)}</strong>
            <span>{minutes(j.departure, j.arrival)} phút</span>
          </span>
          <span class="chain">
            {#each j.legs as leg, k (k)}
              {#if k}<span class="sep" aria-hidden="true">›</span>{/if}
              {#if leg.mode === 'Walk'}
                <span class="walk" title="Đi bộ">🚶{walkMinutes(leg)}′</span>
              {:else}
                <span class="badge" style:background={color(leg.routeColor)} style:color={textOn(leg.routeColor)}>
                  {leg.routeShortName}
                </span>
              {/if}
            {/each}
          </span>
          <small>
            {rideCount(j) === 0 ? 'Đi bộ' : j.transfers === 0 ? 'Đi thẳng' : `${j.transfers} lần đổi tuyến`}
          </small>
        </button>

        {#if i === selected}
          <ol class="steps">
            {#each j.legs as leg, k (k)}
              {#if leg.mode === 'Walk'}
                <li class="walk">
                  Đi bộ {meters(leg.distanceMeters)} · {walkMinutes(leg)} phút tới
                  {leg.to.stopId ? leg.to.name : 'điểm đến'}
                </li>
              {:else}
                {@const approx = leg.approximate ? '~' : ''}
                <li class="ride" style:--c={color(leg.routeColor)}>
                  <div>
                    <span class="badge" style:background={color(leg.routeColor)} style:color={textOn(leg.routeColor)}>
                      {leg.routeShortName}
                    </span>
                    {#if leg.headsign}hướng {leg.headsign}{/if}
                  </div>
                  <div>{approx}{hhmm(leg.departure)} lên tại <strong>{leg.from.name}</strong></div>
                  {#if (leg.stops?.length ?? 0) > 2}
                    <details>
                      <summary>Đi qua {leg.stopCount} trạm</summary>
                      <ol>
                        {#each leg.stops!.slice(1, -1) as s, n (n)}
                          <li>{s.name}</li>
                        {/each}
                      </ol>
                    </details>
                  {/if}
                  <div>{approx}{hhmm(leg.arrival)} xuống tại <strong>{leg.to.name}</strong></div>
                </li>
              {/if}
            {/each}
          </ol>
          {#if j.legs.some((l) => l.approximate)}
            <p class="note">~ giờ ước tính: dữ liệu chỉ có giờ xuất bến, giờ tại các trạm giữa được nội suy.</p>
          {/if}
        {/if}
      </li>
    {/each}
  </ul>
{:else}
  <p class="muted">Chọn điểm đi và điểm đến: gõ tên trạm, chạm lên bản đồ, hoặc dùng vị trí của bạn (◎).</p>
{/if}

<style>
  .ends {
    display: flex;
    gap: 0.5rem;
  }

  .fields {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
  }

  .field {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .dot {
    flex: none;
    width: 0.7rem;
    height: 0.7rem;
    border-radius: 50%;
    background: var(--brand);
  }

  .dot.to {
    background: #c62828;
  }

  input {
    width: 100%;
    padding: 0.55rem 0.7rem;
    font: inherit;
    border: 1px solid var(--line);
    border-radius: 8px;
  }

  .actions {
    display: flex;
    flex-direction: column;
    justify-content: space-around;
  }

  .actions button,
  .when button {
    padding: 0.3rem 0.55rem;
    font: inherit;
    background: none;
    border: 1px solid var(--line);
    border-radius: 8px;
    cursor: pointer;
  }

  .suggestions button {
    width: 100%;
    padding: 0.45rem 0.25rem;
    font: inherit;
    text-align: left;
    background: none;
    border: 0;
    border-bottom: 1px solid var(--line);
    cursor: pointer;
  }

  .suggestions small,
  .muted,
  .note,
  .journey small {
    color: var(--muted);
  }

  .when {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .when input {
    width: auto;
    flex: 1;
  }

  .when span {
    color: var(--muted);
    white-space: nowrap;
  }

  .muted,
  .note {
    margin: 0;
    font-size: 0.9rem;
  }

  .journey {
    display: flex;
    flex-direction: column;
    gap: 0.3rem;
    width: 100%;
    padding: 0.6rem 0.5rem;
    font: inherit;
    text-align: left;
    background: none;
    border: 0;
    border-bottom: 1px solid var(--line);
    border-left: 3px solid transparent;
    cursor: pointer;
  }

  .journey[aria-pressed='true'] {
    border-left-color: var(--brand);
    background: #f3f8f5;
  }

  .time {
    display: flex;
    justify-content: space-between;
  }

  .chain {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.3rem;
  }

  .chain .badge {
    min-width: 2.5rem;
  }

  .sep {
    color: var(--muted);
  }

  .steps {
    margin: 0;
    padding: 0.5rem 0.5rem 0.5rem 1.25rem;
    list-style: none;
  }

  .steps li {
    padding: 0.35rem 0 0.35rem 0.75rem;
    border-left: 3px dotted #999;
  }

  .steps li.ride {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    border-left: 4px solid var(--c);
  }

  details ol {
    margin: 0.25rem 0;
    padding-left: 1.25rem;
    font-size: 0.9rem;
    color: var(--muted);
  }

  summary {
    font-size: 0.9rem;
    color: var(--muted);
    cursor: pointer;
  }
</style>
