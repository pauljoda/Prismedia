import { describe, expect, it } from "vitest";
import { parseTrickplayImagePlaylist, parseTrickplayVtt } from "./trickplay";

describe("trickplay parsing", () => {
  it("parses VTT sprite maps", () => {
    const frames = parseTrickplayVtt(`
WEBVTT

00:00:00.000 --> 00:00:10.000
/assets/videos/1/sprite.jpg#xywh=0,0,160,90
`);

    expect(frames).toEqual([
      {
        start: 0,
        end: 10,
        x: 0,
        y: 0,
        width: 160,
        height: 90,
        url: "/assets/videos/1/sprite.jpg",
      },
    ]);
  });

  it("expands Jellyfin image playlists into tile frames", () => {
    const frames = parseTrickplayImagePlaylist(`
#EXTM3U
#EXT-X-IMAGES-ONLY
#EXT-X-TILES:RESOLUTION=320x180,LAYOUT=2x2,DURATION=5
#EXTINF:20,
0.jpg
`, "http://localhost/Videos/1/Trickplay/320/tiles.m3u8");

    expect(frames).toHaveLength(4);
    expect(frames[0]).toMatchObject({
      start: 0,
      end: 5,
      x: 0,
      y: 0,
      width: 320,
      height: 180,
      url: "http://localhost/Videos/1/Trickplay/320/0.jpg",
    });
    expect(frames[3]).toMatchObject({
      start: 15,
      end: 20,
      x: 320,
      y: 180,
    });
  });
});
