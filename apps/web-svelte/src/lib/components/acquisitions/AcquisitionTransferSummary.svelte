<script lang="ts">
  import { CloudDownload, Loader2 } from "@lucide/svelte";
  import { Disclosure, Progress } from "@prismedia/ui-svelte";
  import type { AcquisitionTransferPresentation } from "$lib/requests/acquisition-transfer-presentation";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import PieceStateBar from "./PieceStateBar.svelte";

  let { transfer }: { transfer: AcquisitionTransferPresentation | null } = $props();
</script>

{#if transfer}
  <section class="@container flex min-w-0 flex-col gap-4" aria-label="Download progress">
    <div class="flex items-center justify-between gap-3">
      <span class="flex min-w-0 items-center gap-control-gap text-control font-medium">
        {#if transfer.active}
          <Loader2 class="size-icon shrink-0 animate-spin motion-reduce:animate-none text-muted-foreground" aria-hidden="true" />
        {/if}
        {transfer.stage}
      </span>
      <span class="shrink-0 font-mono text-lg tabular-nums">{transfer.percent === null ? "—" : `${transfer.percent}%`}</span>
    </div>
    <Progress value={transfer.percent} aria-label="Download progress" class="h-2" />
    <dl class="grid grid-cols-2 gap-4 @min-[24rem]:grid-cols-3">
      <div class="flex min-w-0 flex-col gap-1">
        <dt class="text-caption text-muted-foreground">Speed</dt>
        <dd class="m-0 font-mono text-control">{transfer.speed}</dd>
      </div>
      <div class="flex min-w-0 flex-col gap-1">
        <dt class="text-caption text-muted-foreground">Time left</dt>
        <dd class="m-0 font-mono text-control">{transfer.eta}</dd>
      </div>
      <div class="flex min-w-0 flex-col gap-1">
        <dt class="text-caption text-muted-foreground">Download size</dt>
        <dd class="m-0 font-mono text-control">{transfer.size}</dd>
      </div>
    </dl>
    <Disclosure title="Transfer details">
      <div class="flex flex-col gap-3">
        <dl class="flex flex-wrap items-baseline justify-between gap-2">
          <dt class="text-caption text-muted-foreground">Seeds / peers</dt>
          <dd class="m-0 font-mono text-control">{transfer.peers}</dd>
        </dl>
        {#if transfer.pieces.length > 0}
          <PieceStateBar pieces={transfer.pieces} />
        {/if}
      </div>
    </Disclosure>
  </section>
{:else}
  <StatePlaceholder
    icon={CloudDownload}
    title="Waiting for download progress"
    description="The download client has not reported progress yet."
    busy
  />
{/if}
