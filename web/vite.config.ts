import { svelte } from '@sveltejs/vite-plugin-svelte'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [svelte()],
  server: {
    // Backend: `dotnet run --project src/Apps/BuytOi.Api` (mặc định http://localhost:5000; khác thì đặt API_URL).
    proxy: { '/api': process.env.API_URL ?? 'http://localhost:5000' },
  },
})
