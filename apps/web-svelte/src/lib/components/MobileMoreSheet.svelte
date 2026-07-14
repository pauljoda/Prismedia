<script lang="ts">
  import {
    BookOpen,
    Check,
    ChevronDown,
    ChevronLeft,
    ChevronRight,
    ChevronUp,
    Eye,
    EyeOff,
    FolderInput,
    LogOut,
    Pencil,
    Plus,
    RotateCcw,
    Star,
    Trash2,
    X,
  } from "@lucide/svelte";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { cn, ColorInput } from "@prismedia/ui-svelte";
  import { useNavCustomization } from "$lib/stores/nav-customization.svelte";
  import { useSession } from "$lib/stores/session.svelte";
  import { navItemVisible } from "$lib/nav/nav-visibility";
  import UserAvatar from "./auth/UserAvatar.svelte";
  import { appShellNavIconMap } from "./app-shell-nav-icon-map";
  import ChangelogDialog from "./ChangelogDialog.svelte";
  import RenameSectionDialog from "./nav/RenameSectionDialog.svelte";
  import MoveToSectionDialog from "./nav/MoveToSectionDialog.svelte";
  import { APP_VERSION, fetchReleaseUpdateStatus, type ReleaseUpdateStatus } from "$lib/version";

  interface Props {
    /** Whether the sheet is in the DOM (kept true through the close animation). */
    mounted: boolean;
    /** 0 (closed) → 1 (fully open). Tracks the finger while dragging. */
    progress: number;
    /** True while the finger is actively dragging — disables the snap transition. */
    dragging: boolean;
    reduceMotion: boolean;
    onClose: () => void;
    /** Begin a drag-down-to-dismiss from the grab handle / header. */
    onHandlePointerDown: (e: PointerEvent) => void;
  }

  let { mounted, progress, dragging, reduceMotion, onClose, onHandlePointerDown }: Props = $props();

  const nav = useNavCustomization();
  const session = useSession();
  const pathname = $derived(page.url.pathname);
  const sections = $derived(nav.resolvedSections);
  const favorites = $derived(nav.resolvedFavorites);

  // Mirror the desktop sidebar footer: surface the changelog (with update LED), the running
  // build's version/channel, and the docs link so they are reachable from mobile too.
  const docsHref = "https://pauljoda.github.io/Prismedia/";
  let releaseStatus = $state<ReleaseUpdateStatus | null>(null);
  const updateAvailable = $derived(releaseStatus?.updateAvailable === true);
  const channelLabel = $derived.by(() => {
    const channel = releaseStatus?.channel?.trim().toLowerCase();
    if (!channel || channel === "release") return null;
    return channel;
  });

  $effect(() => {
    void fetchReleaseUpdateStatus().then((status) => {
      releaseStatus = status;
    });
  });

  const opened = $derived(mounted && progress > 0.5);
  const useTransition = $derived(!dragging && !reduceMotion);
  const translateY = $derived(`${(1 - progress) * 100}%`);
  // Defence in depth: when essentially closed, never intercept input even if a
  // frame of `mounted` lingers — otherwise the invisible backdrop blocks taps.
  const interactive = $derived(progress > 0.05);

  let editing = $state(false);
  let closeButton = $state<HTMLButtonElement | null>(null);

  // Section name dialog state (rename + add share one dialog).
  let nameDialogOpen = $state(false);
  let nameDialogTitle = $state("");
  let nameDialogValue = $state("");
  let nameDialogConfirm = $state("Save");
  let nameDialogAction = $state<(value: string) => void>(() => {});

  // Move-to-section dialog state.
  let moveTarget = $state<{ href: string; label: string; sectionId: string } | null>(null);

  function isActive(href: string): boolean {
    return pathname === href || (href !== "/" && pathname.startsWith(href + "/"));
  }

  function openRename(sectionId: string, current: string) {
    nameDialogTitle = "Rename section";
    nameDialogValue = current;
    nameDialogConfirm = "Save";
    nameDialogAction = (value) => nav.renameSection(sectionId, value);
    nameDialogOpen = true;
  }

  function openAddSection() {
    nameDialogTitle = "New section";
    nameDialogValue = "";
    nameDialogConfirm = "Add";
    nameDialogAction = (value) => nav.addSection(value);
    nameDialogOpen = true;
  }

  function handleFavorite(href: string) {
    if (!nav.toggleFavorite(href)) {
      try {
        navigator.vibrate?.(10);
      } catch {
        // Ignore unavailable vibration API.
      }
    }
  }

  // Reset transient edit state whenever the sheet leaves the DOM.
  $effect(() => {
    if (mounted) return;
    editing = false;
    nameDialogOpen = false;
    moveTarget = null;
  });

  // Lock body scroll and wire escape / focus while the sheet is mounted.
  $effect(() => {
    if (!mounted) return;
    document.body.style.overflow = "hidden";
    const handler = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      if (nameDialogOpen || moveTarget) return; // dialogs handle their own escape
      onClose();
    };
    window.addEventListener("keydown", handler);
    return () => {
      document.body.style.overflow = "";
      window.removeEventListener("keydown", handler);
    };
  });

  // Move focus into the sheet once it is fully open.
  let focused = false;
  $effect(() => {
    if (opened && !focused) {
      focused = true;
      closeButton?.focus();
    } else if (!opened) {
      focused = false;
    }
  });
