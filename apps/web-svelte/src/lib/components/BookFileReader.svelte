<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { AlertTriangle, BookOpen, Rows3, Minus, Plus } from "@lucide/svelte";
  import { apiAssetUrl as toApiUrl } from "$lib/api/orval-fetch";
  import ReaderShell from "$lib/components/reader/ReaderShell.svelte";

  type ReaderFlow = "paginated" | "scrolled";

  interface ReaderLocation {
    cfi: string | null;
    fraction: number;
    label: string | null;
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

  let stageEl = $state<HTMLDivElement>();
  // The foliate `<foliate-view>` custom element has no published types; treat as a loose handle.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let view: any = null;
  let ready = $state(false);
  let errorMessage = $state<string | null>(null);
  let flow = $state<ReaderFlow>(untrack(() => initialFlow));
  let fontPercent = $state(100);
  let counterLabel = $state("");

  function contentStyles(percent: number): string {
    return `
      html { color-scheme: dark; font-size: ${percent}%; }
      html, body { background: transparent !important; color: #e7e7ea; }
      a, a:link, a:visited { color: #f2c26a; }
      img { background: transparent; }
    `;
  }

  function applyFlow(next: ReaderFlow) {
    if (!view?.renderer) return;
    view.renderer.setAttribute("flow", next === "scrolled" ? "scrolled" : "paginated");
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

  function handleRelocate(event: Event) {
    const detail = (event as CustomEvent).detail ?? {};
    const fraction = typeof detail.fraction === "number" ? detail.fraction : 0;
    const cfi = typeof detail.cfi === "string" ? detail.cfi : null;
    const label = detail.tocItem?.label?.trim() || null;
    counterLabel = `${Math.round(fraction * 100)}%${label ? ` · ${label}` : ""}`;
    onLocationChange?.({ cfi, fraction, label });
  }

  function goPrev() {
    view?.goLeft?.();
  }

  function goNext() {
    view?.goRight?.();
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

        await view.open(file);
        if (disposed) return;

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
    <div class="flex items-center gap-1">
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
  {/snippet}

  <div class="book-reader-stage" bind:this={stageEl}>
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
</style>
