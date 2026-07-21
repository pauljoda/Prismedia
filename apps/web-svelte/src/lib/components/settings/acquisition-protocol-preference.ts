import { DOWNLOAD_CLIENT_KIND, DOWNLOAD_PROTOCOL, type DownloadProtocolCode } from "$lib/api/generated/codes";
import type { DownloadClientSummary } from "$lib/api/generated/model";

/** Returns the distinct release protocols that enabled download clients can actually handle. */
export function availableDownloadProtocols(clients: DownloadClientSummary[]): DownloadProtocolCode[] {
  const available: DownloadProtocolCode[] = [];
  for (const client of clients) {
    if (!client.enabled) continue;
    const protocol = client.kind === DOWNLOAD_CLIENT_KIND.sabnzbd
      ? DOWNLOAD_PROTOCOL.usenet
      : client.kind === DOWNLOAD_CLIENT_KIND.slskd
        ? DOWNLOAD_PROTOCOL.soulseek
        : DOWNLOAD_PROTOCOL.torrent;
    if (!available.includes(protocol)) available.push(protocol);
  }
  return available;
}
