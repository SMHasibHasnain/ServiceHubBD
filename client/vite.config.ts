import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// In development the API runs on :5291; proxying /api keeps the browser same-origin (no CORS).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "/api": { target: process.env.API_URL ?? "http://localhost:5291", changeOrigin: true },
      "/uploads": { target: process.env.API_URL ?? "http://localhost:5291", changeOrigin: true },
      "/health": { target: process.env.API_URL ?? "http://localhost:5291", changeOrigin: true },
    },
  },
});
