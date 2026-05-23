import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: [
      "packages/*/src/**/*.test.ts",
      "tests/**/*.test.ts",
    ],
    exclude: [
      "**/*.integration.test.ts",
      "**/dist/**",
    ],
    coverage: {
      provider: "v8",
      reporter: ["text", "html", "lcov"],
      reportsDirectory: "./coverage/unit",
      thresholds: {
        lines: 65,
        statements: 65,
        functions: 65,
        branches: 50,
      },
    },
  },
});
