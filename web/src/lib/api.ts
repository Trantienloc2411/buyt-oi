// Kiểu sinh từ OpenAPI của backend: `npm run api` (cần backend đang chạy).
import type { components } from './openapi'

export type RouteSummary = components['schemas']['RouteSummary']
export type RouteDetail = components['schemas']['RouteDetail']
export type StopInfo = components['schemas']['StopInfo']

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path)
  if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`)
  return res.json() as Promise<T>
}

export const api = {
  routes: () => get<RouteSummary[]>('/api/routes'),
  route: (id: string) => get<RouteDetail>(`/api/routes/${encodeURIComponent(id)}`),
  stops: () => get<StopInfo[]>('/api/stops'),
}
