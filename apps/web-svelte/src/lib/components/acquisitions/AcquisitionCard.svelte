<script lang="ts">
  /**
   * The shared acquisition row card: a long horizontal card on desktop, stacking gracefully on mobile.
   * Renders one normalized {@link AcquisitionListItem} — real cover artwork (via EntityThumbnail,
   * squared so a mixed queue keeps one row height), a title block (family-accented kind badge · title ·
   * creator subtitle), a status block (status chip with icon · description · a neutral accent progress
   * bar or an animated searching shimmer · a client badge with bullet-separated meta), and the actions
   * (primary CTA · Remove · an overflow menu). Downloads, Missing, and Cutoff Unmet all render through
   * this, so the design lives in one place.
   */
  import { EllipsisVertical } from "@lucide/svelte";
  import { Checkbox } from "@prismedia/ui-svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
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
  const accent = $derived(entityAccentForKind(item.kind));

  /**
   * A queue mixes posters, square album art, and wide video stills. Squaring every card keeps the
   * row height and the title column constant down the list instead of stepping in and out.
   */
  const squaredThumbnail = $derived({ ...item.thumbnail, aspectRatio: "square" as const });

  function runMenuAction(action: AcquisitionItemAction) {
    menuOpen = false;
    action.run?.();
  }
</script>

<article
  class="acq-card"
  class:is-selected={selected}
  style:--family-accent={accent.primary}
