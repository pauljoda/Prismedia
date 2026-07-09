<script lang="ts">
  import { Layers } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";

  type ImageShape = "portrait" | "square" | "wide";

  interface Props {
    proposal: EntityMetadataProposal;
    title: string;
    subtitle?: string | null;
    kindLabel?: string | null;
    posterUrl?: string | null;
    imageShape?: ImageShape;
    showReason?: boolean;
  }

  let {
    proposal,
    title,
    subtitle = null,
    kindLabel = null,
    posterUrl = null,
    imageShape = "portrait",
    showReason = false,
  }: Props = $props();

  const imageClass = $derived(
    imageShape === "square" ? "h-14 w-14" : imageShape === "wide" ? "h-12 w-[5.5rem]" : "h-16 w-11",
  );
  const confidence = $derived(
    proposal.confidence == null ? null : Number(proposal.confidence),
  );
</script>

<div
  class={cn(
    "grid items-center gap-4 rounded-sm border border-border-subtle bg-surface-1 p-3.5 shadow-well",
    showReason ? "grid-cols-[auto_1fr_auto_auto_auto]" : "grid-cols-[auto_1fr_auto_auto]",
  )}
>
  {#if posterUrl}
    <img
      src={posterUrl}
      alt=""
      class={cn(imageClass, "rounded-xs object-cover")}
      decoding="async"
      referrerpolicy="no-referrer"
    />
  {:else}
    <div class={cn(imageClass, "grid place-items-center rounded-xs bg-surface-3")}>
      <Layers class="h-5 w-5 text-text-disabled" />
    </div>
  {/if}

  <div class="min-w-0">
    <h2 class="truncate">{title}</h2>
    <div class="mt-1 flex min-w-0 flex-wrap items-center gap-1.5">
      <span class="rounded-xs border border-phosphor-600/20 bg-surface-3 px-1.5 py-0.5 font-mono text-[0.6rem] leading-none text-phosphor-600">
        {kindLabel ?? proposal.targetKind}
      </span>
      {#if subtitle}
        <span class="min-w-0 truncate font-mono text-[0.7rem] text-text-muted">{subtitle}</span>
      {/if}
    </div>
  </div>

  <div class="hidden flex-col items-end gap-0.5 md:flex">
    <span class="text-kicker">Match</span>
    <span class="font-mono font-semibold text-text-accent">
      {confidence != null && Number.isFinite(confidence) ? `${Math.round(confidence * 100)}%` : "—"}
    </span>
  </div>
  <div class="hidden flex-col items-end gap-0.5 md:flex">
    <span class="text-kicker">Provider</span>
    <span class="text-[0.82rem] text-text-primary">{proposal.provider}</span>
  </div>
  {#if showReason}
    <div class="hidden flex-col items-end gap-0.5 md:flex">
      <span class="text-kicker">Reason</span>
      <span class="text-[0.74rem] text-text-secondary">{proposal.matchReason ?? "—"}</span>
    </div>
  {/if}
</div>
