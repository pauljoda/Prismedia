<script module lang="ts">
  type ChangelogBlock =
    | { type: "h2"; title: string; date?: string }
    | { type: "h3"; title: string }
    | { type: "p"; text: string }
    | { type: "ul"; items: Array<{ text: string; level: 0 | 1 }> };

  function flushParagraph(blocks: ChangelogBlock[], paragraph: string[]): ChangelogBlock[] {
    if (paragraph.length === 0) return blocks;
    return [...blocks, { type: "p", text: paragraph.join(" ") }];
  }

  function flushList(
    blocks: ChangelogBlock[],
    items: Array<{ text: string; level: 0 | 1 }>,
  ): ChangelogBlock[] {
    if (items.length === 0) return blocks;
    return [...blocks, { type: "ul", items }];
  }

  export function parseChangelog(raw: string): ChangelogBlock[] {
    const lines = raw.replace(/\r\n/g, "\n").split("\n");
    let blocks: ChangelogBlock[] = [];
    let paragraph: string[] = [];
    let listItems: Array<{ text: string; level: 0 | 1 }> = [];

    const flushAll = () => {
      blocks = flushParagraph(blocks, paragraph);
      paragraph = [];
      blocks = flushList(blocks, listItems);
      listItems = [];
    };

    for (const line of lines) {
      if (line.trim() === "") {
        flushAll();
        continue;
      }

      // Skip the document title because the dialog already owns that chrome.
      if (/^#\s+/.test(line)) {
        flushAll();
        continue;
      }

      // H2 with optional date tag like `## [Unreleased]` or `## [0.19.0] - 2026-04-01`
      const h2 = line.match(/^##\s+(.+?)(?:\s+-\s+(.+))?$/);
      if (h2) {
        flushAll();
        blocks.push({
          type: "h2",
          title: h2[1].replace(/^\[|\]$/g, ""),
          date: h2[2],
        });
        continue;
      }

      const h3 = line.match(/^###\s+(.+)$/);
      if (h3) {
        flushAll();
        blocks.push({ type: "h3", title: h3[1] });
        continue;
      }

      // List items — `  - foo` is level 1, `- foo` is level 0.
      const li = line.match(/^(\s*)[-*]\s+(.+)$/);
      if (li) {
        blocks = flushParagraph(blocks, paragraph);
        paragraph = [];
        const indent = li[1].length;
        const level: 0 | 1 = indent >= 2 ? 1 : 0;
        listItems.push({ text: li[2], level });
        continue;
      }

      // Any other non-blank line is paragraph content.
      blocks = flushList(blocks, listItems);
      listItems = [];
      paragraph.push(line.trim());
    }

    flushAll();
    return blocks;
  }
</script>

<script lang="ts">
  import type { Snippet } from "svelte";
  import { RefreshCw, X } from "@lucide/svelte";
  import { fetchReleaseUpdateStatus, type ReleaseUpdateStatus } from "$lib/version";

  const communityLinks = [
    {
      label: "GitHub",
      href: "https://github.com/pauljoda/Prismedia",
      icon: "/icons/github.svg",
    },
    {
      label: "Reddit",
      href: "https://www.reddit.com/r/Prismedia/",
      icon: "/icons/reddit.svg",
    },
  ];

  interface Props {
    version: string;
    children: Snippet;
  }

  let { version, children }: Props = $props();

  let open = $state(false);
  let content = $state<string | null>(null);
  let releaseStatus = $state<ReleaseUpdateStatus | null>(null);
  let loading = $state(false);
  let checkingRelease = $state(false);
  let dialogRef: HTMLDialogElement | null = $state(null);

  const blocks = $derived(content ? parseChangelog(content) : []);
  // Dev builds advertise updates by digest, so latestVersion is null while latestUrl is present.
  const updateAvailable = $derived(
    releaseStatus?.updateAvailable === true && !!releaseStatus.latestUrl,
  );
  const updateLabel = $derived(
    releaseStatus?.latestVersion ? `v${releaseStatus.latestVersion}` : "New build",
  );
  const releaseStatusLabel = $derived.by(() => {
    if (checkingRelease) return "Checking registry";
    if (updateAvailable) return `${updateLabel} available`;
    if (releaseStatus?.status === "current" || releaseStatus?.status === "development") return "Up to date";
    if (releaseStatus?.status === "unknown") return "Update status unavailable";
    return "Update status pending";
  });

  async function loadChangelog() {
    if (content || loading) return;
    loading = true;
    try {
      const res = await fetch("/api/changelog");
      content = res.ok ? await res.text() : "Failed to load changelog.";
    } catch {
      content = "Failed to load changelog.";
    } finally {
      loading = false;
    }
  }

  async function loadReleaseStatus(force = false) {
    if (checkingRelease) return;
    checkingRelease = true;
    try {
      releaseStatus = await fetchReleaseUpdateStatus(fetch, { force });
    } finally {
      checkingRelease = false;
    }
  }

  $effect(() => {
    if (!dialogRef) return;
    if (open) dialogRef.showModal();
    else if (dialogRef.open) dialogRef.close();
  });

  function handleOpen() {
    open = true;
    void loadChangelog();
    void loadReleaseStatus();
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === dialogRef) open = false;
  }

  function handleTriggerKeydown(event: KeyboardEvent) {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    handleOpen();
  }

  function renderInline(text: string): Array<
    | { kind: "text"; value: string }
    | { kind: "strong"; value: string }
    | { kind: "code"; value: string }
    | { kind: "link"; label: string; href: string }
  > {
    const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)]+\))/g);
    return parts.filter(Boolean).map((part) => {
      const bold = part.match(/^\*\*(.+)\*\*$/);
      if (bold) return { kind: "strong" as const, value: bold[1] };
      const code = part.match(/^`(.+)`$/);
      if (code) return { kind: "code" as const, value: code[1] };
      const link = part.match(/^\[([^\]]+)\]\(([^)]+)\)$/);
      if (link) return { kind: "link" as const, label: link[1], href: link[2] };
      return { kind: "text" as const, value: part };
    });
  }