>
  {#if selectable && item.selectable !== false}
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
    <EntityThumbnail card={squaredThumbnail} mediaOnly interactive={false} linkable={false} hoverPreviewsEnabled={false} showWantedBadge={false} imageLoading="lazy" />
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
          <!-- The bullet separates, so it is only drawn between items — never leading the row. -->
          {#if index > 0 || item.clientLabel || item.qualityGap}
            <span class="dot" aria-hidden="true">•</span>
          {/if}
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

    {#if item.menuActions.length === 0}
      <!--
        Reserve the overflow slot even when a row has no menu. Without it the action cluster is
        wider on some rows than others, so Remove lands in a different place down the list.
      -->
      <span class="action-slot-placeholder" aria-hidden="true"></span>
    {:else}
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
    /*
     * The title takes the row's slack and the status column is capped: status content is a chip,
     * a short meter, and a meta line, so giving it the remainder left a wide empty gap while
     * titles truncated at 15rem.
     */
    grid-template-columns: min-content min-content minmax(0, 1fr) minmax(0, 24rem) min-content;
    grid-template-areas: "select poster titleblock statusblock actions";
    align-items: center;
    gap: 0.8rem;
    padding: 0.5rem 0.7rem;
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
    border-color: rgb(199 201 204 / 0.6);
    box-shadow: var(--shadow-glow-accent, 0 0 16px rgb(199 201 204 / 0.18));
  }

  .select { grid-area: select; display: flex; align-items: center; padding-left: 0.25rem; }

  .poster {
    grid-area: poster;
    display: block;
    width: 3rem;
    height: 3rem;
    align-self: center;
    border-radius: var(--radius-sm, 6px);
    overflow: hidden;
    text-decoration: none;
    box-shadow: 0 2px 8px rgb(0 0 0 / 0.4);
  }
  /* Reuse EntityThumbnail's own per-kind frame; strip its standalone border/shadow so it sits flush. */
  .poster :global(.entity-thumbnail) { border: none; border-radius: inherit; box-shadow: none; width: 100%; height: 100%; }
  .poster :global(.entity-thumbnail .media) { border-radius: inherit; height: 100%; }

  .titleblock { grid-area: titleblock; display: flex; flex-direction: column; gap: 0.2rem; min-width: 0; }
  /*
   * The badge names the entity family, so it carries that family's muted accent as a thin leading
   * rail. Text and border stay neutral; the colour is the marker, not the chip.
   */
  .kind {
    align-self: flex-start;
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    font-size: 0.58rem;
    font-weight: 600;
    color: var(--color-text-muted, rgb(196 201 212 / 0.7));
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs, 4px);
    padding: 0.12rem 0.34rem 0.12rem 0;
    overflow: hidden;
  }

  .kind::before {
    content: "";
    align-self: stretch;
    width: 2px;
    margin-right: 0.15rem;
    background: var(--family-accent, var(--color-accent-500));
  }
  .title {
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.95rem;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: var(--color-text-primary);
    text-decoration: none;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    transition: color 120ms ease;
  }
  a.title:hover { color: var(--color-accent-500); }
  .title.is-static { color: var(--color-text-secondary); }
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
  .status-downloading { color: var(--color-text-primary); background: var(--color-surface-3); border: 1px solid var(--color-border-accent); }
  /*
   * Waiting, queued, and searching are all "nothing is wrong yet", and a full queue of them in
   * warning amber reads as a page full of problems. Amber is reserved for `attention`, red for
   * `failed`, so a real issue stands out against a calm list.
   */
  .status-searching { color: var(--color-text-secondary); background: var(--color-surface-2); border: 1px solid var(--color-border-default); }
  .status-queued { color: var(--color-text-secondary); background: var(--color-surface-2); border: 1px solid var(--color-border-default); }
  .status-cleanup { color: var(--color-text-secondary); background: var(--color-surface-3); border: 1px solid var(--color-border-default); }
  .status-attention { color: var(--color-warning-text); background: var(--color-warning-muted); border: 1px solid color-mix(in srgb, var(--color-warning) 38%, transparent); }
  .status-failed { color: var(--color-error-text); background: var(--color-error-muted); border: 1px solid color-mix(in srgb, var(--color-error) 38%, transparent); }
  .status-done { color: var(--color-success-text); background: var(--color-success-muted); border: 1px solid color-mix(in srgb, var(--color-success) 30%, transparent); }
  .status-muted { color: var(--color-text-muted); background: var(--color-surface-2); border: 1px solid var(--color-border-subtle); }
  .description.is-error { color: var(--color-error-text); }

  /*
   * A failure or waiting reason is a full sentence, so a single nowrap line clips it mid-word.
   * Two clamped lines break on a word and carry enough of the reason to act on.
   */
  .description {
    display: -webkit-box;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    font-size: 0.76rem;
    line-height: 1.35;
    color: var(--color-text-muted);
    overflow: hidden;
    overflow-wrap: anywhere;
  }

  /* A 30rem rail for a single percentage reads as a stray rule; keep it a compact meter. */
  .progress { display: flex; align-items: center; gap: 0.6rem; max-width: 16rem; }
  .progress-track { flex: 1 1 auto; height: 5px; border-radius: 3px; background: var(--color-surface-1); overflow: hidden; }
  .progress-fill {
    height: 100%;
    border-radius: 3px;
    background: linear-gradient(90deg, var(--color-accent-700) 0%, var(--color-accent-600) 55%, var(--color-accent-500) 100%);
    transition: width 600ms var(--ease-default, ease);
  }
  .progress-value {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    font-weight: 600;
    color: var(--color-accent-500);
    min-width: 2.6rem;
    text-align: right;
  }
  /*
   * An indeterminate bar means "working", not "something is wrong", so the sweep stays neutral.
   * A queue of eighteen searching rows in warning orange reads as eighteen problems.
   */
  .progress.indeterminate .progress-track { background: var(--color-surface-2); }
  .progress.indeterminate .progress-fill {
    width: 34%;
    background: linear-gradient(90deg, transparent, var(--color-accent-500) 50%, transparent);
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
    color: var(--color-text-muted);
  }
  .client {
    padding: 0.1rem 0.34rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-2);
    color: var(--color-text-secondary);
  }
  .quality {
    padding: 0.1rem 0.34rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid var(--color-border-accent);
    background: var(--color-surface-3);
    color: var(--color-text-primary);
  }
  .dot { opacity: 0.5; }

  /*
   * A reserved width keeps the action column identical on every row, so the status and title
   * columns start at the same x down the whole list instead of shifting with label length
   * ("Search again" against "View"). Buttons right-align, so the reserve is invisible.
   */
  .actions {
    grid-area: actions;
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 0.45rem;
    align-self: center;
    /* Wide enough for the longest primary label plus Remove plus the overflow slot. */
    min-width: 17.5rem;
  }

  /* Matches the overflow button's footprint so every row's trailing edge is identical. */
  .action-slot-placeholder { width: 2.1rem; height: 2.1rem; flex: 0 0 auto; }
  .action {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    height: 2.1rem;
    padding: 0 0.75rem;
    border-radius: var(--radius-sm, 6px);
    border: 1px solid rgb(255 255 255 / 0.12);
    background: rgb(255 255 255 / 0.04);
    color: var(--color-text-primary);
    font-size: 0.76rem;
    font-weight: 600;
    white-space: nowrap;
    text-decoration: none;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease, box-shadow 120ms ease;
  }
  .action:hover:not(:disabled) { background: rgb(255 255 255 / 0.09); border-color: rgb(255 255 255 / 0.2); }
  .action:disabled, .action[aria-disabled="true"] { opacity: 0.4; cursor: not-allowed; pointer-events: none; }
  .action-primary { border-color: var(--color-border-accent-strong); background: var(--color-surface-3); color: var(--color-text-primary); }
  .action-primary:hover:not(:disabled) { background: var(--color-surface-4); border-color: var(--color-border-glow); }
  .action-danger { color: var(--color-error-text); border-color: color-mix(in srgb, var(--color-error) 30%, transparent); }
  .action-danger:hover:not(:disabled) { background: var(--color-error-muted); border-color: color-mix(in srgb, var(--color-error) 50%, transparent); }

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
    color: var(--color-text-primary);
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
  .menu-item:hover { background: rgb(255 255 255 / 0.06); color: var(--color-text-primary); }
  .menu-item :global(svg) { color: var(--color-text-muted); flex: 0 0 auto; }

  /* ── Mobile / narrow container: title + status stack under the poster, actions drop to a full row ── */
  @container (max-width: 48rem) {
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
    .statusblock,
    .titleblock {
      min-width: 0;
      max-width: 100%;
    }
    /* Stacked layout gives actions their own full-width row, so the desktop reserve is dropped. */
    .actions { padding-top: 0.15rem; justify-content: flex-end; flex-wrap: wrap; min-width: 0; }
    .action-label { display: none; }
    .action { padding: 0 0.65rem; }
    .progress {
      max-width: none;
      min-width: 0;
      width: 100%;
    }
    .progress-value { min-width: 2.2rem; }
    .meta { overflow-wrap: anywhere; }

    /* Let the title, subtitle, and status description wrap to full text instead of truncating — on a
       narrow screen the row has the vertical room, and the failure reason especially must read in full. */
    .title,
    .subtitle,
    .description {
      white-space: normal;
      overflow: visible;
      overflow-wrap: anywhere;
    }

    /* The narrow layout has the vertical room, so the reason is shown in full rather than clamped. */
    .description {
      display: block;
      -webkit-line-clamp: unset;
      line-clamp: unset;
    }
  }
</style>
