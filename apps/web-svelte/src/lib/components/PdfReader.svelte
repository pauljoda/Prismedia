<script lang="ts">
  import { onMount, tick } from "svelte";
  import {
    AlertTriangle,
    ChevronDown,
    ChevronLeft,
    ChevronRight,
    ChevronUp,
    Download,
    List,
    Maximize,
    Minus,
    Plus,
    Search,
    StretchVertical,
    X,
  } from "@lucide/svelte";
  import { BookOpen, Rows3 } from "@lucide/svelte";
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
  import { comicTapZone } from "$lib/components/comic-reader";
  import ReaderShell from "$lib/components/reader/ReaderShell.svelte";

  type ReaderFlow = "scrolled" | "paged";

  interface TocEntry {
    label: string;
    pageIndex: number | null;
    subitems: TocEntry[];
  }

  interface Props {
    sourceUrl: string;
    title?: string;
    presentation?: "overlay" | "page";
    closeIcon?: "close" | "back";
    initialPage?: number;
    onClose: () => void;
    onPageChange?: (page: number, pageCount: number) => void;
  }

  let {
    sourceUrl,
    title = "Document",
    presentation = "overlay",
    closeIcon = "close",
    initialPage = 0,
    onClose,
    onPageChange,
  }: Props = $props();

  let shell = $state<ReturnType<typeof ReaderShell>>();
  let scrollEl = $state<HTMLDivElement>();
  let ready = $state(false);
  let errorMessage = $state<string | null>(null);
  let pageCount = $state(0);
  let currentPage = $state(0);
  let toc = $state.raw<TocEntry[]>([]);
  let tocOpen = $state(false);

  const MIN_SCALE = 0.25;
  const MAX_SCALE = 6;
  let scale = $state(1);
  let gapless = $state(false);
  const zoomPercent = $derived(Math.round(scale * 100));
  const downloadHref = $derived(toApiUrl(sourceUrl));

  const hasToc = $derived(toc.length > 0);
  const pageIndexes = $derived(Array.from({ length: pageCount }, (_, i) => i));

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let pdf: any = null;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let pdfjsLib: any = null;
  let wrappers: HTMLDivElement[] = [];
  const rendered = new Set<number>();
  const rendering = new Set<number>();
  let observer: IntersectionObserver | null = null;
  // Unscaled page-1 dimensions, used to derive fit scales and placeholder sizes (the scroll
  // height — and each page's offsetTop — must be correct before pages lazily render so resume works).
  let pageBaseW = 0;
  let pageBaseH = 0;
  let baseWidth = 0;
  let baseHeight = 0;

  function fitWidthScale(): number {
    if (!scrollEl || pageBaseW <= 0) return scale || 1;
    const available = Math.min(scrollEl.clientWidth - 24, 1400);
    return available > 0 ? available / pageBaseW : 1;
  }

  function fitPageScale(): number {
    if (!scrollEl || pageBaseW <= 0 || pageBaseH <= 0) return scale || 1;
    const availW = scrollEl.clientWidth - 24;
    const availH = scrollEl.clientHeight - 24;
    return Math.min(availW / pageBaseW, availH / pageBaseH);
  }

  function clampScale(value: number): number {
    return Math.max(MIN_SCALE, Math.min(MAX_SCALE, value));
  }

  // Re-render the visible pages at the current scale and keep the current page in view.
  function applyScale() {
    if (pageBaseW <= 0) return;
    baseWidth = pageBaseW * scale;
    baseHeight = pageBaseH * scale;
    for (const wrapper of wrappers) {
      if (wrapper) {
        wrapper.style.width = `${baseWidth}px`;
        wrapper.style.height = `${baseHeight}px`;
      }
    }
    for (const index of [...rendered]) unloadPage(index);
    rendered.clear();
    // Re-observe so the IntersectionObserver re-renders whatever is currently on screen.
    observer?.disconnect();
    for (const wrapper of wrappers) if (wrapper) observer?.observe(wrapper);
    scrollToPage(currentPage, "auto");
  }

  function setScale(next: number) {
    const clamped = clampScale(next);
    if (Math.abs(clamped - scale) < 0.001) return;
    scale = clamped;
    applyScale();
  }

  const fitWidth = () => setScale(fitWidthScale());
  const fitPage = () => setScale(fitPageScale());
  const zoomIn = () => setScale(scale * 1.2);
  const zoomOut = () => setScale(scale / 1.2);

  function toggleGap() {
    gapless = !gapless;
  }

  // ── Paged mode (single page per view via scroll-snap + comic-style gestures) ──
  let flow = $state<ReaderFlow>("scrolled");

  function setFlow(next: ReaderFlow) {
    if (next === flow) return;
    flow = next;
    if (next === "paged") setScale(fitPageScale());
    else setScale(fitWidthScale());
  }

  // Paged-mode tap zones via a plain click (so it never blocks native scroll-snap swipe or wheel).
  // The text layer is non-interactive in paged mode, so taps land here: sides turn, centre toggles.
  function handleStageClick(event: MouseEvent) {
    if (flow !== "paged") return;
    if ((event.target as HTMLElement)?.closest?.("[data-reader-control]")) return;
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const zone = comicTapZone(event.clientX - rect.left, rect.width);
    if (zone === "previous") goPrev();
    else if (zone === "next") goNext();
    else shell?.toggleControls();
  }

  // ── In-document search ──
  let searchOpen = $state(false);
  let searchQuery = $state("");
  let searchBusy = $state(false);
  let searchMatches = $state.raw<number[]>([]); // page index of each occurrence, in order
  let searchActive = $state(0);
  let textIndex: string[] | null = null;
  let searchDebounce = 0;
  let searchInputEl = $state<HTMLInputElement>();

  async function buildTextIndex() {
    if (textIndex || !pdf) return;
    const idx: string[] = [];
    for (let i = 1; i <= pageCount; i++) {
      try {
        const page = await pdf.getPage(i);
        const content = await page.getTextContent();
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        idx.push(content.items.map((it: any) => (typeof it.str === "string" ? it.str : "")).join(" ").toLowerCase());
      } catch {
        idx.push("");
      }
    }
    textIndex = idx;
  }

  async function runSearch() {
    const query = searchQuery.trim().toLowerCase();
    if (!query) {
      searchMatches = [];
      searchActive = 0;
      return;
    }
    searchBusy = true;
    await buildTextIndex();
    searchBusy = false;
    const matches: number[] = [];
    textIndex?.forEach((text, page) => {
      let from = text.indexOf(query);
      while (from !== -1) {
        matches.push(page);
        from = text.indexOf(query, from + query.length);
      }
    });
    searchMatches = matches;
    searchActive = 0;
    if (matches.length > 0) scrollToPage(matches[0]);
  }

  function onSearchInput() {
    if (searchDebounce) window.clearTimeout(searchDebounce);
    searchDebounce = window.setTimeout(() => void runSearch(), 250);
  }

  function gotoMatch(delta: number) {
    if (searchMatches.length === 0) return;
    searchActive = (searchActive + delta + searchMatches.length) % searchMatches.length;
    scrollToPage(searchMatches[searchActive]);
  }

  async function toggleSearch() {
    searchOpen = !searchOpen;
    if (searchOpen) {
      await tick();
      searchInputEl?.focus();
    } else {
      searchQuery = "";
      searchMatches = [];
      searchActive = 0;
    }
  }

  function onSearchKeydown(event: KeyboardEvent) {
    if (event.key === "Enter") {
      event.preventDefault();
      if (searchMatches.length) gotoMatch(event.shiftKey ? -1 : 1);
      else void runSearch();
    } else if (event.key === "Escape") {
      event.preventDefault();
      void toggleSearch();
    }
  }

  // Pinch-to-zoom: two-finger gesture scales the page column live via CSS transform, then commits
  // to a crisp re-render on release. One-finger scrolling stays native (touch-action: pan-y).
  const pinchPointers = new Map<number, { x: number; y: number }>();
  let pinchStartDist = 0;
  let pinchStartScale = 1;
  let pinching = $state(false);
  let pinchRatio = $state(1);

  function pointerDistance(): number {
    const pts = [...pinchPointers.values()];
    return pts.length < 2 ? 0 : Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
  }

  function handleStagePointerDown(event: PointerEvent) {
    if (event.pointerType === "mouse" || flow === "paged") return;
    pinchPointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pinchPointers.size === 2) {
      pinching = true;
      pinchStartDist = pointerDistance();
      pinchStartScale = scale;
      pinchRatio = 1;
    }
  }

  function handleStagePointerMove(event: PointerEvent) {
    if (!pinchPointers.has(event.pointerId)) return;
    pinchPointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pinching && pinchPointers.size === 2) {
      event.preventDefault();
      const dist = pointerDistance();
      pinchRatio = pinchStartDist > 0 ? dist / pinchStartDist : 1;
    }
  }

  function handleStagePointerEnd(event: PointerEvent) {
    if (!pinchPointers.delete(event.pointerId)) return;
    if (pinching && pinchPointers.size < 2) {
      pinching = false;
      const target = clampScale(pinchStartScale * pinchRatio);
      pinchRatio = 1;
      setScale(target);
    }
  }

  async function renderPage(index: number) {
    if (!pdf || rendered.has(index) || rendering.has(index)) return;
    const wrapper = wrappers[index];
    if (!wrapper) return;
    rendering.add(index);
    try {
      const page = await pdf.getPage(index + 1);
      const cssViewport = page.getViewport({ scale });
      const dpr = Math.min(globalThis.devicePixelRatio || 1, 2);
      const deviceViewport = page.getViewport({ scale: scale * dpr });

      wrapper.style.height = `${cssViewport.height}px`;
      wrapper.style.width = `${cssViewport.width}px`;

      const canvas = document.createElement("canvas");
      canvas.width = Math.ceil(deviceViewport.width);
      canvas.height = Math.ceil(deviceViewport.height);
      canvas.style.width = `${cssViewport.width}px`;
      canvas.style.height = `${cssViewport.height}px`;
      canvas.className = "pdf-canvas";
      const ctx = canvas.getContext("2d");
      await page.render({ canvasContext: ctx, viewport: deviceViewport }).promise;

      const textLayerDiv = document.createElement("div");
      textLayerDiv.className = "pdf-text-layer";
      textLayerDiv.style.width = `${cssViewport.width}px`;
      textLayerDiv.style.height = `${cssViewport.height}px`;
      textLayerDiv.style.setProperty("--scale-factor", String(scale));
      const textLayer = new pdfjsLib.TextLayer({
        textContentSource: page.streamTextContent(),
        container: textLayerDiv,
        viewport: cssViewport,
      });
      await textLayer.render();

      const linkLayer = await buildLinkLayer(page, cssViewport);

      wrapper.replaceChildren(canvas, textLayerDiv, ...(linkLayer ? [linkLayer] : []));
      rendered.add(index);
    } catch {
      // leave the placeholder; it will retry if it scrolls back into view
    } finally {
      rendering.delete(index);
    }
  }

  function unloadPage(index: number) {
    if (!rendered.has(index)) return;
    const wrapper = wrappers[index];
    if (wrapper) wrapper.replaceChildren();
    rendered.delete(index);
  }

  function updateCurrentPage() {
    if (!scrollEl || pageCount === 0) return;
    const mid = scrollEl.scrollTop + scrollEl.clientHeight / 2;
    let next = currentPage;
    for (let i = 0; i < wrappers.length; i++) {
      const w = wrappers[i];
      if (w && w.offsetTop <= mid) next = i;
      else break;
    }
    if (next !== currentPage) {
      currentPage = next;
      onPageChange?.(currentPage, pageCount);
    }
  }

  let scrollRaf = 0;
  function handleScroll() {
    shell?.showControls();
    if (scrollRaf) return;
    scrollRaf = requestAnimationFrame(() => {
      scrollRaf = 0;
      updateCurrentPage();
    });
  }

  function scrollToPage(index: number, behavior: ScrollBehavior = "smooth") {
    const wrapper = wrappers[Math.max(0, Math.min(index, wrappers.length - 1))];
    if (wrapper && scrollEl) {
      scrollEl.scrollTo({ top: wrapper.offsetTop - 8, behavior });
    }
  }

  function goPrev() {
    scrollToPage(currentPage - 1);
    shell?.showControls();
  }

  function goNext() {
    scrollToPage(currentPage + 1);
    shell?.showControls();
  }

  async function resolveDestPage(dest: unknown): Promise<number | null> {
    try {
      const explicit = typeof dest === "string" ? await pdf.getDestination(dest) : dest;
      if (Array.isArray(explicit) && explicit[0]) return await pdf.getPageIndex(explicit[0]);
    } catch {
      /* ignore */
    }
    return null;
  }

  // Build a transparent overlay of clickable links from the page's annotations: internal links
  // jump to the target page, external URLs open in a new tab. Positioned via the viewport so it
  // does not depend on pdf.js's annotation-layer CSS.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  async function buildLinkLayer(page: any, viewport: any): Promise<HTMLDivElement | null> {
    let annotations: Array<Record<string, unknown>> = [];
    try {
      annotations = await page.getAnnotations();
    } catch {
      return null;
    }
    const links = annotations.filter(
      (a) => a.subtype === "Link" && (typeof a.url === "string" || a.dest != null),
    );
    if (links.length === 0) return null;

    const layer = document.createElement("div");
    layer.className = "pdf-link-layer";
    layer.style.width = `${viewport.width}px`;
    layer.style.height = `${viewport.height}px`;
    for (const annotation of links) {
      const rect = viewport.convertToViewportRectangle(annotation.rect as number[]);
      const left = Math.min(rect[0], rect[2]);
      const top = Math.min(rect[1], rect[3]);
      const width = Math.abs(rect[2] - rect[0]);
      const height = Math.abs(rect[3] - rect[1]);
      const link = document.createElement("a");
      link.className = "pdf-link";
      link.style.left = `${left}px`;
      link.style.top = `${top}px`;
      link.style.width = `${width}px`;
      link.style.height = `${height}px`;
      if (typeof annotation.url === "string") {
        link.href = annotation.url;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
      } else {
        link.href = "#";
        const dest = annotation.dest;
        link.addEventListener("click", (event) => {
          event.preventDefault();
          void resolveDestPage(dest).then((idx) => {
            if (idx !== null) scrollToPage(idx);
          });
        });
      }
      layer.append(link);
    }
    return layer;
  }

  function normalizeOutline(items: unknown, resolve: (dest: unknown) => Promise<number | null>): Promise<TocEntry[]> {
    if (!Array.isArray(items)) return Promise.resolve([]);
    return Promise.all(
      items.map(async (item) => {
        const entry = item as { title?: unknown; dest?: unknown; items?: unknown };
        const label = typeof entry.title === "string" ? entry.title.trim() : "";
        const pageIndex = await resolve(entry.dest).catch(() => null);
        const subitems = await normalizeOutline(entry.items, resolve);
        return { label, pageIndex, subitems };
      }),
    ).then((entries) => entries.filter((e) => e.label.length > 0 || e.subitems.length > 0));
  }

  function openToc(entry: TocEntry) {
    if (entry.pageIndex === null) return;
    tocOpen = false;
    scrollToPage(entry.pageIndex);
  }

  onMount(() => {
    let disposed = false;

    void (async () => {
      try {
        pdfjsLib = await import("pdfjs-dist/build/pdf.mjs");
        const workerModule = await import("pdfjs-dist/build/pdf.worker.min.mjs?url");
        pdfjsLib.GlobalWorkerOptions.workerSrc = (workerModule as { default: string }).default;

        const absoluteUrl = toApiUrl(sourceUrl) ?? sourceUrl;
        const response = await fetch(absoluteUrl);
        if (!response.ok) throw new Error(`Failed to load PDF (${response.status})`);
        const data = await response.arrayBuffer();
        if (disposed) return;

        pdf = await pdfjsLib.getDocument({ data }).promise;
        if (disposed) return;
        pageCount = pdf.numPages;

        // Size placeholders from the first page so the scroll height (and each page's offsetTop)
        // is correct before pages lazily render — required for resume and the scrollbar.
        try {
          const first = await pdf.getPage(1);
          const vp = first.getViewport({ scale: 1 });
          pageBaseW = vp.width;
          pageBaseH = vp.height;
          scale = clampScale(fitWidthScale());
          baseWidth = pageBaseW * scale;
          baseHeight = pageBaseH * scale;
        } catch {
          baseWidth = scrollEl ? Math.min(scrollEl.clientWidth - 24, 1100) : 800;
          baseHeight = baseWidth * 1.414;
        }

        // Resolve the outline (chapters) to page indexes.
        try {
          const outline = await pdf.getOutline();
          if (outline?.length) {
            toc = await normalizeOutline(outline, async (dest) => {
              const explicit = typeof dest === "string" ? await pdf.getDestination(dest) : dest;
              if (!Array.isArray(explicit) || !explicit[0]) return null;
              return await pdf.getPageIndex(explicit[0]);
            });
          }
        } catch {
          /* outline is optional */
        }

        ready = true;
        await tick();
        if (disposed) return;

        // Size placeholders and observe them for lazy rendering.
        for (const wrapper of wrappers) {
          if (wrapper) {
            wrapper.style.width = `${baseWidth}px`;
            wrapper.style.height = `${baseHeight}px`;
          }
        }
        observer = new IntersectionObserver(
          (entries) => {
            for (const e of entries) {
              const index = Number((e.target as HTMLElement).dataset.pageIndex);
              if (Number.isNaN(index)) continue;
              if (e.isIntersecting) void renderPage(index);
              else unloadPage(index);
            }
          },
          { root: scrollEl, rootMargin: "200% 0px" },
        );
        for (const wrapper of wrappers) if (wrapper) observer.observe(wrapper);

        if (initialPage > 0) {
          currentPage = Math.min(initialPage, pageCount - 1);
          await tick();
          scrollToPage(currentPage, "auto");
        }
      } catch (err) {
        if (!disposed) errorMessage = err instanceof Error ? err.message : String(err);
      }
    })();

    return () => {
      disposed = true;
      observer?.disconnect();
      observer = null;
      if (scrollRaf) cancelAnimationFrame(scrollRaf);
      try {
        pdf?.destroy?.();
      } catch {
        /* ignore */
      }
      pdf = null;
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
  {#snippet counter()}{pageCount > 0 ? `Page ${currentPage + 1} / ${pageCount}` : ""}{/snippet}

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

    <button
      type="button"
      onclick={() => void toggleSearch()}
      class:active-reader-control={searchOpen}
      class="reader-mode-button"
      aria-label="Search document"
      title="Search"
    >
      <Search class="h-4 w-4" />
    </button>

    <div class="flex items-center gap-1 border-l border-border-subtle pl-2">
      <button
        type="button"
        onclick={() => setFlow("scrolled")}
        class:active-reader-control={flow === "scrolled"}
        class="reader-mode-button"
        aria-label="Scrolled reading"
        title="Scrolled (selectable text)"
      >
        <Rows3 class="h-4 w-4" />
      </button>
      <button
        type="button"
        onclick={() => setFlow("paged")}
        class:active-reader-control={flow === "paged"}
        class="reader-mode-button"
        aria-label="Paged reading"
        title="Paged (one page per view)"
      >
        <BookOpen class="h-4 w-4" />
      </button>
    </div>

    {#if flow === "scrolled"}
      <div class="flex items-center gap-1 border-l border-border-subtle pl-2">
        <button
          type="button"
          onclick={zoomOut}
          disabled={scale <= MIN_SCALE}
          class="reader-mode-button"
          aria-label="Zoom out"
          title="Zoom out"
        >
          <Minus class="h-4 w-4" />
        </button>
        <span class="min-w-[3ch] text-center font-mono text-[0.62rem] tabular-nums text-text-muted">{zoomPercent}%</span>
        <button
          type="button"
          onclick={zoomIn}
          disabled={scale >= MAX_SCALE}
          class="reader-mode-button"
          aria-label="Zoom in"
          title="Zoom in"
        >
          <Plus class="h-4 w-4" />
        </button>
        <button type="button" onclick={fitWidth} class="reader-mode-button" aria-label="Fit width" title="Fit width">
          <span class="text-[0.62rem]">W</span>
        </button>
        <button type="button" onclick={fitPage} class="reader-mode-button" aria-label="Fit page" title="Fit one page in view">
          <Maximize class="h-4 w-4" />
        </button>
      </div>
    {/if}

    <div class="flex items-center gap-1 border-l border-border-subtle pl-2">
      {#if flow === "scrolled"}
        <button
          type="button"
          onclick={toggleGap}
          class:active-reader-control={gapless}
          class="reader-mode-button"
          aria-label="Toggle page gaps"
          title={gapless ? "Show page gaps" : "Remove page gaps"}
        >
          <StretchVertical class="h-4 w-4" />
        </button>
      {/if}
      {#if downloadHref}
        <a
          href={downloadHref}
          download
          class="reader-mode-button"
          aria-label="Download PDF"
          title="Download original PDF"
        >
          <Download class="h-4 w-4" />
        </a>
      {/if}
    </div>
  {/snippet}

  <!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events -->
  <div
    class="pdf-stage"
    class:paged={flow === "paged"}
    bind:this={scrollEl}
    onscroll={handleScroll}
    onclick={handleStageClick}
    onpointerdown={handleStagePointerDown}
    onpointermove={handleStagePointerMove}
    onpointerup={handleStagePointerEnd}
    onpointercancel={handleStagePointerEnd}
  >
    {#if ready}
      <div
        class="pdf-pages"
        class:gapless
        style={pinching ? `transform: scale(${pinchRatio}); transform-origin: center top` : ""}
      >
        {#each pageIndexes as index (index)}
          <div class="pdf-page" data-page-index={index} bind:this={wrappers[index]}></div>
        {/each}
      </div>
    {/if}

    {#if errorMessage}
      <div class="pdf-message">
        <AlertTriangle class="h-5 w-5" />
        <p>{errorMessage}</p>
      </div>
    {:else if !ready}
      <div class="pdf-message">
        <p>Opening document…</p>
      </div>
    {/if}
  </div>

  {#if ready && flow === "paged"}
    <!-- Fixed over the stage (siblings, not inside the scroller) so they don't scroll away. -->
    <button
      type="button"
      onclick={goPrev}
      data-reader-control
      class="reader-nav-button reader-nav-prev"
      aria-label="Previous page"
      title="Previous (←)"
    >
      <ChevronLeft class="h-6 w-6" />
    </button>
    <button
      type="button"
      onclick={goNext}
      data-reader-control
      class="reader-nav-button reader-nav-next"
      aria-label="Next page"
      title="Next (→)"
    >
      <ChevronRight class="h-6 w-6" />
    </button>
  {/if}

  {#if searchOpen}
    <div class="pdf-search" data-reader-control>
      <Search class="h-4 w-4 shrink-0 text-text-muted" />
      <input
        bind:this={searchInputEl}
        bind:value={searchQuery}
        oninput={onSearchInput}
        onkeydown={onSearchKeydown}
        type="search"
        placeholder="Search document…"
        class="pdf-search-input"
      />
      <span class="pdf-search-count">
        {#if searchBusy}…{:else if searchQuery.trim()}{searchMatches.length ? `${searchActive + 1}/${searchMatches.length}` : "0/0"}{/if}
      </span>
      <button
        type="button"
        class="reader-mode-button"
        onclick={() => gotoMatch(-1)}
        disabled={searchMatches.length === 0}
        aria-label="Previous match"
        title="Previous match"
      >
        <ChevronUp class="h-4 w-4" />
      </button>
      <button
        type="button"
        class="reader-mode-button"
        onclick={() => gotoMatch(1)}
        disabled={searchMatches.length === 0}
        aria-label="Next match"
        title="Next match"
      >
        <ChevronDown class="h-4 w-4" />
      </button>
      <button type="button" class="reader-mode-button" onclick={() => void toggleSearch()} aria-label="Close search" title="Close">
        <X class="h-4 w-4" />
      </button>
    </div>
  {/if}
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
  {#each items as entry (entry.label + (entry.pageIndex ?? ""))}
    <button
      type="button"
      class="toc-item"
      style={`padding-left: ${0.85 + depth * 0.9}rem`}
      disabled={entry.pageIndex === null}
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
  .pdf-stage {
    position: absolute;
    inset: 0;
    overflow-y: auto;
    overflow-x: auto;
    background: #0b0c0f;
    overscroll-behavior: contain;
    /* Allow native one-finger scroll while we handle two-finger pinch ourselves. */
    touch-action: pan-x pan-y;
  }

  .pdf-pages {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1rem;
    padding: max(3.5rem, env(safe-area-inset-top)) 0 4rem;
  }

  /* Stitch pages together with no gap for a continuous document feel. */
  .pdf-pages.gapless {
    gap: 0;
  }

  /* Paged mode: one page per view via scroll-snap; the gesture overlay drives turns. */
  .pdf-stage.paged {
    scroll-snap-type: y mandatory;
  }

  .pdf-stage.paged .pdf-page {
    scroll-snap-align: center;
    scroll-snap-stop: always;
  }

  /* In paged mode the text layer must not eat taps (it has no selection there); taps drive
     the comic-style tap zones instead. */
  .pdf-stage.paged :global(.pdf-text-layer),
  .pdf-stage.paged :global(.pdf-link-layer) {
    pointer-events: none;
  }

  .reader-nav-button {
    position: absolute;
    top: 50%;
    z-index: 10;
    display: none;
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
  }

  .reader-nav-prev {
    left: 0.75rem;
  }

  .reader-nav-next {
    right: 0.75rem;
  }

  .reader-nav-button:hover,
  .reader-nav-button:focus-visible {
    border-color: var(--color-border-accent-strong);
    color: var(--color-text-accent-bright);
    box-shadow: var(--shadow-glow-accent);
    outline: none;
  }

  @media (min-width: 640px) {
    .reader-nav-button {
      display: flex;
    }
  }

  .pdf-page {
    position: relative;
    background: #1a1c22;
    box-shadow: 0 0 24px rgba(0, 0, 0, 0.5);
  }

  :global(.pdf-canvas) {
    display: block;
  }

  /* pdf.js text layer: transparent, selectable text positioned over the canvas. */
  :global(.pdf-text-layer) {
    position: absolute;
    inset: 0;
    overflow: hidden;
    line-height: 1;
    text-size-adjust: none;
    forced-color-adjust: none;
    transform-origin: 0 0;
    z-index: 2;
  }

  :global(.pdf-text-layer span),
  :global(.pdf-text-layer br) {
    position: absolute;
    white-space: pre;
    color: transparent;
    cursor: text;
    transform-origin: 0% 0%;
  }

  :global(.pdf-text-layer ::selection) {
    background: rgba(242, 194, 106, 0.35);
  }

  /* Transparent clickable link overlay (internal page jumps + external URLs). */
  :global(.pdf-link-layer) {
    position: absolute;
    inset: 0;
    z-index: 3;
  }

  :global(.pdf-link-layer .pdf-link) {
    position: absolute;
    display: block;
    border-radius: 2px;
  }

  :global(.pdf-link-layer .pdf-link:hover) {
    background: rgba(242, 194, 106, 0.18);
    box-shadow: 0 0 0 1px rgba(242, 194, 106, 0.4);
  }

  .pdf-message {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    gap: 0.6rem;
    color: var(--color-text-secondary);
    text-align: center;
    pointer-events: none;
  }

  .pdf-search {
    position: absolute;
    top: max(3.25rem, calc(env(safe-area-inset-top) + 3rem));
    right: 0.75rem;
    z-index: 30;
    display: flex;
    align-items: center;
    gap: 0.4rem;
    max-width: calc(100vw - 1.5rem);
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-md);
    background: var(--color-overlay-heavy);
    padding: 0.4rem 0.5rem;
    backdrop-filter: blur(var(--glass-blur-md));
    box-shadow: var(--shadow-glow-accent);
  }

  .pdf-search-input {
    width: min(14rem, 40vw);
    border: none;
    background: transparent;
    color: var(--color-text-primary);
    font-size: 0.82rem;
    outline: none;
  }

  .pdf-search-count {
    min-width: 3.5ch;
    text-align: center;
    font-family: var(--font-mono, monospace);
    font-size: 0.62rem;
    color: var(--color-text-muted);
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

  .reader-mode-button:hover,
  .reader-mode-button:focus-visible {
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
