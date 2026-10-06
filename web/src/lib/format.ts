// Tìm không dấu: "ben thanh" khớp "Bến Thành".
export const fold = (s: string) => s.normalize('NFD').replace(/\p{M}/gu, '').replace(/đ/gi, 'd').toLowerCase()

export const color = (hex: string | null | undefined) => `#${hex ?? '0b7a4b'}`

// Chữ đen trên nền sáng, chữ trắng trên nền tối (độ sáng tương đối).
export function textOn(hex: string | null | undefined) {
  const n = parseInt(hex ?? '0b7a4b', 16)
  const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255]
  return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? '#000' : '#fff'
}

// API trả giờ địa phương không kèm múi giờ ("2026-10-07T07:34:00") → cắt chuỗi, không qua Date để khỏi lệch múi giờ.
export const hhmm = (t: string) => t.slice(11, 16)

export const minutes = (from: string, to: string) => Math.round((Date.parse(to) - Date.parse(from)) / 60_000)

export const meters = (m: number) => (m < 1000 ? `${Math.round(m / 10) * 10} m` : `${(m / 1000).toFixed(1)} km`)
