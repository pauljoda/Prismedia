import { sveltekit } from "@sveltejs/kit/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [tailwindcss(), sveltekit()],
  server: {
    port: 5173,
    strictPort: false,
    hmr: {
      clientPort: 5173,
    },
    proxy: {
      "/api": {
        target: "http://localhost:8008",
        changeOrigin: true,
      },
    },
  },
  optimizeDeps: {
    exclude: ["jassub"],
  },
  ssr: {
    noExternal: [],
    external: ["jassub", "jsdom"],
  },
  define: {
    "process.env.PUBLIC_API_URL": JSON.stringify(""),
    "process.env.API_URL": JSON.stringify(""),
  },
});
