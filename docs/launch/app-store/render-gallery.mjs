import { chromium } from "@playwright/test";
import { mkdir, stat } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const outputDirectory = path.join(here, "screenshots");
const galleryUrl = new URL("./gallery.html", import.meta.url);

const captures = [
  ...Array.from({ length: 6 }, (_, index) => ({
    id: `iphone-${String(index + 1).padStart(2, "0")}`,
    width: 1320,
    height: 2868,
    directory: "iphone-6.9",
    filename: `${String(index + 1).padStart(2, "0")}.png`,
  })),
  ...Array.from({ length: 6 }, (_, index) => ({
    id: `ipad-${String(index + 1).padStart(2, "0")}`,
    width: 2064,
    height: 2752,
    directory: "ipad-13",
    filename: `${String(index + 1).padStart(2, "0")}.png`,
  })),
  ...Array.from({ length: 2 }, (_, index) => ({
    id: `tvos-${String(index + 1).padStart(2, "0")}`,
    width: 3840,
    height: 2160,
    directory: "apple-tv",
    filename: `${String(index + 1).padStart(2, "0")}.png`,
  })),
];

const browser = await chromium.launch({ headless: true });

try {
  for (const capture of captures) {
    const captureDirectory = path.join(outputDirectory, capture.directory);
    await mkdir(captureDirectory, { recursive: true });

    const page = await browser.newPage({
      viewport: { width: capture.width, height: capture.height },
      deviceScaleFactor: 1,
    });

    const url = new URL(galleryUrl);
    url.searchParams.set("slide", capture.id);
    await page.goto(url.href, { waitUntil: "networkidle" });
    await page.evaluate(() => document.fonts.ready);

    const target = page.locator(`#${capture.id}`);
    const bounds = await target.boundingBox();
    if (
      !bounds ||
      Math.round(bounds.width) !== capture.width ||
      Math.round(bounds.height) !== capture.height
    ) {
      throw new Error(
        `${capture.id} rendered at ${bounds?.width}×${bounds?.height}; expected ${capture.width}×${capture.height}`,
      );
    }

    const overflow = await page.locator("[data-copy]").evaluateAll((elements) =>
      elements
        .filter((element) => {
          const style = getComputedStyle(element);
          const visible = style.display !== "none" && style.visibility !== "hidden";
          return (
            visible &&
            (element.scrollWidth > element.clientWidth + 1 ||
              element.scrollHeight > element.clientHeight + 1)
          );
        })
        .map((element) => ({
          text: element.textContent?.trim().slice(0, 100),
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          clientHeight: element.clientHeight,
          scrollHeight: element.scrollHeight,
        })),
    );
    if (overflow.length > 0) {
      throw new Error(`${capture.id} has clipped copy: ${JSON.stringify(overflow)}`);
    }

    const outputPath = path.join(captureDirectory, capture.filename);
    await target.screenshot({ path: outputPath, type: "png" });

    const output = await stat(outputPath);
    if (output.size > 10_000_000) {
      throw new Error(
        `${capture.directory}/${capture.filename} is ${output.size} bytes; App Store screenshots must remain below 10 MB`,
      );
    }

    await page.close();
    console.log(
      `Rendered ${capture.directory}/${capture.filename} (${capture.width}×${capture.height}, ${Math.round(output.size / 1024)} KB)`,
    );
  }
} finally {
  await browser.close();
}
