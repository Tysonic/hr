import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on http://localhost:5262 (dotnet run --project api/src/TyHr.Api).
// Proxying /api keeps the browser on one origin, so no CORS setup is needed in development.
// Port 5174 (not Vite's 5173) so TyHR can run beside the other Tysoft web apps; Keycloak's tyhr-web client expects it.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': { target: process.env.API_URL ?? 'http://localhost:5262', changeOrigin: true },
    },
  },
})
