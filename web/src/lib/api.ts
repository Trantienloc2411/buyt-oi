// Kiểu sinh từ OpenAPI của backend: `npm run api` (cần backend đang chạy).
import type { components } from './openapi'

export type RouteSummary = components['schemas']['RouteSummary']
export type RouteDetail = components['schemas']['RouteDetail']
export type StopInfo = components['schemas']['StopInfo']
export type Journey = components['schemas']['Journey']
export type JourneyLeg = components['schemas']['JourneyLeg']
export type LegPlace = components['schemas']['LegPlace']

export type Point = { lat: number; lon: number }

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(path, { signal })
  if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`)
  return res.json() as Promise<T>
}

export const api = {
  routes: () => get<RouteSummary[]>('/api/routes'),
  route: (id: string) => get<RouteDetail>(`/api/routes/${encodeURIComponent(id)}`),
  stops: () => get<StopInfo[]>('/api/stops'),
  /** @param departAt giờ địa phương "YYYY-MM-DDTHH:mm"; bỏ trống = đi ngay. */
  journeys: (from: Point, to: Point, departAt: string, signal?: AbortSignal) => {
    const q = new URLSearchParams({
      fromLat: `${from.lat}`,
      fromLon: `${from.lon}`,
      toLat: `${to.lat}`,
      toLon: `${to.lon}`,
    })
    if (departAt) q.set('departAt', departAt)
    return get<Journey[]>(`/api/journeys?${q}`, signal)
  },
}
