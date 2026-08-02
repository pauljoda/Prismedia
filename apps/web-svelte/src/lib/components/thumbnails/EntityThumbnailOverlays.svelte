<script lang="ts">
  import { getRatingValue, isNsfw, isWanted } from "$lib/api/capabilities";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { Flame, Star } from "@lucide/svelte";

  interface Props {
    card: EntityThumbnailCard;
    onSelectedChange?: (selected: boolean) => void;
    selectable: boolean;
    selected: boolean;
    showWantedBadge: boolean;
  }

  let { card, onSelectedChange, selectable, selected, showWantedBadge }: Props = $props();
  const nsfw = $derived(isNsfw(card.entity.capabilities));
  const wanted = $derived(isWanted(card.entity.capabilities));
  const wantedDisplay = $derived(acquisitionStatusDisplay(card.wantedStatus));
  const rating = $derived(getRatingValue(card.entity.capabilities));
  const bottomLeft = $derived(card.custom?.bottomLeft);
  const sourceTag = $derived(card.custom?.sourceTag);
  const progressPercent = $derived(card.progress != null && card.progress > 0 ? Math.min(100, Math.max(0, card.progress * 100)) : null);

  function handleSelectionChange(event: Event) {
    onSelectedChange?.((event.currentTarget as HTMLInputElement).checked);
  }
  function stopSelectionActivation(event: Event) { event.stopPropagation(); }
  function formatRating(value: number): string { return value <= 0 ? "" : String(Math.round(value)); }
</script>

