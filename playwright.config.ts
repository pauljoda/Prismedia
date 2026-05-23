import { defineConfig } from "@playwright/test";

const baseURL = process.env.PRISMEDIA_E2E_WEB_URL ?? "http://127.0.0.1:8008";
const svelteURL = process.env.PRISMEDIA_E2E_SVELTE_URL ?? "http://127.0.0.1:8009";
const svelteHost = new URL(svelteURL).hostname;
const manageSvelteServer = svelteHost === "127.0.0.1" || svelteHost === "localhost";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  reporter: [
    ["list"],
    ["html", { open: "never", outputFolder: "playwright-report" }],
  ],
  expect: {
    timeout: 10_000,
  },
  use: {
    baseURL,
    viewport: { width: 1440, height: 1200 },
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        browserName: "chromium",
      },
    },
  ],
  webServer: manageSvelteServer
    ? {
        command: "pnpm --filter @prismedia/web-svelte dev",
        url: svelteURL,
        reuseExistingServer: true,
        timeout: 120_000,
      }
    : undefined,
});
