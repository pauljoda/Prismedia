import { chromium } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const outputDirectory = path.join(here, "assets");
const galleryUrl = new URL("./gallery.html", import.meta.url);

const captures = [
  { id: "thumbnail", width: 240, height: 240, filename: "prismedia-thumbnail.png" },
  { id: "slide-01", width: 1270, height: 760, filename: "01-one-private-home.png" },
  { id: "slide-02", width: 1270, height: 760, filename: "02-one-media-lifecycle.png" },
  { id: "slide-03", width: 1270, height: 760, filename: "03-native-playback.png" },
  { id: "slide-04", width: 1270, height: 760, filename: "04-custom-reader.png" },
  { id: "slide-05", width: 1270, height: 760, filename: "05-purpose-built-media.png" },
  { id: "slide-06", width: 1270, height: 760, filename: "06-self-hosted.png" },
];

await mkdir(outputDirectory, { recursive: true });

const browser = await chromium.launch({ headless: true });

try {
  for (const capture of captures) {
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
          text: element.textContent?.trim().slice(0, 80),
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          clientHeight: element.clientHeight,
          scrollHeight: element.scrollHeight,
        })),
    );

    if (overflow.length > 0) {
      throw new Error(`${capture.id} has clipped copy: ${JSON.stringify(overflow)}`);
    }

    await target.screenshot({
      path: path.join(outputDirectory, capture.filename),
      type: "png",
    });

    await page.close();
    console.log(`Rendered ${capture.filename}`);
  }
} finally {
  await browser.close();
}
