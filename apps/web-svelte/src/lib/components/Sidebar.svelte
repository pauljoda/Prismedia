<script lang="ts">
  import { BookOpen, PanelLeftClose, PanelLeftOpen, Wrench } from "@lucide/svelte";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { cn } from "@prismedia/ui-svelte";
  import { appShellSections } from "./app-shell-sections";
  import { appShellNavIconMap } from "./app-shell-nav-icon-map";
  import LogoMark from "./LogoMark.svelte";
  import ChangelogDialog from "./ChangelogDialog.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { APP_VERSION, fetchReleaseUpdateStatus, type ReleaseUpdateStatus } from "$lib/version";
  import { shouldExposeDevRoutes } from "$lib/dev-routes";

  interface Props {
    collapsed: boolean;
    onToggle: () => void;
  }

  let { collapsed, onToggle }: Props = $props();
  let hovered = $state(false);
  let releaseStatus = $state<ReleaseUpdateStatus | null>(null);
  const nsfw = useNsfw();
  const isExpanded = $derived(!collapsed || hovered);
  const brandLogoSize = $derived(isExpanded ? 40 : 34);
  const brandLogoNsfw = $derived(nsfw.mode === "show");
  const updateAvailable = $derived(releaseStatus?.updateAvailable === true);
  const pathname = $derived(page.url.pathname);
  const showDevTools = shouldExposeDevRoutes();
  const docsHref = "https://pauljoda.github.io/Prismedia/docs/users/quick-start";

  function isActive(href: string): boolean {
    return pathname === href || (href !== "/" && pathname.startsWith(href + "/"));
  }

  $effect(() => {
    void fetchReleaseUpdateStatus().then((status) => {
      releaseStatus = status;
    });
  });
</script>

<aside
  onmouseenter={() => (hovered = true)}
  onmouseleave={() => (hovered = false)}
  class={cn(
    "fixed left-0 top-0 z-[1200] flex h-dvh flex-col bg-surface-1 border-r border-border-subtle transition-[width] duration-moderate overflow-hidden",
    isExpanded ? "w-60" : "w-14",
  )}
  style:transition-timing-function="var(--ease-mechanical)"
