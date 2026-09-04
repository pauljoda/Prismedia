<script lang="ts">
  import { Ellipsis, Search, Settings } from "@lucide/svelte";
  import { page } from "$app/state";
  import { cn, DropdownMenu } from "@prismedia/ui-svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { useSession } from "$lib/stores/session.svelte";
  import { useSearch } from "$lib/stores/search.svelte";
  import { getCanvasHeaderBreadcrumbItems } from "./canvas-header-breadcrumbs";
  import LogoMark from "./LogoMark.svelte";
  import OverflowTicker from "./OverflowTicker.svelte";

  const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  const SEGMENT_LABELS: Record<string, string> = {
    movies: "Movies",
    videos: "Videos",
    people: "People",
  };

  function segmentLabel(seg: string): string {
    const decoded = decodeURIComponent(seg);
    const mapped = SEGMENT_LABELS[decoded.toLowerCase()];
    if (mapped) return mapped;
    return decoded.charAt(0).toUpperCase() + decoded.slice(1);
  }

  const chrome = useAppChrome();

  const pathCrumbs = $derived.by(() => {
    const segments = page.url.pathname.split("/").filter(Boolean);
    return segments
      .filter((seg) => !UUID_RE.test(seg))
      .map((seg, i, arr) => ({
        label: segmentLabel(seg),
        href: "/" + segments.slice(0, i + 1).join("/"),
        isLast: i === arr.length - 1,
      }));
  });
  const crumbs = $derived(
    chrome.breadcrumbs.length > 0
      ? chrome.breadcrumbs.map((crumb, i) => ({
          label: crumb.label,
          href: crumb.href ?? "#",
          isLast: i === chrome.breadcrumbs.length - 1 || !crumb.href,
        }))
      : pathCrumbs,
  );
  const desktopCrumbItems = $derived(getCanvasHeaderBreadcrumbItems(crumbs, 3));
  const mobileCrumbItems = $derived(getCanvasHeaderBreadcrumbItems(crumbs, 1));

  const search = useSearch();
  const session = useSession();

  let appleMod = $state(false);
  $effect(() => {
    appleMod = typeof navigator !== "undefined" && /Mac|iPhone|iPad/i.test(navigator.userAgent);
  });

  const searchShortcutKbd = $derived(appleMod ? "⌘K" : "Ctrl+K");

  function resolveHref(href: string) {
    return href;
  }
</script>