{#if selectable}
  <input class="selection" class:is-selected={selected} type="checkbox" checked={selected} title={`Select ${card.entity.title}`} aria-label={`Select ${card.entity.title}`} onclick={stopSelectionActivation} onpointerdown={stopSelectionActivation} onchange={handleSelectionChange} />
{/if}
{#if (wanted && showWantedBadge) || nsfw}
  <div class="badges top-badges">
    {#if wanted && showWantedBadge}
      {@const WantedIcon = wantedDisplay.icon}
      <span class={`badge wanted-badge wb-${wantedDisplay.tone}`} title={`Wanted — ${wantedDisplay.label}`} aria-label={`Wanted — ${wantedDisplay.label}`}><WantedIcon size={11} /><span class="wanted-label">{wantedDisplay.label}</span></span>
    {/if}
    {#if nsfw}<span class="badge danger icon-only" title="NSFW" aria-label="NSFW"><Flame size={13} /></span>{/if}
  </div>
{/if}
{#if bottomLeft}<div class="badges bottom-left-badges" class:has-selection={selectable}><span class="badge position-badge" title={bottomLeft.title ?? bottomLeft.label}>{bottomLeft.label}</span></div>{/if}
{#if sourceTag}<div class="badges source-badges"><span class="badge source-badge" title={sourceTag.title ?? sourceTag.label}>{sourceTag.label}</span></div>{/if}
{#if rating > 0}<div class="badges bottom-right-badges"><span class="badge rating-badge" title={`Rating ${formatRating(rating)}`} aria-label={`Rating ${formatRating(rating)}`}>{formatRating(rating)}<Star size={11} /></span></div>{/if}
{#if progressPercent != null}<div class="progress-meter" aria-hidden="true"><span class="progress-meter-fill" style:width={`${progressPercent}%`}></span></div>{/if}

<style>
  .progress-meter { position: absolute; inset: auto 0 0; z-index: 4; height: 3px; background: rgb(0 0 0 / 0.45); }
  .progress-meter-fill { display: block; height: 100%; background: color-mix(in srgb, var(--entity-accent) 80%, #c7c9cc); }
  .badges { position: absolute; z-index: 3; right: 0.45rem; left: 2.45rem; display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: 0.35rem; pointer-events: none; }
  .top-badges { top: 0.45rem; }
  .bottom-left-badges { top: 0.45rem; right: auto; left: 0.45rem; justify-content: flex-start; }
  .bottom-left-badges.has-selection { left: 2.45rem; }
  .bottom-right-badges { right: 0.45rem; bottom: 0.45rem; left: auto; justify-content: flex-end; }
  .source-badges { bottom: 0.45rem; right: auto; left: 0.45rem; justify-content: flex-start; }
  .badge { display: inline-flex; min-height: 1.35rem; align-items: center; gap: 0.25rem; border: 1px solid rgb(255 255 255 / 0.12); border-radius: var(--radius-xs, 4px); background: rgb(11 11 12 / 0.72); color: rgb(244 239 230 / 0.88); font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.66rem; line-height: 1; letter-spacing: 0; padding: 0.25rem 0.38rem; }
  .badge :global(svg) { flex: 0 0 auto; }
  .danger { border-color: rgb(255 92 67 / 0.42); background: rgb(40 13 10 / 0.76); color: #ff806f; box-shadow: none; }
  .position-badge { border-color: rgb(199 201 204 / 0.34); background: rgb(9 10 11 / 0.78); color: rgb(244 239 230 / 0.9); box-shadow: 0 0 14px rgb(0 0 0 / 0.18); }
  .source-badge { border-color: rgb(255 255 255 / 0.16); background: rgb(11 11 12 / 0.78); color: rgb(224 228 236 / 0.82); font-size: 0.56rem; font-weight: 600; letter-spacing: 0.05em; text-transform: uppercase; }
  .rating-badge { gap: 0.18rem; border-color: rgb(199 201 204 / 0.38); background: rgb(39 29 12 / 0.76); color: rgb(199 201 204 / 0.96); box-shadow: none; }
  .rating-badge :global(svg) { fill: currentColor; filter: none; }
  .wanted-badge { gap: 0.28rem; border-color: rgb(199 201 204 / 0.42); background: rgb(39 29 12 / 0.82); color: rgb(199 201 204 / 0.96); box-shadow: 0 0 14px rgb(0 0 0 / 0.3); font-size: 0.58rem; font-weight: 600; letter-spacing: 0.05em; text-transform: uppercase; }
  .wanted-badge :global(svg) { flex: 0 0 auto; }
  .wb-downloading { color: #c7c9cc; border-color: rgb(199 201 204 / 0.5); background: rgb(52 38 14 / 0.85); box-shadow: none; }
  .wb-searching { color: #c8c9cc; border-color: rgb(211 176 106 / 0.4); background: rgb(40 33 18 / 0.85); }
  .wb-attention { color: #c7c9cc; border-color: rgb(199 201 204 / 0.48); background: rgb(52 36 12 / 0.85); }
  .wb-queued { color: rgb(224 228 236 / 0.9); border-color: rgb(255 255 255 / 0.22); background: rgb(18 20 24 / 0.85); }
  .wb-cleanup { color: #c8c9cc; border-color: rgb(211 176 106 / 0.34); background: rgb(33 29 20 / 0.85); }
  .wb-failed { color: #ff9a86; border-color: rgb(255 122 92 / 0.46); background: rgb(44 16 12 / 0.85); box-shadow: none; }
  .wb-done { color: #6fd39a; border-color: rgb(87 201 138 / 0.34); background: rgb(20 46 32 / 0.82); }
  .wb-muted { color: rgb(196 201 212 / 0.72); border-color: rgb(255 255 255 / 0.14); background: rgb(18 20 24 / 0.82); }
  .wb-wanted { color: rgb(199 201 204 / 0.96); border-color: rgb(199 201 204 / 0.42); background: rgb(39 29 12 / 0.82); }
  .icon-only { justify-content: center; inline-size: 1.35rem; padding-inline: 0; }
  .selection { position: absolute; z-index: 6; top: 0.45rem; left: 0.45rem; display: grid; inline-size: 1.55rem; block-size: 1.55rem; border: 1px solid rgb(255 255 255 / 0.12); border-radius: var(--radius-xs, 4px); background: rgb(11 11 12 / 0.72); appearance: none; cursor: pointer; opacity: 0; pointer-events: none; transition: opacity 120ms ease, border-color 120ms ease, box-shadow 120ms ease; }
  :global(.entity-thumbnail:is(:hover, :focus-within)) .selection, :global(.entity-thumbnail.is-select-mode) .selection, :global(.entity-thumbnail.is-selected) .selection, .selection:focus { opacity: 1; pointer-events: auto; }
  .selection::before { position: absolute; inset: 0.38rem; border: 1px solid rgb(244 239 230 / 0.7); background: rgb(0 0 0 / 0.16); content: ""; pointer-events: none; }
  .selection::after { position: absolute; top: 0.58rem; left: 0.54rem; inline-size: 0.45rem; block-size: 0.24rem; border-bottom: 2px solid #0b0b0c; border-left: 2px solid #0b0b0c; content: ""; opacity: 0; transform: rotate(-45deg); }
  .selection:checked, .selection.is-selected { border-color: color-mix(in srgb, var(--entity-accent) 74%, white 8%); box-shadow: 0 0 0 1px color-mix(in srgb, var(--entity-accent) 64%, transparent); }
  .selection:checked::before, .selection.is-selected::before { border-color: var(--entity-accent); background: linear-gradient(135deg, var(--entity-accent), var(--entity-accent-secondary)); }
  .selection:checked::after, .selection.is-selected::after { opacity: 1; }
  :global(.entity-thumbnail.is-list) .selection { opacity: 1; pointer-events: auto; }
  :global(.entity-thumbnail.is-list) .badges { right: 0.38rem; left: 2.2rem; }
  @media (max-width: 640px) { .badge { font-size: 0.61rem; } }
  @container (max-width: 112px) { .badges { gap: 0.18rem; } .top-badges { top: 0.3rem; right: 0.3rem; left: 0.3rem; } .bottom-left-badges { top: 0.3rem; left: 0.3rem; } .bottom-right-badges { right: 0.3rem; bottom: 0.3rem; } .badge { min-height: 0; gap: 0.15rem; padding: 0.15rem 0.26rem; font-size: 0.5rem; line-height: 1; } .wanted-badge { gap: 0; padding: 0.2rem; } .wanted-label, .rating-badge { display: none; } .icon-only { inline-size: 1.05rem; } .progress-meter { height: 2px; } }
</style>
