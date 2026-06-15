<script lang="ts">
  import type { VideoFileInfoModel } from "$lib/entities/media-view-models";

  interface Props {
    video: VideoFileInfoModel;
  }

  let { video }: Props = $props();

  function formatBitRate(bps: number | null | undefined): string {
    if (!bps) return "\u2014";
    if (bps >= 1000000) return `${(bps / 1000000).toFixed(1)} Mbps`;
    return `${(bps / 1000).toFixed(0)} Kbps`;
  }
</script>

<div class="surface-well p-4">
  <div class="space-y-2 text-mono-sm">
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Path</span>
      <span class="truncate text-right">{video.filePath ?? "\u2014"}</span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Adaptive Stream</span>
      <span class="truncate text-right">{video.streamUrl ?? "\u2014"}</span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Direct Stream</span>
      <span class="truncate text-right">{video.directStreamUrl ?? "\u2014"}</span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Size</span>
      <span class="truncate text-right">{video.fileSizeFormatted ?? "\u2014"}</span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Codec</span>
      <span class="truncate text-right">
        {[video.codec, video.container?.toUpperCase()].filter(Boolean).join(" / ") || "\u2014"}
      </span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Resolution</span>
      <span class="truncate text-right">
        {video.width && video.height ? `${video.width}x${video.height}` : "\u2014"}
      </span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Duration</span>
      <span class="truncate text-right">{video.durationFormatted ?? "\u2014"}</span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Bitrate</span>
      <span class="truncate text-right">{formatBitRate(video.bitRate)}</span>
    </div>
    <div class="separator"></div>
    <div class="flex justify-between gap-4">
      <span class="text-text-muted flex-shrink-0">Frame Rate</span>
      <span class="truncate text-right">
        {video.frameRate ? `${video.frameRate} fps` : "\u2014"}
      </span>
    </div>
  </div>
</div>
