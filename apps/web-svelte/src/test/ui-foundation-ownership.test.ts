import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { parse } from "svelte/compiler";
import { describe, expect, it } from "vitest";

const sourceRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

function componentFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return entry.name === "dev" ? [] : componentFiles(path);
    return entry.name.endsWith(".svelte") && !entry.name.includes(".test") ? [path] : [];
  });
}

describe("shared UI foundation ownership", () => {
  it("composes production controls from the UI package, retaining native file pickers", () => {
    const violations: string[] = [];
    for (const path of componentFiles(sourceRoot)) {
      const source = readFileSync(path, "utf8");
      const ast = parse(source, { modern: true });
      function walk(value: unknown): void {
        if (!value || typeof value !== "object") return;
        const node = value as Record<string, unknown>;
        if (node.type === "RegularElement" && ["button", "input", "select", "textarea", "dialog"].includes(String(node.name))) {
          const element = node as unknown as {
            name: string; start: number;
            attributes: Array<{ type: string; name?: string; value?: Array<{ type: string; data?: string }> }>;
          };
          const nativeFilePicker = element.name === "input" && element.attributes.some(attribute =>
            attribute.type === "Attribute" && attribute.name === "type" &&
            Array.isArray(attribute.value) && attribute.value.length === 1 &&
            attribute.value[0].type === "Text" && attribute.value[0].data === "file");
          if (!nativeFilePicker) {
            const line = source.slice(0, element.start).split("\n").length;
            violations.push(`${relative(sourceRoot, path)}:${line} <${element.name}>`);
          }
        }
        for (const child of Object.values(node)) {
          if (Array.isArray(child)) child.forEach(walk);
          else if (child && typeof child === "object") walk(child);
        }
      }
      walk(ast.fragment);
    }
    expect(violations, "Use shared primitives; media engines and native file pickers retain their browser APIs.").toEqual([]);
  });
});