>
  <!-- Logo + collapse toggle -->
  <div
    class={cn(
      "flex h-16 items-center justify-between border-b border-border-subtle shrink-0 transition-[padding] duration-moderate",
      isExpanded ? "px-3" : "px-2",
    )}
  >
    <a
      href={resolve("/")}
      aria-label="Dashboard"
      class={cn(
        "flex h-full min-w-0 shrink-0 items-center transition-[gap] duration-moderate",
        isExpanded ? "flex-1 gap-2" : "w-full justify-center gap-0",
      )}
    >
      <div
        class={cn(
          "brand-mark-backdrop flex shrink-0 items-center justify-center transition-[width,height] duration-moderate",
          isExpanded ? "h-11 w-11" : "h-9 w-9",
          brandLogoNsfw && "brand-mark-backdrop-nsfw",
        )}
      >
        <LogoMark
          size={brandLogoSize}
          class="relative z-10"
        />
      </div>
      <div
        class={cn(
          "overflow-hidden transition-[max-width,opacity] duration-moderate",
          isExpanded ? "max-w-[160px] opacity-100" : "max-w-0 opacity-0",
        )}
      >
        <span class="block font-heading font-bold tracking-[0.18em] text-text-primary text-lg leading-none">
          PRISMEDIA
        </span>
      </div>
    </a>
    <div
      class={cn(
        "shrink-0 overflow-hidden transition-[max-width,opacity] duration-moderate flex items-center justify-end",
        isExpanded ? "max-w-[32px] opacity-100" : "max-w-0 opacity-0",
      )}
    >
      <button
        onclick={onToggle}
        class="flex h-8 w-8 items-center justify-center text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors duration-fast"
        aria-label={collapsed ? "Pin sidebar open" : "Collapse sidebar"}
      >
        {#if collapsed}
          <PanelLeftOpen class="h-4 w-4" />
        {:else}
          <PanelLeftClose class="h-4 w-4" />
        {/if}
      </button>
    </div>
  </div>

  <!-- Navigation sections -->
  <nav class="flex-1 overflow-y-auto overflow-x-hidden py-3 scrollbar-hidden">
    {#each appShellSections as section (section.id)}
      <div class="mb-4">
        <div
          class={cn(
            "px-4 pb-1.5 text-kicker whitespace-nowrap transition-[max-height,opacity] duration-moderate overflow-hidden",
            isExpanded ? "max-h-8 opacity-100" : "max-h-0 opacity-0",
          )}
        >
          {section.kicker}
        </div>
        <div
          class={cn(
            "mx-auto mb-1 w-6 separator transition-[max-height,opacity] duration-moderate overflow-hidden",
            !isExpanded ? "max-h-2 opacity-100" : "max-h-0 opacity-0",
          )}
        ></div>
        <ul class="space-y-0.5 px-2">
          {#each section.items as item (item.href)}
            {@const Icon = appShellNavIconMap[item.icon]}
            {@const active = isActive(item.href)}
            <li>
              <a
                href={resolve(item.href as "/")}
                class={cn(
                  "group relative flex items-center px-2.5 py-2 text-sm transition-colors duration-fast whitespace-nowrap",
                  active
                    ? "bg-accent-950 text-glow-accent"
                    : "text-text-muted hover:text-text-primary hover:bg-surface-2",
                )}
                title={!isExpanded ? item.label : undefined}
              >
                {#if active}
                  <span class="absolute left-0 top-1.5 bottom-1.5 w-[3px] bg-accent-500 shadow-[var(--shadow-glow-accent)]"></span>
                {/if}
                <div class="w-5 flex items-center justify-center shrink-0">
                  {#if Icon}
                    <Icon
                      class={cn(
                        "h-4 w-4",
                        active
                          ? "text-accent-300 drop-shadow-[0_0_8px_rgba(199,155,92,0.5)]"
                          : "text-text-muted group-hover:text-text-primary",
                      )}
                    />
                  {/if}
                </div>
                <div
                  class={cn(
                    "overflow-hidden transition-[max-width,opacity] duration-moderate",
                    isExpanded ? "max-w-[160px] opacity-100 ml-3" : "max-w-0 opacity-0 ml-0",
                  )}
                >
                  {item.label}
                </div>
              </a>
            </li>
          {/each}
        </ul>
      </div>
    {/each}
  </nav>

  <!-- Footer actions -->
  <div class="shrink-0 space-y-1 border-t border-border-subtle px-3 py-3">
    {#if showDevTools}
      <a
        href={resolve("/dev")}
        aria-label="Open dev tools"
        title={!isExpanded ? "Dev Tools" : undefined}
        class="group flex h-8 items-center overflow-hidden whitespace-nowrap text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
      >
        <div class="flex w-8 shrink-0 items-center justify-center">
          <Wrench class="h-4 w-4 transition-colors group-hover:text-text-accent" />
        </div>
        <div
          class={cn(
            "overflow-hidden transition-[max-width,opacity] duration-moderate",
            isExpanded ? "max-w-[160px] opacity-100 ml-1" : "max-w-0 opacity-0 ml-0",
          )}
        >
          <span class="text-mono-sm text-text-disabled transition-colors group-hover:text-text-accent">
            Dev Tools
          </span>
        </div>
      </a>
    {/if}
    <ChangelogDialog version={APP_VERSION}>
      <div
        class="group flex h-8 items-center overflow-hidden whitespace-nowrap text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
        title={!isExpanded ? (updateAvailable ? "Update available" : "Changelog") : undefined}
      >
        <div class="flex w-8 shrink-0 items-center justify-center">
          <span class={cn("led led-sm", updateAvailable ? "led-active" : "led-idle")}></span>
        </div>
        <div
          class={cn(
            "overflow-hidden transition-[max-width,opacity] duration-moderate",
            isExpanded ? "max-w-[160px] opacity-100 ml-1" : "max-w-0 opacity-0 ml-0",
          )}
        >
          <span class="text-mono-sm text-text-disabled transition-colors group-hover:text-text-accent">
            v{APP_VERSION}
            {#if updateAvailable}
              <span class="sr-only">Update available</span>
            {/if}
          </span>
        </div>
      </div>
    </ChangelogDialog>
    <a
      href={docsHref}
      target="_blank"
      rel="noopener noreferrer"
      aria-label="Open Prismedia documentation"
      title={!isExpanded ? "Docs" : undefined}
      class="group flex h-8 items-center overflow-hidden whitespace-nowrap text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
    >
      <div class="flex w-8 shrink-0 items-center justify-center">
        <BookOpen class="h-4 w-4 transition-colors group-hover:text-text-accent" />
      </div>
      <div
        class={cn(
          "overflow-hidden transition-[max-width,opacity] duration-moderate",
          isExpanded ? "max-w-[160px] opacity-100 ml-1" : "max-w-0 opacity-0 ml-0",
        )}
      >
        <span class="text-mono-sm text-text-disabled transition-colors group-hover:text-text-accent">
          Docs
        </span>
      </div>
    </a>
  </div>
</aside>

<style>
  .brand-mark-backdrop {
    position: relative;
    isolation: isolate;
  }

  .brand-mark-backdrop::before {
    content: "";
    position: absolute;
    inset: -0.25rem;
    z-index: 0;
    background:
      radial-gradient(circle at 50% 47%, rgb(244 204 134 / 0.22), transparent 38%),
      radial-gradient(circle at 50% 52%, rgb(196 154 90 / 0.18), transparent 68%);
    filter: blur(0.18rem);
    opacity: 0.95;
    pointer-events: none;
  }

  .brand-mark-backdrop :global(img) {
    filter:
      drop-shadow(0 0 8px rgb(244 204 134 / 0.42))
      drop-shadow(0 0 22px rgb(196 154 90 / 0.28));
  }

  .brand-mark-backdrop-nsfw::before {
    background:
      radial-gradient(circle at 50% 47%, rgb(255 78 70 / 0.25), transparent 38%),
      radial-gradient(circle at 50% 52%, rgb(190 35 35 / 0.2), transparent 68%);
  }

  .brand-mark-backdrop-nsfw :global(img) {
    filter:
      drop-shadow(0 0 8px rgb(255 90 82 / 0.42))
      drop-shadow(0 0 22px rgb(190 35 35 / 0.3));
  }
</style>