</script>

<div
  role="button"
  tabindex="0"
  onclick={handleOpen}
  onkeydown={handleTriggerKeydown}
  class="cursor-pointer rounded-xs outline-none focus-visible:ring-2 focus-visible:ring-accent-500/35"
>
  {@render children()}
</div>

<dialog
  bind:this={dialogRef}
  onclick={handleBackdropClick}
  onclose={() => (open = false)}
  aria-label="Prismedia changelog"
  class="changelog-dialog fixed inset-0 m-auto h-[min(86dvh,44rem)] w-[min(94vw,56rem)] flex-col overflow-hidden border border-border-default p-0 text-text-primary open:flex"
>
  <header class="changelog-header relative border-b border-border-subtle px-4 py-3.5 sm:px-5">
    <div class="flex items-start justify-between gap-4">
      <div class="min-w-0 space-y-3">
        <div>
          <p class="text-kicker mb-1 text-text-accent">Release console</p>
          <h2 class="font-heading text-[1.35rem] font-bold uppercase leading-none tracking-[0.18em] text-text-primary sm:text-[1.55rem]">
            Changelog
          </h2>
        </div>
        <div class="flex flex-wrap items-center gap-1.5">
          <span class="status-chip status-chip-strong">Installed v{version}</span>
          <span class="status-chip">{releaseStatusLabel}</span>
        </div>
      </div>

      <button
        type="button"
        onclick={() => (open = false)}
        class="control-button h-9 w-9 shrink-0"
        aria-label="Close changelog"
      >
        <X class="h-4 w-4" />
      </button>
    </div>

    <div class="mt-3 flex flex-wrap items-center gap-1.5">
      {#each communityLinks as link (link.href)}
        <a
          href={link.href}
          target="_blank"
          rel="noopener noreferrer"
          class="link-button"
          aria-label={`Open Prismedia on ${link.label}`}
        >
          <img src={link.icon} alt="" class="h-3.5 w-3.5 opacity-80" />
          {link.label}
        </a>
      {/each}
      <button
        type="button"
        onclick={() => void loadReleaseStatus(true)}
        disabled={checkingRelease}
        class="link-button disabled:cursor-wait disabled:opacity-60"
        aria-label="Check for updates"
        title="Check for updates"
      >
        <RefreshCw class={checkingRelease ? "h-3.5 w-3.5 animate-spin" : "h-3.5 w-3.5"} />
        Refresh
      </button>
    </div>
  </header>

  <div class="changelog-scroll min-h-0 flex-1 overflow-y-auto px-4 py-3.5 sm:px-5">
    {#if updateAvailable}
      <a
        href={releaseStatus?.latestUrl ?? undefined}
        target="_blank"
        rel="noopener noreferrer"
        class="update-banner mb-4 grid gap-1 px-3.5 py-3 text-sm transition hover:border-border-accent-strong hover:shadow-[var(--shadow-glow-accent-strong)] sm:grid-cols-[1fr_auto] sm:items-center"
      >
        <span class="font-heading font-semibold text-text-primary">
          Update available: {updateLabel}
        </span>
        <span class="font-mono text-[0.68rem] uppercase tracking-[0.16em] text-text-accent">Open package</span>
      </a>
    {/if}

    {#if loading}
      <div class="surface-well animate-pulse px-4 py-8 text-center text-xs uppercase tracking-[0.16em] text-text-disabled">
        Loading changelog...
      </div>
    {:else if content}
      <div class="release-stream pb-5">
        {#each blocks as block, index (index)}
          {#if block.type === "h2"}
            <section class="release-marker mt-4 first:mt-0">
              <div class="flex flex-wrap items-baseline justify-between gap-2 border-b border-border-subtle pb-2">
                <h2 class="font-heading text-base font-semibold tracking-[-0.02em] text-text-primary">
                  {block.title}
                </h2>
                {#if block.date}
                  <span class="font-mono text-[0.68rem] uppercase tracking-[0.12em] text-text-disabled">{block.date}</span>
                {/if}
              </div>
            </section>
          {:else if block.type === "h3"}
            <h3 class="mt-3 inline-flex border border-border-accent/35 bg-accent-950/20 px-2 py-1 font-heading text-[0.7rem] font-semibold uppercase tracking-[0.16em] text-text-accent">
              {block.title}
            </h3>
          {:else if block.type === "p"}
            <p class="mt-3 max-w-3xl text-sm leading-relaxed text-text-muted first:mt-0">
              {#each renderInline(block.text) as part, i (i)}
                {#if part.kind === "strong"}
                  <strong class="font-semibold text-text-primary">{part.value}</strong>
                {:else if part.kind === "code"}
                  <code class="inline-code">
                    {part.value}
                  </code>
                {:else if part.kind === "link"}
                  <a
                    href={part.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    class="text-text-accent transition hover:text-text-accent-bright hover:underline"
                  >
                    {part.label}
                  </a>
                {:else}
                  {part.value}
                {/if}
              {/each}
            </p>
          {:else}
            <ul class="changelog-list mt-2 space-y-1.5 pl-5">
              {#each block.items as item, itemIndex (itemIndex)}
                <li
                  class={item.level === 0
                    ? "text-sm leading-relaxed text-text-secondary"
                    : "ml-4 text-[0.8rem] leading-relaxed text-text-muted"}
                >
                  {#each renderInline(item.text) as part, i (i)}
                    {#if part.kind === "strong"}
                      <strong class="font-semibold text-text-primary">{part.value}</strong>
                    {:else if part.kind === "code"}
                      <code class="inline-code">
                        {part.value}
                      </code>
                    {:else if part.kind === "link"}
                      <a
                        href={part.href}
                        target="_blank"
                        rel="noopener noreferrer"
                        class="text-text-accent transition hover:text-text-accent-bright hover:underline"
                      >
                        {part.label}
                      </a>
                    {:else}
                      {part.value}
                    {/if}
                  {/each}
                </li>
              {/each}
            </ul>
          {/if}
        {/each}
      </div>
    {/if}
  </div>
</dialog>

<style>
  .changelog-dialog {
    border-radius: var(--radius-md);
    background:
      radial-gradient(circle at 92% 8%, rgb(255 255 255 / 0.045), transparent 24%),
      linear-gradient(145deg, rgb(17 22 29 / 0.97), rgb(7 8 11 / 0.99));
    box-shadow:
      0 20px 60px rgb(0 0 0 / 0.58),
      inset 0 1px 0 rgb(255 255 255 / 0.055);
    backdrop-filter: blur(22px);
    -webkit-backdrop-filter: blur(22px);
  }

  .changelog-dialog::backdrop {
    background: rgb(7 8 11 / 0.82);
    backdrop-filter: blur(10px);
    -webkit-backdrop-filter: blur(10px);
  }

  .changelog-header {
    background: linear-gradient(180deg, rgb(42 48 56 / 0.36), rgb(11 14 18 / 0.22));
  }

  .status-chip,
  .link-button,
  .control-button,
  .update-banner,
  .inline-code {
    border-radius: var(--radius-xs);
  }

  .status-chip {
    display: inline-flex;
    min-height: 1.55rem;
    align-items: center;
    border: 1px solid var(--color-border-subtle);
    background: rgb(11 14 18 / 0.5);
    padding: 0.2rem 0.5rem;
    font-family: var(--font-mono);
    font-size: 0.62rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--color-text-muted);
  }

  .status-chip-strong {
    border-color: var(--color-border-accent);
    color: var(--color-text-accent);
  }

  .link-button,
  .control-button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.4rem;
    border: 1px solid var(--color-border-subtle);
    background: rgb(11 14 18 / 0.52);
    color: var(--color-text-muted);
    font-family: var(--font-heading);
    font-size: 0.64rem;
    font-weight: 700;
    letter-spacing: 0.12em;
    min-height: 1.85rem;
    padding: 0 0.58rem;
    text-transform: uppercase;
    transition: border-color 180ms var(--ease-mechanical), color 180ms var(--ease-mechanical), box-shadow 180ms var(--ease-mechanical), background 180ms var(--ease-mechanical);
  }

  .link-button:hover,
  .link-button:focus-visible,
  .control-button:hover,
  .control-button:focus-visible {
    border-color: var(--color-border-accent);
    background: var(--color-overlay-glass-accent);
    color: var(--color-text-accent);
    box-shadow: 0 0 16px rgb(242 194 106 / 0.08);
    outline: none;
  }

  .changelog-scroll {
    scrollbar-width: none;
  }

  .changelog-scroll::-webkit-scrollbar {
    display: none;
  }

  .update-banner {
    border: 1px solid var(--color-border-accent);
    background:
      linear-gradient(135deg, rgb(122 94 32 / 0.24), rgb(17 22 29 / 0.84)),
      var(--color-overlay-glass);
    box-shadow: var(--shadow-glow-accent);
  }

  .release-stream {
    position: relative;
  }

  .release-marker {
    position: relative;
  }

  .changelog-list {
    list-style: disc;
  }

  .changelog-list li::marker {
    color: var(--color-accent-500);
  }

  .inline-code {
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    color: var(--color-text-secondary);
    font-family: var(--font-mono);
    font-size: 0.72rem;
    padding: 0.05rem 0.3rem;
    word-break: break-word;
  }
</style>
