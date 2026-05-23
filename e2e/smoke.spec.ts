import { test, expect, type APIRequestContext } from "@playwright/test";
import { ensureFixtureLibraryRoot } from "./fixture-library";

const API_BASE = process.env.PRISMEDIA_E2E_API_URL ?? "http://127.0.0.1:8008/api";

type VideosResponse = {
  videos: Array<{ id: string; title: string }>;
};

async function waitForVideos(request: APIRequestContext) {
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
    const response = await request.get(`${API_BASE}/videos?limit=20&offset=0`);
    expect(response.ok()).toBeTruthy();
    const body = (await response.json()) as VideosResponse;
    if (body.videos.length > 0) {
      return body.videos;
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }

  throw new Error("Timed out waiting for fixture videos to appear");
}

test("dashboard, videos, video detail, subtitles, and jobs are reachable", async ({
  page,
  request,
}) => {
  await ensureFixtureLibraryRoot(request);

  const scanResponse = await request.post(`${API_BASE}/jobs/queues/library-scan/run`);
  expect(scanResponse.ok()).toBeTruthy();

  const videos = await waitForVideos(request);
  const firstVideo = videos[0]!;

  const subtitleUpload = await request.post(`${API_BASE}/videos/${firstVideo.id}/subtitles`, {
    multipart: {
      language: "en",
      label: "Fixture",
      file: {
        name: "fixture.ass",
        mimeType: "text/plain",
        buffer: Buffer.from(
          `[Script Info]
Title: Fixture

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,24,&H00FFFFFF,&H000000FF,&H00000000,&H66000000,0,0,0,0,100,100,0,0,1,2,0,2,20,20,20,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,Fixture subtitle
`,
        ),
      },
    },
  });
  expect(subtitleUpload.ok()).toBeTruthy();

  await page.goto("/");
  await expect(page.getByText("Recent Videos").first()).toBeVisible();

  await page.goto("/videos");
  await expect(page.getByRole("heading", { name: "Videos", level: 1 })).toBeVisible();

  await page.goto(`/videos/${firstVideo.id}`);
  await expect(page.getByText("Transcript").first()).toBeVisible();

  await page.goto("/jobs");
  await expect(page.getByText("Queues")).toBeVisible();
});
