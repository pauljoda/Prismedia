<script lang="ts">
  import { onMount, untrack } from "svelte";
  import {
    AlertTriangle,
    BookOpen,
    ChevronLeft,
    ChevronRight,
    List,
    Minus,
    Plus,
    Rows3,
    X,
  } from "@lucide/svelte";
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
  import ReaderShell from "$lib/components/reader/ReaderShell.svelte";
  import { comicTapZone } from "$lib/components/comic-reader";

  type ReaderFlow = "paginated" | "scrolled";

  interface ReaderLocation {
    cfi: string | null;
    fraction: number;
    label: string | null;
  }

  interface TocEntry {
    label: string;
    href: string | null;
    subitems: TocEntry[];
  }

  interface Props {
    sourceUrl: string;
    contentType: string;
    title?: string;
    presentation?: "overlay" | "page";
    closeIcon?: "close" | "back";
    initialLocation?: string | null;
    initialFlow?: ReaderFlow;
    onClose: () => void;
    onLocationChange?: (location: ReaderLocation) => void;
    onFlowChange?: (flow: ReaderFlow) => void;
  }

  let {
    sourceUrl,
    contentType,
    title = "Book",
    presentation = "overlay",
    closeIcon = "close",
    initialLocation = null,
    initialFlow = "paginated",
    onClose,
    onLocationChange,
    onFlowChange,
  }: Props = $props();

  const MIN_FONT = 80;
  const MAX_FONT = 200;

  let shell = $state<ReturnType<typeof ReaderShell>>();
  let stageEl = $state<HTMLDivElement>();
  // The foliate `<foliate-view>` custom element has no published types; treat as a loose handle.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let view: any = null;
  let ready = $state(false);
  let errorMessage = $state<string | null>(null);
  let flow = $state<ReaderFlow>(untrack(() => initialFlow));
  let fontPercent = $state(100);
  let counterLabel = $state("");
  let atEnd = $state(false);
  let toc = $state.raw<TocEntry[]>([]);
  let tocOpen = $state(false);
  // Fixed-layout books (PDF) have no reflow, so font-size and paged/scrolled flow do not apply.
  let isFixedLayout = $state(false);

  const hasToc = $derived(toc.length > 0);

  // Brass accent so in-book links read as interactive without the default deep-blue.
  function contentStyles(percent: number): string {
    return `
      html { color-scheme: dark; font-size: ${percent}%; }
      html, body { background: transparent !important; color: #e7e7ea; }
      a, a:link, a:visited { color: #f2c26a; text-decoration: underline; text-underline-offset: 2px; }
      a:hover { color: #f7d59a; }
      img { background: transparent; }
    `;
  }

  function applyFlow(next: ReaderFlow) {
    view?.renderer?.setAttribute?.("flow", next === "scrolled" ? "scrolled" : "paginated");
  }

  function applyStyles() {
    view?.renderer?.setStyles?.(contentStyles(fontPercent));
  }

  function setFlow(next: ReaderFlow) {
    if (next === flow) return;
    flow = next;
    applyFlow(next);
    onFlowChange?.(next);
  }

  function changeFont(delta: number) {
    const next = Math.max(MIN_FONT, Math.min(MAX_FONT, fontPercent + delta));
    if (next === fontPercent) return;
    fontPercent = next;
    applyStyles();
  }

  function goPrev() {
    view?.goLeft?.();
    shell?.showControls();
  }

  function goNext() {
    view?.goRight?.();
    shell?.showControls();
  }

  function normalizeToc(items: unknown): TocEntry[] {
    if (!Array.isArray(items)) return [];
    return items
      .map((item) => {
        const entry = item as { label?: unknown; href?: unknown; subitems?: unknown };
        const label = typeof entry.label === "string" ? entry.label.trim() : "";
        return {
          label,
          href: typeof entry.href === "string" ? entry.href : null,
          subitems: normalizeToc(entry.subitems),
        };
      })
      .filter((entry) => entry.label.length > 0 || entry.subitems.length > 0);
  }

  function openToc(entry: TocEntry) {
    if (!entry.href) return;
    tocOpen = false;
    try {
      view?.goTo?.(entry.href);
    } catch {
      // ignore navigation failures for malformed hrefs
    }
  }

  function handleRelocate(event: Event) {
    const detail = (event as CustomEvent).detail ?? {};
    const fraction = typeof detail.fraction === "number" ? detail.fraction : 0;
    const cfi = typeof detail.cfi === "string" ? detail.cfi : null;
    const label = detail.tocItem?.label?.trim() || null;
    atEnd = fraction >= 0.999;
    counterLabel = `${Math.round(fraction * 100)}%${label ? ` · ${label}` : ""}`;
    onLocationChange?.({ cfi, fraction, label });
  }

  // Comic-style tap zones injected into the rendered content document: left/right thirds
  // turn the page, the centre toggles the chrome. Swipe is handled by foliate natively.
  function handleContentTap(event: MouseEvent, doc: Document) {
    const target = event.target as HTMLElement | null;
    if (target?.closest?.("a[href]")) return;
    const selection = doc.getSelection?.();
    if (selection && !selection.isCollapsed) return;

    // Use the iframe's visible viewport width. In paged mode foliate columnizes the document
    // so documentElement.clientWidth is the full multi-column width, which would skew the zones.
    const width = doc.defaultView?.innerWidth || doc.documentElement?.clientWidth || 0;
    if (width <= 0) {
      shell?.toggleControls();
      return;
    }
    const zone = comicTapZone(event.clientX, width);
    if (zone === "previous") goPrev();
    else if (zone === "next") goNext();
    else shell?.toggleControls();
  }

  function attachContentTaps(doc: Document) {
    // foliate can emit `load` for a document more than once; bind the tap handler only once.
    const marked = doc as Document & { __prismediaTaps?: boolean };
    if (marked.__prismediaTaps) return;
    marked.__prismediaTaps = true;
    doc.addEventListener("click", (event) => handleContentTap(event as MouseEvent, doc));
  }

  onMount(() => {
    let disposed = false;

    void (async () => {
      try {
        // Registers the <foliate-view> custom element (vendored, client-only).
        await import("$lib/vendor/foliate-js/view.js");
        if (disposed || !stageEl) return;

        const absoluteUrl = toApiUrl(sourceUrl) ?? sourceUrl;
        const response = await fetch(absoluteUrl);
        if (!response.ok) {
          throw new Error(`Failed to load book (${response.status})`);
        }
        const blob = await response.blob();
        const fileName = contentType.includes("pdf") ? "book.pdf" : "book.epub";
        const file = new File([blob], fileName, { type: contentType });
        if (disposed) return;

        view = document.createElement("foliate-view");
        view.style.width = "100%";
        view.style.height = "100%";
        stageEl.append(view);

        // Inject tap zones into every rendered section/page document as it loads.
        view.addEventListener("load", (event: Event) => {
          const doc = (event as CustomEvent).detail?.doc as Document | undefined;
          if (doc) attachContentTaps(doc);
        });

        await view.open(file);
        if (disposed) return;

        toc = normalizeToc(view.book?.toc);
        isFixedLayout = Boolean(view.isFixedLayout);
        view.addEventListener("relocate", handleRelocate);
        applyFlow(flow);
        applyStyles();
        await view.init({
          lastLocation: initialLocation ?? undefined,
          showTextStart: !initialLocation,
        });
        ready = true;
      } catch (err) {
        if (!disposed) {
          errorMessage = err instanceof Error ? err.message : String(err);
        }
      }
    })();

    return () => {
      disposed = true;
      try {
        view?.removeEventListener?.("relocate", handleRelocate);
        view?.close?.();
        view?.remove?.();
      } catch {
        // ignore teardown races
      }
      view = null;
    };
  });
