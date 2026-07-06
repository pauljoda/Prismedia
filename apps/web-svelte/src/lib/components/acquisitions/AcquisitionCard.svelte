<script lang="ts">
  /**
   * The shared acquisition row card: a long horizontal card on desktop, stacking gracefully on mobile.
   * Renders one normalized {@link AcquisitionListItem} — poster artwork (reusing EntityThumbnail so the
   * item is recognizable at a glance), a kind badge + status pill, a pretty progress indicator
   * (determinate brass fill while downloading, an animated shimmer while searching/queued), metadata
   * chips, and the row's action buttons. Downloads, Missing, and Cutoff Unmet all render through this,
   * so the design lives in one place.
   */
  import { Checkbox } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import type { AcquisitionListItem } from "$lib/requests/acquisition-list-item";

  let {
    item,
    selectable = false,
    selected = false,
    onToggleSelected,
  }: {
    item: AcquisitionListItem;
    selectable?: boolean;
    selected?: boolean;
    onToggleSelected?: (id: string) => void;
  } = $props();

  const percent = $derived(item.progress != null ? Math.round(Math.min(1, Math.max(0, item.progress)) * 100) : null);
</script>

<article class={`acq-card tone-${item.tone}`} class:is-selected={selected}>
  {#if selectable}
    <div class="select">
      <Checkbox size="md" checked={selected} onchange={() => onToggleSelected?.(item.id)} aria-label={`Select ${item.title}`} />
    </div>
  {/if}

  <svelte:element
    this={item.href ? "a" : "div"}
    href={item.href}
    class="poster"
    aria-label={item.href ? `Open ${item.title}` : undefined}
  >
    <EntityThumbnail
      card={item.thumbnail}
      mediaOnly
      interactive={false}
      linkable={false}
      hoverPreviewsEnabled={false}
      imageLoading="lazy"
    />
  </svelte:element>

  <div class="body">
    <div class="head">
      <span class="kind">{labelForEntityKind(item.kind)}</span>
      <span class={`status status-${item.tone}`}>
        {#if item.indeterminate}<span class="pulse" aria-hidden="true"></span>{/if}
        {item.statusLabel}
      </span>
      {#if item.message}
        <span class="message" class:is-error={item.tone === "attention"} title={item.message}>{item.message}</span>
      {/if}
    </div>

    {#if item.href}
      <a class="title" href={item.href} title={item.title}>{item.title}</a>
    {:else}
      <span class="title is-static" title={item.title}>{item.title}</span>
    {/if}

    {#if item.progress != null || item.indeterminate}
      <div class="progress" class:indeterminate={item.progress == null && item.indeterminate}>
        <div class="progress-track">
          <div class="progress-fill" style:width={percent != null ? `${percent}%` : undefined}></div>
        </div>
        {#if percent != null}<span class="progress-value">{percent}%</span>{/if}
      </div>
    {/if}

    {#if item.meta.length > 0}
      <div class="meta">
        {#each item.meta as chip (chip.label)}
          <span class={`chip chip-${chip.tone ?? "default"}`} title={chip.title}>
            {#if chip.icon}{@const Icon = chip.icon}<Icon size={11} />{/if}
            {chip.label}
          </span>
        {/each}
      </div>
    {/if}
  </div>

  {#if item.actions.length > 0}
    <div class="actions">
      {#each item.actions as action (action.id)}
        {@const Icon = action.icon}
        <button
          type="button"
          class={`action action-${action.tone ?? "default"}`}
          disabled={action.disabled}
          title={action.label}
          aria-label={action.label}
          onclick={action.run}
        >
          <Icon size={15} />
          <span class="action-label">{action.label}</span>
        </button>
      {/each}
    </div>
  {/if}
</article>

<style>
  .acq-card {
    position: relative;
    display: grid;
    grid-template-columns: min-content min-content minmax(0, 1fr) min-content;
    grid-template-areas: "select poster body actions";
    align-items: center;
    gap: 0.85rem;
    padding: 0.6rem 0.75rem;
    border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
    border-radius: var(--radius-md, 10px);
    background:
      linear-gradient(180deg, rgb(20 22 26 / 0.72) 0%, rgb(12 13 16 / 0.86) 100%);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.03), 0 2px 8px rgb(0 0 0 / 0.28);
    transition: border-color 160ms ease, box-shadow 160ms ease, transform 160ms ease;
  }

  .acq-card:hover {
    border-color: rgb(255 255 255 / 0.16);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.05), 0 4px 14px rgb(0 0 0 / 0.34);
  }

  .acq-card.is-selected {
    border-color: rgb(242 194 106 / 0.6);
    box-shadow: var(--shadow-glow-accent, 0 0 16px rgb(242 194 106 / 0.18));
  }

  /* A thin left accent rail keyed to the row's tone — state you can read from across the list. */
  .acq-card::before {
    content: "";
    position: absolute;
    inset: 0.6rem auto 0.6rem 0;
    width: 3px;
    border-radius: 3px;
    background: var(--tone-accent, rgb(255 255 255 / 0.14));
  }
  .tone-downloading { --tone-accent: linear-gradient(180deg, #d59a2a, #f2c26a); }
  .tone-searching { --tone-accent: linear-gradient(180deg, #3f6f9c, #6aa7d5); }
  .tone-attention { --tone-accent: linear-gradient(180deg, #b8862e, #f2c26a); }
  .tone-done { --tone-accent: linear-gradient(180deg, #2f7d4f, #57c98a); }
  .tone-muted { --tone-accent: rgb(255 255 255 / 0.14); }

  .select {
    grid-area: select;
    display: flex;
    align-items: center;
    padding-left: 0.35rem;
  }

  .poster {
    grid-area: poster;
    display: block;
    width: 3.5rem;
    align-self: center;
    border-radius: var(--radius-sm, 6px);
    overflow: hidden;
    text-decoration: none;
    box-shadow: 0 2px 8px rgb(0 0 0 / 0.4);
  }
  /* Reuse EntityThumbnail's own per-kind frame (a book is tall, an album square, a video wide) —
     only strip its standalone border/shadow so it sits flush as the card's artwork anchor. */
  .poster :global(.entity-thumbnail) {
    border: none;
    border-radius: inherit;
    box-shadow: none;
  }
  .poster :global(.entity-thumbnail .media) {
    border-radius: inherit;
  }

  .body {
    grid-area: body;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 0.35rem;
    min-width: 0;
  }

  .head {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.4rem;
    min-width: 0;
  }

  .kind {
    text-transform: uppercase;
    letter-spacing: 0.05em;
    font-size: 0.58rem;
    font-weight: 600;
    color: var(--color-text-muted, rgb(196 201 212 / 0.7));
    border: 1px solid rgb(255 255 255 / 0.1);
    border-radius: var(--radius-xs, 4px);
    padding: 0.12rem 0.34rem;
    white-space: nowrap;
  }

  .status {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    font-size: 0.66rem;
    font-weight: 600;
    letter-spacing: 0.01em;
    border-radius: var(--radius-xs, 4px);
    padding: 0.14rem 0.4rem;
    white-space: nowrap;
  }
  .status-downloading { color: #f2c26a; background: rgb(60 44 16 / 0.55); border: 1px solid rgb(242 194 106 / 0.3); }
  .status-searching { color: #8fc0e8; background: rgb(24 40 56 / 0.55); border: 1px solid rgb(106 167 213 / 0.28); }
  .status-attention { color: #f2c26a; background: rgb(58 38 12 / 0.6); border: 1px solid rgb(242 194 106 / 0.38); }
  .status-done { color: #6fd39a; background: rgb(20 46 32 / 0.55); border: 1px solid rgb(87 201 138 / 0.3); }
  .status-muted { color: rgb(196 201 212 / 0.7); background: rgb(255 255 255 / 0.05); border: 1px solid rgb(255 255 255 / 0.1); }

  .pulse {
    width: 0.42rem;
    height: 0.42rem;
    border-radius: 50%;
    background: currentColor;
    box-shadow: 0 0 6px currentColor;
    animation: acq-pulse 1.4s ease-in-out infinite;
  }
  @keyframes acq-pulse {
    0%, 100% { opacity: 0.35; transform: scale(0.85); }
    50% { opacity: 1; transform: scale(1.1); }
  }

  .message {
    min-width: 0;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    color: rgb(196 201 212 / 0.7);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .message.is-error { color: #ff9a86; }

  .title {
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.92rem;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: rgb(244 239 230 / 0.96);
    text-decoration: none;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    transition: color 120ms ease;
  }
  a.title:hover { color: #f2c26a; }
  .title.is-static { color: rgb(244 239 230 / 0.9); }

  .progress {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    max-width: 26rem;
  }
  .progress-track {
    flex: 1 1 auto;
    height: 5px;
    border-radius: 3px;
    background: rgb(0 0 0 / 0.4);
    overflow: hidden;
  }
  .progress-fill {
    height: 100%;
    border-radius: 3px;
    background: linear-gradient(90deg, #7a5e20 0%, #d59a2a 55%, #f2c26a 100%);
    box-shadow: 0 0 8px rgb(242 194 106 / 0.5);
    transition: width 600ms var(--ease-default, ease);
  }
  .progress-value {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.64rem;
    color: rgb(242 194 106 / 0.9);
    min-width: 2.4rem;
    text-align: right;
  }
  /* Indeterminate: a brass sheen sweeps a translucent track. */
  .progress.indeterminate .progress-track {
    background: rgb(106 167 213 / 0.14);
  }
  .progress.indeterminate .progress-fill {
    width: 38%;
    background: linear-gradient(90deg, transparent, #6aa7d5 50%, transparent);
    box-shadow: none;
    animation: acq-sweep 1.5s ease-in-out infinite;
  }
  @keyframes acq-sweep {
    0% { transform: translateX(-120%); }
    100% { transform: translateX(320%); }
  }
  @media (prefers-reduced-motion: reduce) {
    .progress.indeterminate .progress-fill { animation: none; opacity: 0.6; }
    .pulse { animation: none; }
  }

  .meta {
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem;
  }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 0.22rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    line-height: 1;
    padding: 0.16rem 0.34rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid rgb(255 255 255 / 0.08);
    background: rgb(255 255 255 / 0.04);
    color: rgb(196 201 212 / 0.82);
    white-space: nowrap;
  }
  .chip :global(svg) { color: rgb(242 194 106 / 0.7); flex: 0 0 auto; }
  .chip-accent { border-color: rgb(242 194 106 / 0.3); color: rgb(242 194 106 / 0.92); background: rgb(40 30 12 / 0.5); }
  .chip-danger { border-color: rgb(255 122 92 / 0.32); color: #ff9a86; background: rgb(48 18 14 / 0.5); }

  .actions {
    grid-area: actions;
    display: flex;
    align-items: center;
    gap: 0.4rem;
    align-self: center;
  }
  .action {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 2rem;
    padding: 0 0.7rem;
    border-radius: var(--radius-sm, 6px);
    border: 1px solid rgb(255 255 255 / 0.12);
    background: rgb(255 255 255 / 0.04);
    color: rgb(244 239 230 / 0.9);
    font-size: 0.74rem;
    font-weight: 600;
    white-space: nowrap;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease, color 120ms ease, box-shadow 120ms ease;
  }
  .action:hover:not(:disabled) { background: rgb(255 255 255 / 0.09); border-color: rgb(255 255 255 / 0.2); }
  .action:disabled { opacity: 0.4; cursor: not-allowed; }
  .action-primary {
    border-color: rgb(242 194 106 / 0.42);
    background: rgb(50 38 14 / 0.6);
    color: #f2c26a;
  }
  .action-primary:hover:not(:disabled) {
    background: rgb(66 50 18 / 0.72);
    box-shadow: 0 0 12px rgb(242 194 106 / 0.2);
  }
  .action-danger { color: #ff9a86; border-color: rgb(255 122 92 / 0.3); }
  .action-danger:hover:not(:disabled) {
    background: rgb(48 18 14 / 0.6);
    border-color: rgb(255 122 92 / 0.5);
  }

  /* ── Mobile / narrow container: actions drop to their own row under poster+body ── */
  @container (max-width: 30rem) {
    .acq-card {
      grid-template-columns: min-content min-content minmax(0, 1fr);
      grid-template-areas:
        "select poster body"
        "actions actions actions";
      gap: 0.5rem 0.7rem;
    }
    .actions {
      justify-content: flex-end;
    }
    .action-label { display: none; }
    .action { padding: 0 0.6rem; }
    .progress { max-width: none; }
  }
</style>
