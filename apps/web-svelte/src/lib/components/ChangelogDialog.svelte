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
  const updateAvailable = $derived(
    releaseStatus?.updateAvailable === true && !!releaseStatus.latestVersion && !!releaseStatus.latestUrl,
  );

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

<!-- svelte-ignore a11y_click_events_have_key_events -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<span onclick={handleOpen} class="cursor-pointer">
  {@render children()}
</span>

<dialog
  bind:this={dialogRef}
  onclick={handleBackdropClick}
  onclose={() => (open = false)}
  class="fixed inset-0 m-auto h-[85vh] w-[90vw] max-w-3xl flex-col border border-border-subtle bg-surface-1 p-0 text-text-primary backdrop:bg-black/70 open:flex sm:h-[80vh]"
>
  <div class="flex flex-wrap items-center justify-between gap-3 border-b border-border-subtle px-5 py-3.5">
    <div class="min-w-0">
      <h2 class="font-heading text-sm font-bold uppercase tracking-wider text-text-accent">
        Changelog &middot; v{version}
      </h2>
      <div class="mt-2 flex flex-wrap gap-2">
        {#each communityLinks as link (link.href)}
          <a
            href={link.href}
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex h-8 items-center gap-2 border border-border-subtle bg-surface-2/80 px-2.5 font-heading text-[10px] font-bold uppercase tracking-wider text-text-muted transition hover:border-border-accent hover:text-text-accent hover:shadow-[0_0_18px_rgba(196,154,90,0.18)] focus-visible:border-border-accent focus-visible:text-text-accent focus-visible:outline-none focus-visible:shadow-[0_0_18px_rgba(196,154,90,0.22)]"
            aria-label={`Open Prismedia on ${link.label}`}
          >
            <img src={link.icon} alt="" class="h-3.5 w-3.5" />
            {link.label}
          </a>
        {/each}
        <button
          type="button"
          onclick={() => void loadReleaseStatus(true)}
          disabled={checkingRelease}
          class="inline-flex h-8 w-8 items-center justify-center border border-border-subtle bg-surface-2/80 text-text-muted transition hover:border-border-accent hover:text-text-accent hover:shadow-[0_0_18px_rgba(196,154,90,0.18)] focus-visible:border-border-accent focus-visible:text-text-accent focus-visible:outline-none focus-visible:shadow-[0_0_18px_rgba(196,154,90,0.22)] disabled:cursor-wait disabled:opacity-60"
          aria-label="Check for updates"
          title="Check for updates"
        >
          <RefreshCw class={checkingRelease ? "h-3.5 w-3.5 animate-spin" : "h-3.5 w-3.5"} />
        </button>
      </div>
    </div>
    <button
      type="button"
      onclick={() => (open = false)}
      class="flex h-7 w-7 shrink-0 items-center justify-center text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      aria-label="Close"
    >
      <X class="h-4 w-4" />
    </button>
  </div>
  <div class="scrollbar-hidden flex-1 overflow-y-auto px-5 py-4">
    {#if updateAvailable}
      <a
        href={releaseStatus?.latestUrl ?? undefined}
        target="_blank"
        rel="noopener noreferrer"
        class="mb-4 flex items-center justify-between gap-3 border border-border-accent bg-glass-2 px-3 py-2 text-xs text-text-primary shadow-[0_0_22px_rgba(196,154,90,0.14)] transition hover:text-text-accent"
      >
        <span class="font-heading font-bold uppercase tracking-wider">
          Update available: v{releaseStatus?.latestVersion}
        </span>
        <span class="font-mono text-[10px] text-text-disabled">GitHub release</span>
      </a>
    {/if}
    {#if loading}
      <p class="animate-pulse text-xs text-text-disabled">Loading changelog...</p>
    {:else if content}
      <div class="pb-4">
        {#each blocks as block, index (index)}
          {#if block.type === "h2"}
            <div class="mt-6 border-b border-border-subtle pb-2 first:mt-0">
              <h2 class="font-heading text-sm font-bold uppercase tracking-wider text-text-primary">
                {block.title}
                {#if block.date}
                  <span class="ml-2 font-mono text-[10px] text-text-disabled">{block.date}</span>
                {/if}
              </h2>
            </div>
          {:else if block.type === "h3"}
            <h3 class="mt-4 font-heading text-xs font-bold uppercase tracking-wider text-text-accent">
              {block.title}
            </h3>
          {:else if block.type === "p"}
            <p class="mt-3 text-xs leading-relaxed text-text-muted first:mt-0">
              {#each renderInline(block.text) as part, i (i)}
                {#if part.kind === "strong"}
                  <strong class="text-text-primary">{part.value}</strong>
                {:else if part.kind === "code"}
                  <code class="bg-surface-3/60 px-1 py-0.5 font-mono text-[10px] break-all">
                    {part.value}
                  </code>
                {:else if part.kind === "link"}
                  <a
                    href={part.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    class="text-text-accent hover:underline"
                  >
                    {part.label}
                  </a>
                {:else}
                  {part.value}
                {/if}
              {/each}
            </p>
          {:else}
            <ul class="mt-2 space-y-1">
              {#each block.items as item, itemIndex (itemIndex)}
                <li
                  class={item.level === 0
                    ? "ml-4 list-disc text-xs leading-relaxed text-text-muted"
                    : "ml-8 list-disc text-[11px] leading-relaxed text-text-muted/75"}
                >
                  {#each renderInline(item.text) as part, i (i)}
                    {#if part.kind === "strong"}
                      <strong class="text-text-primary">{part.value}</strong>
                    {:else if part.kind === "code"}
                      <code class="bg-surface-3/60 px-1 py-0.5 font-mono text-[10px] break-all">
                        {part.value}
                      </code>
                    {:else if part.kind === "link"}
                      <a
                        href={part.href}
                        target="_blank"
                        rel="noopener noreferrer"
                        class="text-text-accent hover:underline"
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
