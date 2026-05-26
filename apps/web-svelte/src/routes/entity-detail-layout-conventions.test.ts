import { describe, expect, it } from "vitest";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const routesDir = dirname(fileURLToPath(import.meta.url));

describe("EntityDetail route layout conventions", () => {
  it("lets EntityDetail own its page padding and width", () => {
    const offenders = routeFiles(routesDir)
      .filter((file) => !file.includes(`${join("routes", "dev")}${"/"}`))
      .filter((file) => readFileSync(file, "utf8").includes("<EntityDetail"))
      .flatMap((file) => {
        const source = readFileSync(file, "utf8");
        const selectors = [".detail-page", ".image-detail-back-page"];
        return selectors
          .filter((selector) =>
            new RegExp(`\\${selector}\\s*\\{[^}]*?(max-width:\\s*72rem|padding:\\s*clamp\\(|padding-bottom:\\s*10rem)`, "s")
              .test(source))
          .map((selector) => `${relative(routesDir, file)} ${selector}`);
      });

    expect(offenders).toEqual([]);
  });
});

function routeFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) return routeFiles(path);
    return path.endsWith(".svelte") ? [path] : [];
  });
}
