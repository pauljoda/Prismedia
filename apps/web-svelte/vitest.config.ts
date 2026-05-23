import { svelte } from "@sveltejs/vite-plugin-svelte";
import { resolve } from "node:path";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [svelte({ hot: !process.env.VITEST })],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.{test,spec}.{ts,js}"],
    coverage: {
      provider: "v8",
      reporter: ["text", "html", "lcov"],
      reportsDirectory: "../../coverage/web-svelte",
    },
  },
  resolve: {
    conditions: process.env.VITEST ? ["browser"] : undefined,
    alias: {
      $lib: resolve("./src/lib"),
      "$app/navigation": resolve("./src/test/mocks/app-navigation.ts"),
      "$app/environment": resolve("./src/test/mocks/app-environment.ts"),
      "$app/paths": resolve("./src/test/mocks/app-paths.ts"),
      "$app/state": resolve("./src/test/mocks/app-state.ts"),
      "$env/static/public": resolve("./src/test/mocks/env-static-public.ts"),
      "$env/dynamic/public": resolve("./src/test/mocks/env-dynamic-public.ts"),
      "$env/dynamic/private": resolve("./src/test/mocks/env-dynamic-private.ts"),
    },
  },
});
