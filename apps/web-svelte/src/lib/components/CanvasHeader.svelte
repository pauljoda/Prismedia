<script lang="ts">
  import { Ellipsis, Search, Settings } from "@lucide/svelte";
  import { page } from "$app/state";
  import { onMount } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { apiPath } from "$lib/api/orval-fetch";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { useSearch } from "$lib/stores/search.svelte";
  import { getCanvasHeaderBreadcrumbItems } from "./canvas-header-breadcrumbs";
  import LogoMark from "./LogoMark.svelte";
  import OverflowTicker from "./OverflowTicker.svelte";

  const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

  const SEGMENT_LABELS: Record<string, string> = {
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

  let appleMod = $state(false);
  let breadcrumbMenuOpen = $state(false);
  let backendRuntime = $state<"dotnet" | "unknown" | "offline">("unknown");
  $effect(() => {
    appleMod = typeof navigator !== "undefined" && /Mac|iPhone|iPad/i.test(navigator.userAgent);
  });

  onMount(() => {
    let cancelled = false;
    const checkBackend = async () => {
      try {
        const response = await fetch(apiPath("/health"));
        const health = (await response.json()) as { runtime?: string };
        if (!cancelled) backendRuntime = health.runtime === "dotnet" ? "dotnet" : "unknown";
      } catch {
        if (!cancelled) backendRuntime = "offline";
      }
    };

    void checkBackend();
    return () => {
      cancelled = true;
    };
  });

  const searchShortcutKbd = $derived(appleMod ? "⌘K" : "Ctrl+K");

  function closeBreadcrumbMenu() {
    breadcrumbMenuOpen = false;
  }

  function resolveHref(href: string) {
    return href;
  }
</script>

<header class="flex h-14 shrink-0 items-center justify-between gap-3 border-b border-border-subtle px-5">
  <div class="flex min-w-0 flex-1 items-center gap-3">
    <a
      href="/"
      aria-label="Dashboard"
      class={cn(
        "md:hidden flex h-8 w-8 shrink-0 items-center justify-center",
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
              <span class="relative shrink-0">
                <button
                  type="button"
                  class={cn(
                    "flex h-6 w-6 items-center justify-center border border-border-subtle bg-glass-1 text-text-muted backdrop-blur-md",
                    "hover:text-text-primary hover:border-border-accent focus-visible:border-border-accent-strong focus-visible:shadow-focus-accent",
                    "transition-colors duration-fast outline-none",
                  )}
                  aria-label={item.label}
                  aria-haspopup="menu"
                  aria-expanded={breadcrumbMenuOpen}
                  onclick={() => (breadcrumbMenuOpen = !breadcrumbMenuOpen)}
                >
                  <Ellipsis class="h-3.5 w-3.5" />
                </button>
                {#if breadcrumbMenuOpen}
                  <div
                    class="absolute left-0 top-full z-[120] mt-2 w-[min(14rem,calc(100vw-2rem))] border border-border-default bg-glass-2 p-1 shadow-glass backdrop-blur-xl"
                    role="menu"
                  >
                    {#each item.items as crumb (crumb.href)}
                      <a
                        href={resolveHref(crumb.href)}
                        role="menuitem"
                        class="block min-w-0 truncate px-3 py-2 text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary focus-visible:bg-surface-2 focus-visible:text-text-primary outline-none"
                        onclick={closeBreadcrumbMenu}
                      >
                        {crumb.label}
                      </a>
                    {/each}
                  </div>
                {/if}
              </span>
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
              <span class="relative shrink-0">
                <button
                  type="button"
                  class={cn(
                    "flex h-7 w-7 items-center justify-center border border-border-subtle bg-glass-1 text-text-muted backdrop-blur-md",
                    "hover:text-text-primary hover:border-border-accent focus-visible:border-border-accent-strong focus-visible:shadow-focus-accent",
                    "transition-colors duration-fast outline-none",
                  )}
                  aria-label={item.label}
                  aria-haspopup="menu"
                  aria-expanded={breadcrumbMenuOpen}
                  onclick={() => (breadcrumbMenuOpen = !breadcrumbMenuOpen)}
                >
                  <Ellipsis class="h-4 w-4" />
                </button>
                {#if breadcrumbMenuOpen}
                  <div
                    class="absolute left-0 top-full z-[120] mt-2 w-[min(14rem,calc(100vw-2rem))] border border-border-default bg-glass-2 p-1 shadow-glass backdrop-blur-xl"
                    role="menu"
                  >
                    {#each item.items as crumb (crumb.href)}
                      <a
                        href={resolveHref(crumb.href)}
                        role="menuitem"
                        class="block min-w-0 truncate px-3 py-2 text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary focus-visible:bg-surface-2 focus-visible:text-text-primary outline-none"
                        onclick={closeBreadcrumbMenu}
                      >
                        {crumb.label}
                      </a>
                    {/each}
                  </div>
                {/if}
              </span>
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
    <div
      class={cn(
        "hidden items-center gap-1.5 border border-border-subtle bg-surface-1 px-2 py-1 text-[0.64rem] font-mono uppercase tracking-wider text-text-disabled lg:flex",
        backendRuntime === "dotnet" && "border-border-accent/30 text-text-accent",
        backendRuntime === "offline" && "border-status-error/30 text-status-error-text",
      )}
      title={backendRuntime === "dotnet" ? "API served by .NET" : "Checking .NET API"}
    >
      <span
        class={cn(
          "h-1.5 w-1.5 bg-text-disabled",
          backendRuntime === "dotnet" && "bg-accent-500 shadow-[0_0_8px_rgba(196,154,90,0.75)]",
          backendRuntime === "offline" && "bg-status-error",
        )}
      ></span>
      API {backendRuntime}
    </div>
    <button
      type="button"
      onclick={() => search.openPalette()}
      class={cn(
        "group flex items-center justify-center sm:justify-between w-8 sm:w-64 px-0 sm:px-3 py-1.5",
        "bg-transparent sm:bg-surface-1 border border-transparent sm:border-border-default sm:border-t-[rgba(0,0,0,0.6)]",
        "sm:shadow-[inset_0_2px_6px_rgba(0,0,0,0.5)]",
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
      <kbd class="hidden sm:inline-flex h-5 items-center border border-border-subtle px-1.5 text-[0.65rem] font-mono text-text-disabled bg-surface-2 shadow-[inset_0_1px_0_rgba(255,255,255,0.04),0_1px_2px_rgba(0,0,0,0.2)]">
        {searchShortcutKbd}
      </kbd>
    </button>
    <a
      href="/settings"
      class="flex h-8 w-8 items-center justify-center text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors duration-fast"
    >
      <Settings class="h-4 w-4" />
    </a>
  </div>
</header>
