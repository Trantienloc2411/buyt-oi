import { svelte } from '@sveltejs/vite-plugin-svelte'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [svelte()],
  server: {
    // Backend: `dotnet run --project src/BuytOi.Host` (mặc định http://localhost:5000).
    proxy: { '/api': 'http://localhost:5000' },
  },
})
