<script lang="ts">
  /**
   * The shared acquisition row card: a long horizontal card on desktop, stacking gracefully on mobile.
   * Renders one normalized {@link AcquisitionListItem} — real cover artwork (via EntityThumbnail, kind
   * shape preserved), a title block (kind badge · title · creator subtitle), a status block (status chip
   * with icon · description · a brass progress bar or an animated searching shimmer · a client badge with
   * bullet-separated meta), and the actions (primary CTA · Remove · an overflow menu). Downloads, Missing,
   * and Cutoff Unmet all render through this, so the design lives in one place.
   */
  import { EllipsisVertical } from "@lucide/svelte";
  import { Checkbox } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import type { AcquisitionItemAction, AcquisitionListItem } from "$lib/requests/acquisition-list-item";

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

  let menuOpen = $state(false);

  const percent = $derived(item.progress != null ? Math.round(Math.min(1, Math.max(0, item.progress)) * 100) : null);

  function runMenuAction(action: AcquisitionItemAction) {
    menuOpen = false;
    action.run?.();
  }
</script>

<article class="acq-card" class:is-selected={selected}>
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
    <EntityThumbnail card={item.thumbnail} mediaOnly interactive={false} linkable={false} hoverPreviewsEnabled={false} showWantedBadge={false} imageLoading="lazy" />
  </svelte:element>

  <!-- Title block -->
  <div class="titleblock">
    <span class="kind">{labelForEntityKind(item.kind)}</span>
    {#if item.href}
      <a class="title" href={item.href} title={item.title}>{item.title}</a>
    {:else}
      <span class="title is-static" title={item.title}>{item.title}</span>
    {/if}
    {#if item.subtitle}
      <span class="subtitle" title={item.subtitle}>{item.subtitle}</span>
    {/if}
  </div>

  <!-- Status block -->
  <div class="statusblock">
    <span class={`status status-${item.tone}`}>
      {#if item.statusIcon}{@const Icon = item.statusIcon}<Icon size={13} />{/if}
      {item.statusLabel}
    </span>

    {#if item.progress != null || item.indeterminate}
      <div class="progress" class:indeterminate={item.progress == null && item.indeterminate}>
        <div class="progress-track">
          <div class="progress-fill" style:width={percent != null ? `${percent}%` : undefined}></div>
        </div>
        {#if percent != null}<span class="progress-value">{percent}%</span>{/if}
      </div>
    {:else if item.description}
      <span class="description" class:is-error={item.tone === "failed"} title={item.description}>{item.description}</span>
    {/if}

    {#if item.clientLabel || item.qualityGap || item.metaParts.length > 0}
      <div class="meta">
        {#if item.clientLabel}<span class="client">{item.clientLabel}</span>{/if}
        {#if item.qualityGap}<span class="quality">{item.qualityGap}</span>{/if}
        {#each item.metaParts as part, index (part + index)}
          <span class="dot" aria-hidden="true">•</span>
          <span class="meta-part">{part}</span>
        {/each}
      </div>
    {/if}
  </div>

  <!-- Actions -->
  <div class="actions">
    {#if item.primaryAction}
      {@const Icon = item.primaryAction.icon}
      <svelte:element
        this={item.primaryAction.href ? "a" : "button"}
        href={item.primaryAction.href}
        type={item.primaryAction.href ? undefined : "button"}
        role={item.primaryAction.href ? "link" : "button"}
        class="action action-primary"
        disabled={item.primaryAction.href ? undefined : item.primaryAction.disabled}
        aria-disabled={item.primaryAction.disabled}
        title={item.primaryAction.label}
        onclick={item.primaryAction.href ? undefined : item.primaryAction.run}
      >
        <Icon size={15} />
        <span class="action-label">{item.primaryAction.label}</span>
      </svelte:element>
    {/if}

    {#if item.removeAction}
      {@const Icon = item.removeAction.icon}
      <button
        type="button"
        class="action action-danger"
        disabled={item.removeAction.disabled}
        title={item.removeAction.label}
        onclick={item.removeAction.run}
      >
        <Icon size={15} />
        <span class="action-label">{item.removeAction.label}</span>
      </button>
    {/if}

    {#if item.menuActions.length > 0}
      <div class="menu-anchor">
        <button
          type="button"
          class="action-icon"
          class:is-open={menuOpen}
          aria-haspopup="menu"
          aria-expanded={menuOpen}
          aria-label="More actions"
          onclick={() => (menuOpen = !menuOpen)}
        >
          <EllipsisVertical size={16} />
        </button>
        {#if menuOpen}
          <button type="button" class="menu-scrim" aria-label="Close menu" onclick={() => (menuOpen = false)}></button>
          <div role="menu" class="menu" use:keepFlyoutOnScreen>
            {#each item.menuActions as action (action.id)}
              {@const Icon = action.icon}
              <svelte:element
                this={action.href ? "a" : "button"}
                href={action.href}
                type={action.href ? undefined : "button"}
                role="menuitem"
                tabindex="0"
                class="menu-item"
                onclick={action.href ? () => (menuOpen = false) : () => runMenuAction(action)}
              >
                <Icon size={14} />
                {action.label}
              </svelte:element>
            {/each}
          </div>
        {/if}
      </div>
    {/if}
  </div>
</article>

<style>
  .acq-card {
    position: relative;
    display: grid;
    grid-template-columns: min-content min-content minmax(0, 15rem) minmax(0, 1fr) min-content;
    grid-template-areas: "select poster titleblock statusblock actions";
    align-items: center;
    gap: 1rem;
    padding: 0.7rem 0.85rem;
    border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
    border-radius: var(--radius-md, 10px);
    background: linear-gradient(180deg, rgb(20 22 26 / 0.72) 0%, rgb(12 13 16 / 0.86) 100%);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.03), 0 2px 8px rgb(0 0 0 / 0.28);
    transition: border-color 160ms ease, box-shadow 160ms ease;
  }
  .acq-card:hover {
    border-color: rgb(255 255 255 / 0.16);
    box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.05), 0 4px 14px rgb(0 0 0 / 0.34);
  }
  .acq-card.is-selected {
    border-color: rgb(242 194 106 / 0.6);
    box-shadow: var(--shadow-glow-accent, 0 0 16px rgb(242 194 106 / 0.18));
  }

  .select { grid-area: select; display: flex; align-items: center; padding-left: 0.25rem; }

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
  /* Reuse EntityThumbnail's own per-kind frame; strip its standalone border/shadow so it sits flush. */
  .poster :global(.entity-thumbnail) { border: none; border-radius: inherit; box-shadow: none; }
  .poster :global(.entity-thumbnail .media) { border-radius: inherit; }

  .titleblock { grid-area: titleblock; display: flex; flex-direction: column; gap: 0.2rem; min-width: 0; }
  .kind {
    align-self: flex-start;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    font-size: 0.58rem;
    font-weight: 600;
    color: var(--color-text-muted, rgb(196 201 212 / 0.7));
    border: 1px solid rgb(255 255 255 / 0.1);
    border-radius: var(--radius-xs, 4px);
    padding: 0.12rem 0.34rem;
  }
  .title {
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.95rem;
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
  .subtitle {
    font-size: 0.74rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.68));
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .statusblock { grid-area: statusblock; display: flex; flex-direction: column; gap: 0.35rem; min-width: 0; }

  .status {
    align-self: flex-start;
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    font-size: 0.72rem;
    font-weight: 600;
    border-radius: var(--radius-sm, 6px);
    padding: 0.24rem 0.5rem;
  }
  .status :global(svg) { flex: 0 0 auto; }
  .status-downloading { color: #f2c26a; background: rgb(60 44 16 / 0.5); border: 1px solid rgb(242 194 106 / 0.32); }
  .status-searching { color: #e7d3af; background: rgb(48 40 22 / 0.5); border: 1px solid rgb(211 176 106 / 0.3); }
  .status-queued { color: rgb(214 219 228 / 0.85); background: rgb(255 255 255 / 0.05); border: 1px solid rgb(255 255 255 / 0.14); }
  .status-attention { color: #f2c26a; background: rgb(58 38 12 / 0.55); border: 1px solid rgb(242 194 106 / 0.4); }
  .status-failed { color: #ff9a86; background: rgb(48 18 14 / 0.5); border: 1px solid rgb(255 122 92 / 0.38); }
  .status-done { color: #6fd39a; background: rgb(20 46 32 / 0.5); border: 1px solid rgb(87 201 138 / 0.3); }
  .status-muted { color: rgb(196 201 212 / 0.7); background: rgb(255 255 255 / 0.05); border: 1px solid rgb(255 255 255 / 0.1); }
  .description.is-error { color: #ff9a86; }

  .description {
    font-size: 0.76rem;
    color: rgb(196 201 212 / 0.72);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .progress { display: flex; align-items: center; gap: 0.6rem; max-width: 30rem; }
  .progress-track { flex: 1 1 auto; height: 6px; border-radius: 3px; background: rgb(0 0 0 / 0.4); overflow: hidden; }
  .progress-fill {
    height: 100%;
    border-radius: 3px;
    background: linear-gradient(90deg, #7a5e20 0%, #d59a2a 55%, #f2c26a 100%);
    box-shadow: 0 0 8px rgb(242 194 106 / 0.5);
    transition: width 600ms var(--ease-default, ease);
  }
  .progress-value {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    font-weight: 600;
    color: #f2c26a;
    min-width: 2.6rem;
    text-align: right;
  }
  .progress.indeterminate .progress-track { background: rgb(211 176 106 / 0.14); }
  .progress.indeterminate .progress-fill {
    width: 34%;
    background: linear-gradient(90deg, transparent, #d3b06a 50%, transparent);
    box-shadow: none;
    animation: acq-sweep 1.5s ease-in-out infinite;
  }
  @keyframes acq-sweep {
    0% { transform: translateX(-120%); }
    100% { transform: translateX(320%); }
  }
  @media (prefers-reduced-motion: reduce) {
    .progress.indeterminate .progress-fill { animation: none; opacity: 0.55; }
  }

  .meta {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.35rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    color: rgb(196 201 212 / 0.66);
  }
  .client {
    padding: 0.1rem 0.34rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid rgb(255 255 255 / 0.1);
    background: rgb(255 255 255 / 0.04);
    color: rgb(214 219 228 / 0.82);
  }
  .quality {
    padding: 0.1rem 0.34rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid rgb(242 194 106 / 0.3);
    background: rgb(40 30 12 / 0.5);
    color: rgb(242 194 106 / 0.92);
  }
  .dot { opacity: 0.5; }

  .actions { grid-area: actions; display: flex; align-items: center; gap: 0.45rem; align-self: center; }
  .action {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 2.1rem;
    padding: 0 0.75rem;
    border-radius: var(--radius-sm, 6px);
    border: 1px solid rgb(255 255 255 / 0.12);
    background: rgb(255 255 255 / 0.04);
    color: rgb(244 239 230 / 0.9);
    font-size: 0.76rem;
    font-weight: 600;
    white-space: nowrap;
    text-decoration: none;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease, box-shadow 120ms ease;
  }
  .action:hover:not(:disabled) { background: rgb(255 255 255 / 0.09); border-color: rgb(255 255 255 / 0.2); }
  .action:disabled, .action[aria-disabled="true"] { opacity: 0.4; cursor: not-allowed; pointer-events: none; }
  .action-primary { border-color: rgb(242 194 106 / 0.42); background: rgb(50 38 14 / 0.6); color: #f2c26a; }
  .action-primary:hover:not(:disabled) { background: rgb(66 50 18 / 0.72); box-shadow: 0 0 12px rgb(242 194 106 / 0.2); }
  .action-danger { color: #ff9a86; border-color: rgb(255 122 92 / 0.3); }
  .action-danger:hover:not(:disabled) { background: rgb(48 18 14 / 0.6); border-color: rgb(255 122 92 / 0.5); }

  .menu-anchor { position: relative; display: flex; }
  .action-icon {
    display: grid;
    place-items: center;
    width: 2.1rem;
    height: 2.1rem;
    border-radius: var(--radius-sm, 6px);
    border: 1px solid transparent;
    color: rgb(196 201 212 / 0.6);
    cursor: pointer;
    transition: background 120ms ease, color 120ms ease, border-color 120ms ease;
  }
  .action-icon:hover, .action-icon.is-open {
    background: rgb(255 255 255 / 0.06);
    border-color: rgb(255 255 255 / 0.14);
    color: rgb(244 239 230 / 0.9);
  }
  .menu-scrim { position: fixed; inset: 0; z-index: 30; background: transparent; cursor: default; }
  .menu {
    position: absolute;
    right: 0;
    top: 2.4rem;
    z-index: 31;
    min-width: 10rem;
    overflow: hidden;
    border: 1px solid var(--color-border-default, rgb(255 255 255 / 0.14));
    border-radius: var(--radius-sm, 6px);
    background: var(--color-surface-1, #16181c);
    padding: 0.25rem;
    box-shadow: 0 12px 30px rgb(0 0 0 / 0.45);
  }
  .menu-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    width: 100%;
    padding: 0.4rem 0.5rem;
    border-radius: var(--radius-xs, 4px);
    font-size: 0.78rem;
    color: rgb(214 219 228 / 0.85);
    text-align: left;
    text-decoration: none;
    cursor: pointer;
    background: transparent;
  }
  .menu-item:hover { background: rgb(255 255 255 / 0.06); color: rgb(244 239 230 / 0.95); }
  .menu-item :global(svg) { color: rgb(242 194 106 / 0.75); flex: 0 0 auto; }

  /* ── Mobile / narrow container: title + status stack under the poster, actions drop to a full row ── */
  @container (max-width: 40rem) {
    .acq-card {
      grid-template-columns: min-content min-content minmax(0, 1fr);
      grid-template-areas:
        "select poster titleblock"
        "select poster statusblock"
        "actions actions actions";
      gap: 0.35rem 0.85rem;
      align-items: start;
    }
    .poster { align-self: start; }
    .select { align-items: start; padding-top: 0.15rem; }
    .titleblock { align-self: end; }
    .actions { padding-top: 0.15rem; justify-content: flex-end; }
    .action-label { display: none; }
    .action { padding: 0 0.65rem; }
    .progress { max-width: none; }

    /* Let the title, subtitle, and status description wrap to full text instead of truncating — on a
       narrow screen the row has the vertical room, and the failure reason especially must read in full. */
    .title,
    .subtitle,
    .description {
      white-space: normal;
      overflow: visible;
    }
  }
</style>