{#snippet breadcrumbMenu(label: string, items: { label: string; href: string }[])}
  <DropdownMenu.Root>
    <DropdownMenu.Trigger
      class="flex size-7 shrink-0 items-center justify-center rounded-xs border border-border-subtle bg-glass-1 text-text-muted transition-colors duration-fast hover:text-text-primary focus-visible:outline-none focus-visible:shadow-focus-accent"
      aria-label={label}
    >
      <Ellipsis class="size-4" />
    </DropdownMenu.Trigger>
    <DropdownMenu.Content align="start" class="w-56">
      <DropdownMenu.Group>
        {#each items as crumb (crumb.href)}
          <DropdownMenu.Item>
            {#snippet child({ props })}
              <a {...props} href={resolveHref(crumb.href)}>{crumb.label}</a>
            {/snippet}
          </DropdownMenu.Item>
        {/each}
      </DropdownMenu.Group>
    </DropdownMenu.Content>
  </DropdownMenu.Root>
{/snippet}

<header
  class="app-glass sticky top-0 z-40 flex shrink-0 items-center justify-between gap-3 border-b px-5"
  style:height="var(--prismedia-canvas-header-height, 3.5rem)"
>
  <div class="flex min-w-0 flex-1 items-center gap-3">
    <a
      href="/"
      aria-label="Dashboard"
      class={cn(
        "md:hidden flex h-8 w-8 shrink-0 items-center justify-center rounded-sm",
        "text-text-muted hover:text-text-primary hover:bg-surface-2",
        "transition-colors duration-fast",
      )}
    >
      <LogoMark size={24} alt="" />
    </a>
    <nav class="hidden min-w-0 flex-1 items-center gap-1.5 overflow-hidden text-mono-sm sm:flex" aria-label="Breadcrumb">
      {#if crumbs.length === 0}
        <span class="truncate text-text-muted">Dashboard</span>
      {:else}
        {#each desktopCrumbItems as item, i (`desktop-${item.kind}-${i}`)}
          {#if i > 0 && desktopCrumbItems[i - 1]?.kind !== "overflow"}
            <span class="shrink-0 text-text-disabled">/</span>
          {/if}
          <span class={cn("flex min-w-0 items-center", item.kind === "crumb" && item.isLast && "flex-1")}>
            {#if item.kind === "overflow"}
              {@render breadcrumbMenu(item.label, item.items)}
            {:else if item.isLast}
              <OverflowTicker text={item.label} class="text-text-primary" />
            {:else}
              <a
                href={resolveHref(item.href)}
                class="shrink-0 text-text-muted hover:text-text-primary transition-colors duration-fast"
              >
                {item.label}
              </a>
            {/if}
          </span>
          {#if item.kind === "overflow"}
            <span class="shrink-0 text-text-disabled">/</span>
          {/if}
        {/each}
      {/if}
    </nav>
    <nav class="flex min-w-0 flex-1 items-center gap-1 overflow-hidden text-mono-sm sm:hidden" aria-label="Breadcrumb">
      {#if mobileCrumbItems.length === 0}
        <span class="truncate text-text-muted">Dashboard</span>
      {:else}
        {#each mobileCrumbItems as item, i (`${item.kind}-${i}`)}
          {#if i > 0 && mobileCrumbItems[i - 1]?.kind !== "overflow"}
            <span class="shrink-0 text-text-disabled">/</span>
          {/if}
          <span class={cn("flex min-w-0 items-center", item.kind === "crumb" && item.isLast && "flex-1")}>
            {#if item.kind === "overflow"}
              {@render breadcrumbMenu(item.label, item.items)}
            {:else if item.isLast}
              <OverflowTicker text={item.label} class="text-text-primary" />
            {:else}
              <a
                href={resolveHref(item.href)}
                class="shrink-0 text-text-muted hover:text-text-primary transition-colors duration-fast"
              >
                {item.label}
              </a>
            {/if}
          </span>
          {#if item.kind === "overflow"}
            <span class="shrink-0 text-text-disabled">/</span>
          {/if}
        {/each}
      {/if}
    </nav>
  </div>

  <div class="flex items-center gap-2">
    <button
      type="button"
      onclick={() => search.openPalette()}
      class={cn(
        "group flex items-center justify-center sm:justify-between w-8 sm:w-64 px-0 sm:px-3 py-1.5 rounded-sm",
        "bg-transparent sm:bg-surface-1 border border-transparent sm:border-border-default",
        "sm:shadow-[var(--shadow-well)]",
        "text-text-muted hover:text-text-primary sm:hover:border-border-accent focus-visible:border-border-accent-strong focus-visible:shadow-focus-accent",
        "transition-all duration-fast cursor-text select-none outline-none",
      )}
      aria-label="Open search"
      title={`Search (${searchShortcutKbd})`}
    >
      <div class="flex items-center gap-2.5">
        <Search class="h-4 w-4 sm:h-3.5 sm:w-3.5 text-text-muted sm:text-text-disabled group-hover:text-text-primary sm:group-hover:text-text-muted transition-colors duration-fast" />
        <span class="hidden sm:inline text-[0.8rem]">Search...</span>
      </div>
      <kbd class="hidden sm:inline-flex h-5 items-center rounded-xs border border-border-subtle px-1.5 text-[0.65rem] font-mono text-text-disabled bg-surface-2 shadow-[inset_0_1px_0_rgba(255,255,255,0.04),0_1px_2px_rgba(0,0,0,0.2)]">
        {searchShortcutKbd}
      </kbd>
    </button>
    {#if session.canManageServer}
      <a
        href="/settings"
        class="flex h-8 w-8 items-center justify-center rounded-sm text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors duration-fast"
      >
        <Settings class="h-4 w-4" />
      </a>
    {/if}
  </div>

</header>