</script>

<ReaderShell
  bind:this={shell}
  {title}
  {presentation}
  {closeIcon}
  {onClose}
  onPrev={goPrev}
  onNext={goNext}
  onActivate={goNext}
>
  {#snippet counter()}{counterLabel}{/snippet}

  {#snippet controls()}
    {#if hasToc}
      <button
        type="button"
        onclick={() => (tocOpen = true)}
        class="reader-mode-button"
        aria-label="Table of contents"
        title="Contents"
      >
        <List class="h-4 w-4" />
        <span class="hidden sm:inline">Contents</span>
      </button>
    {/if}

    {#if !isFixedLayout}
      <div class="flex items-center gap-1 border-l border-border-subtle pl-2">
        <button
          type="button"
          onclick={() => setFlow("paginated")}
          class:active-reader-control={flow === "paginated"}
          class="reader-mode-button"
          aria-label="Paged reading"
          title="Paged reading"
        >
          <BookOpen class="h-4 w-4" />
          <span class="hidden sm:inline">Paged</span>
        </button>
        <button
          type="button"
          onclick={() => setFlow("scrolled")}
          class:active-reader-control={flow === "scrolled"}
          class="reader-mode-button"
          aria-label="Scrolled reading"
          title="Scrolled reading"
        >
          <Rows3 class="h-4 w-4" />
          <span class="hidden sm:inline">Scroll</span>
        </button>
      </div>
    {/if}

    {#if !isFixedLayout}
    <div class="hidden items-center gap-1 border-l border-border-subtle pl-2 sm:flex">
      <button
        type="button"
        onclick={() => changeFont(-10)}
        disabled={fontPercent <= MIN_FONT}
        class="reader-mode-button"
        aria-label="Decrease font size"
        title="Decrease font size"
      >
        <Minus class="h-4 w-4" />
      </button>
      <span class="font-mono text-[0.62rem] tabular-nums text-text-muted">{fontPercent}%</span>
      <button
        type="button"
        onclick={() => changeFont(10)}
        disabled={fontPercent >= MAX_FONT}
        class="reader-mode-button"
        aria-label="Increase font size"
        title="Increase font size"
      >
        <Plus class="h-4 w-4" />
      </button>
    </div>
    {/if}
  {/snippet}

  <div class="book-reader-stage" bind:this={stageEl}>
    {#if ready}
      <button
        type="button"
        onclick={goPrev}
        data-reader-control
        class="reader-nav-button left-2 sm:left-3"
        aria-label="Previous page"
        title="Previous (←)"
      >
        <ChevronLeft class="h-6 w-6" />
      </button>
      <button
        type="button"
        onclick={goNext}
        data-reader-control
        class="reader-nav-button right-2 sm:right-3"
        aria-label="Next page"
        title="Next (→)"
      >
        <ChevronRight class="h-6 w-6" />
      </button>
      {#if atEnd}
        <div class="book-reader-end" data-reader-control>End of book</div>
      {/if}
    {/if}

    {#if errorMessage}
      <div class="book-reader-message">
        <AlertTriangle class="h-5 w-5" />
        <p>{errorMessage}</p>
      </div>
    {:else if !ready}
      <div class="book-reader-message">
        <p>Opening book…</p>
      </div>
    {/if}
  </div>
</ReaderShell>

{#if tocOpen}
  {@const closeToc = () => (tocOpen = false)}
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div class="toc-overlay" onclick={closeToc}>
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="toc-panel" role="dialog" aria-label="Table of contents" tabindex="-1" onclick={(e) => e.stopPropagation()}>
      <div class="toc-header">
        <span class="toc-title">Contents</span>
        <button type="button" class="reader-mode-button" onclick={closeToc} aria-label="Close contents" title="Close">
          <X class="h-4 w-4" />
        </button>
      </div>
      <nav class="toc-list">
        {@render tocItems(toc, 0)}
      </nav>
    </div>
  </div>
{/if}

{#snippet tocItems(items: TocEntry[], depth: number)}
  {#each items as entry (entry.label + (entry.href ?? ""))}
    <button
      type="button"
      class="toc-item"
      style={`padding-left: ${0.85 + depth * 0.9}rem`}
      disabled={!entry.href}
      onclick={() => openToc(entry)}
    >
      {entry.label}
    </button>
    {#if entry.subitems.length > 0}
      {@render tocItems(entry.subitems, depth + 1)}
    {/if}
  {/each}
{/snippet}

<style>
  .book-reader-stage {
    position: absolute;
    inset: 0;
    display: block;
    background: #0b0c0f;
    overflow: hidden;
  }

  .book-reader-message {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    gap: 0.6rem;
    color: var(--color-text-secondary);
    text-align: center;
    pointer-events: none;
  }

  .book-reader-end {
    position: absolute;
    bottom: 1.25rem;
    left: 50%;
    transform: translateX(-50%);
    z-index: 12;
    border: 1px solid var(--color-border-accent);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    padding: 0.35rem 0.8rem;
    color: var(--color-text-accent-bright);
    font-family: var(--font-mono, monospace);
    font-size: 0.62rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    backdrop-filter: blur(var(--glass-blur-sm));
  }

  .reader-mode-button {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-default);
    background: var(--color-overlay-heavy);
    padding: 0.45rem 0.65rem;
    border-radius: var(--radius-sm);
    color: var(--color-text-secondary);
    font-size: 0.72rem;
    line-height: 1;
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical);
  }

  .reader-mode-button:hover:not(:disabled),
  .reader-mode-button:focus-visible,
  .active-reader-control {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
  }

  .reader-mode-button:disabled {
    opacity: 0.5;
  }

  .reader-nav-button {
    position: absolute;
    top: 50%;
    z-index: 10;
    display: flex;
    height: 2.75rem;
    width: 2.75rem;
    transform: translateY(-50%);
    align-items: center;
    justify-content: center;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    background: var(--color-overlay-heavy);
    color: var(--color-text-secondary);
    backdrop-filter: blur(var(--glass-blur-sm));
    transition:
      border-color var(--duration-normal) var(--ease-mechanical),
      color var(--duration-normal) var(--ease-mechanical),
      box-shadow var(--duration-normal) var(--ease-mechanical);
  }

  .reader-nav-button:hover,
  .reader-nav-button:focus-visible {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
  }

  .toc-overlay {
    position: fixed;
    inset: 0;
    z-index: 2147483100;
    display: flex;
    justify-content: flex-start;
    background: rgba(4, 5, 7, 0.55);
    backdrop-filter: blur(var(--glass-blur-sm));
  }

  .toc-panel {
    display: flex;
    width: min(22rem, 86vw);
    height: 100%;
    flex-direction: column;
    border-right: 1px solid var(--color-border-default);
    background: var(--color-surface-1, #0e1014);
    box-shadow: var(--shadow-glow-accent);
  }

  .toc-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    padding: max(0.75rem, env(safe-area-inset-top)) 0.85rem 0.75rem;
    border-bottom: 1px solid var(--color-border-default);
  }

  .toc-title {
    font-family: var(--font-mono, monospace);
    font-size: 0.68rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: var(--color-text-accent-bright);
  }

  .toc-list {
    flex: 1 1 auto;
    overflow-y: auto;
    padding: 0.4rem 0.4rem 1.5rem;
  }

  .toc-item {
    display: block;
    width: 100%;
    border-radius: var(--radius-sm);
    padding: 0.5rem 0.85rem;
    text-align: left;
    font-size: 0.82rem;
    color: var(--color-text-secondary);
    transition:
      background-color var(--duration-fast) var(--ease-mechanical),
      color var(--duration-fast) var(--ease-mechanical);
  }

  .toc-item:hover:not(:disabled),
  .toc-item:focus-visible {
    background: var(--color-overlay-heavy);
    color: var(--color-text-accent-bright);
    outline: none;
  }

  .toc-item:disabled {
    color: var(--color-text-muted);
    cursor: default;
  }
</style>
