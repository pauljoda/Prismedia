import { describe, expect, it } from "vitest";
import { DOWNLOAD_CLIENT_KIND, DOWNLOAD_PROTOCOL } from "$lib/api/generated/codes";
import type { DownloadClientSummary } from "$lib/api/generated/model";
import { availableDownloadProtocols } from "./acquisition-protocol-preference";

describe("availableDownloadProtocols", () => {
  it("returns both protocol types once when enabled clients support both", () => {
    expect(availableDownloadProtocols([
      client(DOWNLOAD_CLIENT_KIND.sabnzbd),
      client(DOWNLOAD_CLIENT_KIND.qBittorrent),
      client(DOWNLOAD_CLIENT_KIND.transmission),
      client(DOWNLOAD_CLIENT_KIND.slskd),
    ])).toEqual([DOWNLOAD_PROTOCOL.usenet, DOWNLOAD_PROTOCOL.torrent, DOWNLOAD_PROTOCOL.soulseek]);
  });

  it("does not advertise a protocol supported only by disabled clients", () => {
    expect(availableDownloadProtocols([
      client(DOWNLOAD_CLIENT_KIND.sabnzbd),
      client(DOWNLOAD_CLIENT_KIND.qBittorrent, false),
    ])).toEqual([DOWNLOAD_PROTOCOL.usenet]);
  });
});

function client(kind: DownloadClientSummary["kind"], enabled = true): DownloadClientSummary {
  return {
    id: `${kind}-client`,
    kind,
    displayName: kind,
    baseUrl: "http://download-client.test",
    username: null,
    category: "prismedia",
    enabled,
    hasPassword: false,
  };
}