</script>

{#if mounted}
  <button
    type="button"
    class={cn("sheet-backdrop fixed inset-0 z-[60] bg-black/60 backdrop-blur-sm md:hidden", useTransition && "animate")}
    style:opacity={progress}
    style:pointer-events={interactive ? "auto" : "none"}
    aria-label="Close navigation"
    onclick={onClose}
  ></button>

  <div
    role="dialog"
    aria-modal="true"
    aria-label="Navigation"
    class={cn(
      "sheet app-glass fixed inset-x-0 bottom-0 z-[60] flex max-h-[82dvh] flex-col border md:hidden",
      useTransition && "animate",
    )}
    style:transform="translateY({translateY})"
    style:pointer-events={interactive ? "auto" : "none"}
  >
    <!-- Drag-to-dismiss zone: grab handle + title. Touch enhancement only —
         the sheet is also dismissable via the close button and backdrop. -->
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div class="drag-zone" onpointerdown={onHandlePointerDown}>
      <div class="flex justify-center pt-2.5">
        <span class="h-1 w-9 rounded-full bg-border-default"></span>
      </div>
      <div class="flex items-start justify-between gap-3 px-4 pb-3 pt-3">
        <div class="min-w-0">
          <p class="text-kicker text-text-accent">{editing ? "Customize" : "Navigate"}</p>
          <h2 class="font-heading text-base font-semibold tracking-wide text-text-primary">
            {editing ? "Edit navigation" : "All sections"}
          </h2>
        </div>
        <div class="flex shrink-0 items-center gap-1">
          <button
            type="button"
            onpointerdown={(e) => e.stopPropagation()}
            onclick={() => (editing = !editing)}
            aria-pressed={editing}
            class={cn(
              "flex h-9 w-9 items-center justify-center rounded-sm transition-colors",
              editing ? "text-text-accent" : "text-text-muted hover:bg-surface-2 hover:text-text-primary",
            )}
            aria-label={editing ? "Done editing" : "Edit navigation"}
          >
            {#if editing}
              <Check class="h-[1.15rem] w-[1.15rem]" />
            {:else}
              <Pencil class="h-4 w-4" />
            {/if}
          </button>
          <button
            type="button"
            bind:this={closeButton}
            onpointerdown={(e) => e.stopPropagation()}
            class="flex h-9 w-9 items-center justify-center rounded-sm text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
            aria-label="Close navigation"
            onclick={onClose}
          >
            <X class="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>

    {#if editing}
      <!-- Bottom-bar order preview -->
      <div class="border-t border-border-subtle px-4 py-3">
        <div class="mb-2 flex items-center justify-between">
          <span class="text-kicker text-text-muted">Bottom bar</span>
          <span class="text-mono-sm text-text-disabled">{nav.prefs.mobileFavorites.length}/4 · star to pin</span>
        </div>
        <div class="grid grid-cols-4 gap-1.5">
          {#each favorites as fav, i (fav.href)}
            {@const FavIcon = appShellNavIconMap[fav.icon]}
            <div class="fav-cell">
              {#if FavIcon}
                <FavIcon class="h-4 w-4 text-text-accent" />
              {/if}
              <span class="fav-label">{fav.label}</span>
              <div class="flex items-center gap-0.5">
                <button
                  type="button"
                  class="fav-arrow"
                  aria-label="Move left"
                  disabled={i === 0}
                  onclick={() => nav.moveFavorite(fav.href, -1)}
                >
                  <ChevronLeft class="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  class="fav-arrow"
                  aria-label="Move right"
                  disabled={i === favorites.length - 1}
                  onclick={() => nav.moveFavorite(fav.href, 1)}
                >
                  <ChevronRight class="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          {/each}
        </div>
      </div>
    {/if}

    <!-- Body -->
    <nav
      class="flex-1 overflow-y-auto border-t border-border-subtle px-3 pb-[calc(1rem+env(safe-area-inset-bottom,0px))] pt-2"
      style:scrollbar-width="thin"
    >
      {#each sections as section, sectionIndex (section.id)}
        {@const visibleItems = editing ? section.items : section.items.filter((i) => !i.hidden && navItemVisible(i.href, session))}
        {#if editing || visibleItems.length > 0}
          <div class="nav-section mb-3 last:mb-0" style:--section-accent={section.accent}>
            <!-- Section header -->
            <div class="flex items-center justify-between gap-2 px-1.5 pb-1">
              <span class="section-heading">
                <span class="section-marker" aria-hidden="true"></span>
                <span class="section-label text-kicker truncate">{section.label}</span>
              </span>
              {#if editing}
                <div class="flex shrink-0 items-center gap-0.5">
                  <ColorInput
                    value={section.accent}
                    aria-label={`Color for ${section.label}`}
                    onValueChange={(accent) => nav.setSectionAccent(section.id, accent)}
                  />
                  <button
                    type="button"
                    class="icon-btn"
                    aria-label="Move section up"
                    disabled={sectionIndex === 0}
                    onclick={() => nav.moveSectionByOffset(section.id, -1)}
                  >
                    <ChevronUp class="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    class="icon-btn"
                    aria-label="Move section down"
                    disabled={sectionIndex === sections.length - 1}
                    onclick={() => nav.moveSectionByOffset(section.id, 1)}
                  >
                    <ChevronDown class="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    class="icon-btn"
                    aria-label="Rename section"
                    onclick={() => openRename(section.id, section.label)}
                  >
                    <Pencil class="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    class="icon-btn icon-btn-danger"
                    aria-label="Delete section"
                    disabled={sections.length <= 1}
                    onclick={() => nav.removeSection(section.id)}
                  >
                    <Trash2 class="h-3.5 w-3.5" />
                  </button>
                </div>
              {/if}
            </div>

            <ul class="space-y-0.5">
              {#each visibleItems as item, itemIndex (item.href)}
                {@const Icon = appShellNavIconMap[item.icon]}
                {@const active = isActive(item.href)}
                {@const favorite = nav.isFavorite(item.href)}
                <li>
                  {#if editing}
                    <div
                      class={cn(
                        "flex items-center gap-2 rounded-sm px-2 py-1.5",
                        item.hidden ? "opacity-45" : "",
                      )}
                    >
                      {#if Icon}
                        <Icon class="h-4 w-4 shrink-0 text-text-muted" />
                      {/if}
                      <span class="min-w-0 flex-1 truncate text-sm text-text-primary">{item.label}</span>

                      <div class="flex shrink-0 items-center gap-0.5">
                        <button
                          type="button"
                          class={cn("icon-btn", favorite && "icon-btn-active")}
                          aria-pressed={favorite}
                          aria-label={favorite ? "Remove from bottom bar" : "Add to bottom bar"}
                          disabled={item.hidden || (!favorite && nav.favoritesFull)}
                          onclick={() => handleFavorite(item.href)}
                        >
                          <Star class={cn("h-4 w-4", favorite && "fill-current")} />
                        </button>
                        <button
                          type="button"
                          class="icon-btn"
                          aria-label={item.hidden ? "Show item" : "Hide item"}
                          onclick={() => nav.toggleHidden(item.href)}
                        >
                          {#if item.hidden}
                            <EyeOff class="h-4 w-4" />
                          {:else}
                            <Eye class="h-4 w-4" />
                          {/if}
                        </button>
                        <button
                          type="button"
                          class="icon-btn"
                          aria-label="Move up"
                          disabled={itemIndex === 0}
                          onclick={() => nav.moveItemWithinSection(section.id, item.href, -1)}
                        >
                          <ChevronUp class="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          class="icon-btn"
                          aria-label="Move down"
                          disabled={itemIndex === visibleItems.length - 1}
                          onclick={() => nav.moveItemWithinSection(section.id, item.href, 1)}
                        >
                          <ChevronDown class="h-4 w-4" />
                        </button>
                        {#if sections.length > 1}
                          <button
                            type="button"
                            class="icon-btn"
                            aria-label="Move to another section"
                            onclick={() =>
                              (moveTarget = { href: item.href, label: item.label, sectionId: section.id })}
                          >
                            <FolderInput class="h-4 w-4" />
                          </button>
                        {/if}
                      </div>
                    </div>
                  {:else}
                    <a
                      href={resolve(item.href as "/")}
                      aria-current={active ? "page" : undefined}
                      class={cn(
                        "group relative flex items-center gap-3 rounded-sm px-2.5 py-2.5 text-sm transition-colors",
                        active ? "nav-item-active text-text-primary" : "text-text-muted active:bg-surface-2",
                      )}
                      onclick={onClose}
                    >
                      {#if active}
                        <span class="active-rail absolute bottom-1.5 left-0 top-1.5 w-[3px] rounded-l-full"></span>
                      {/if}
                      {#if Icon}
                        <Icon
                          class={cn(
                            "h-4 w-4 shrink-0",
                            active
                              ? "nav-item-icon-active"
                              : "text-text-muted group-hover:text-text-primary",
                          )}
                        />
                      {/if}
                      <span class="flex-1 truncate">{item.label}</span>
                      {#if favorite}
                        <Star class="h-3.5 w-3.5 shrink-0 fill-current text-accent-500/70" aria-label="In bottom bar" />
                      {/if}
                    </a>
                  {/if}
                </li>
              {/each}
            </ul>
          </div>
        {/if}
      {/each}

      {#if editing}
        <div class="mt-2 flex items-center justify-between gap-2 px-1">
          <button type="button" class="ghost-action" onclick={openAddSection}>
            <Plus class="h-4 w-4" />
            <span>Add section</span>
          </button>
          <button type="button" class="ghost-action" onclick={() => nav.reset()}>
            <RotateCcw class="h-4 w-4" />
            <span>Reset</span>
          </button>
        </div>
      {:else}
        <!-- Footer actions: changelog (with update indicator) and docs, matching the desktop sidebar. -->
        <div class="mt-2 space-y-0.5 border-t border-border-subtle pt-3">
          <ChangelogDialog version={APP_VERSION}>
            <div
              class="flex w-full items-center gap-3 rounded-sm px-2.5 py-2.5 text-sm text-text-muted active:bg-surface-2"
            >
              <span class={cn("led led-sm shrink-0", updateAvailable ? "led-active" : "led-idle")}></span>
              <span class="flex-1 text-left">{updateAvailable ? "Update available" : "Changelog"}</span>
              <span class="text-mono-sm text-text-disabled">
                v{releaseStatus?.localVersion ?? APP_VERSION}
                {#if channelLabel}
                  <span
                    class="ml-1 rounded-xs bg-surface-2 px-1 text-mono-sm text-[0.6rem] uppercase tracking-wide text-text-muted"
                  >{channelLabel}</span>
                {/if}
              </span>
            </div>
          </ChangelogDialog>
          <a
            href={docsHref}
            target="_blank"
            rel="noopener noreferrer"
            class="flex items-center gap-3 rounded-sm px-2.5 py-2.5 text-sm text-text-muted active:bg-surface-2"
            onclick={onClose}
          >
            <BookOpen class="h-4 w-4 shrink-0" />
            <span class="flex-1">Docs</span>
          </a>
          {#if session.user}
            <div class="flex items-center gap-1 border-t border-border-subtle pt-2">
              <a
                href={resolve("/account")}
                class="flex min-w-0 flex-1 items-center gap-3 rounded-sm px-2.5 py-2.5 text-sm text-text-muted active:bg-surface-2"
                onclick={onClose}
              >
                <UserAvatar displayName={session.user.displayName} username={session.user.username} size="md" />
                <span class="flex min-w-0 flex-col">
                  <span class="truncate text-text-primary">{session.user.displayName}</span>
                  <span class="font-mono text-[0.6rem] uppercase tracking-wide text-text-disabled">
                    {session.isAdmin ? "Administrator" : "Member"}
                  </span>
                </span>
              </a>
              <button
                type="button"
                class="flex h-10 w-10 shrink-0 items-center justify-center rounded-sm text-text-muted active:bg-surface-2"
                aria-label="Sign out"
                onclick={() => void session.logout()}
              >
                <LogOut class="h-4 w-4" />
              </button>
            </div>
          {/if}
        </div>
      {/if}
    </nav>
  </div>

  <RenameSectionDialog
    open={nameDialogOpen}
    title={nameDialogTitle}
    value={nameDialogValue}
    confirmLabel={nameDialogConfirm}
    onConfirm={(value) => nameDialogAction(value)}
    onClose={() => (nameDialogOpen = false)}
  />

  <MoveToSectionDialog
    open={!!moveTarget}
    itemLabel={moveTarget?.label ?? ""}
    sections={sections.map((s) => ({ id: s.id, label: s.label }))}
    currentSectionId={moveTarget?.sectionId ?? ""}
    onMove={(sectionId) => moveTarget && nav.moveItemToSection(moveTarget.href, sectionId)}
    onClose={() => (moveTarget = null)}
  />
{/if}

<style>
  .sheet {
    border-top-left-radius: var(--radius-2xl);
    border-top-right-radius: var(--radius-2xl);
    box-shadow:
      0 -12px 40px rgba(0, 0, 0, 0.5),
      var(--shadow-panel);
    will-change: transform;
  }

  .section-label {
    color: var(--color-text-muted);
  }

  .section-heading {
    display: inline-flex;
    min-width: 0;
    align-items: center;
    gap: 0.4rem;
  }

  .section-marker {
    width: 0.7rem;
    height: 2px;
    flex: 0 0 auto;
    background: color-mix(in srgb, var(--section-accent) 74%, #c7c9cc);
    opacity: 0.82;
  }

  .nav-item-active {
    background: rgb(255 255 255 / 0.055);
    box-shadow: inset 0 0 0 1px var(--color-border-subtle);
  }

  .active-rail {
    background: color-mix(in srgb, var(--section-accent) 76%, #c7c9cc);
    opacity: 0.9;
  }

  .nav-item-icon-active {
    color: var(--color-text-primary);
  }
  .sheet.animate {
    transition: transform 280ms var(--ease-mechanical);
  }
  .sheet-backdrop {
    will-change: opacity;
  }
  .sheet-backdrop.animate {
    transition: opacity 200ms var(--ease-default);
  }

  .drag-zone {
    touch-action: none;
    cursor: grab;
  }

  .icon-btn {
    display: inline-flex;
    height: 1.85rem;
    width: 1.85rem;
    align-items: center;
    justify-content: center;
    border-radius: var(--radius-sm);
    color: var(--color-text-muted);
    transition:
      color var(--duration-fast) var(--ease-default),
      background var(--duration-fast) var(--ease-default);
  }
  .icon-btn:hover {
    color: var(--color-text-primary);
    background: var(--color-surface-2);
  }
  .icon-btn:disabled {
    opacity: 0.3;
    pointer-events: none;
  }
  .icon-btn-active {
    color: var(--color-accent-500);
  }
  .icon-btn-danger:hover {
    color: var(--color-error-text, #f87171);
  }

  /* Bottom-bar preview cells */
  .fav-cell {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.3rem;
    border-radius: var(--radius-md);
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-1);
    padding: 0.5rem 0.25rem 0.4rem;
  }
  .fav-label {
    max-width: 100%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.62rem;
    color: var(--color-text-secondary);
  }
  .fav-arrow {
    display: inline-flex;
    height: 1.4rem;
    width: 1.4rem;
    align-items: center;
    justify-content: center;
    border-radius: var(--radius-xs);
    color: var(--color-text-muted);
    transition:
      color var(--duration-fast) var(--ease-default),
      background var(--duration-fast) var(--ease-default);
  }
  .fav-arrow:hover {
    color: var(--color-text-primary);
    background: var(--color-surface-2);
  }
  .fav-arrow:disabled {
    opacity: 0.3;
    pointer-events: none;
  }

  .ghost-action {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    border-radius: var(--radius-sm);
    padding: 0.5rem 0.75rem;
    font-size: 0.8rem;
    color: var(--color-text-muted);
    transition:
      color var(--duration-fast) var(--ease-default),
      background var(--duration-fast) var(--ease-default);
  }
  .ghost-action:hover {
    color: var(--color-text-primary);
    background: var(--color-surface-2);
  }
</style>
