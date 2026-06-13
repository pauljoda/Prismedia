<script lang="ts">
  import {
    BookOpen,
    Check,
    ChevronRight,
    Eye,
    EyeOff,
    GripVertical,
    PanelLeftClose,
    PanelLeftOpen,
    Pencil,
    Plus,
    RotateCcw,
    Trash2,
  } from "@lucide/svelte";
  import { dragHandle, dragHandleZone, SHADOW_ITEM_MARKER_PROPERTY_NAME } from "svelte-dnd-action";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { cn, prefersReducedMotion } from "@prismedia/ui-svelte";
  import { useNavCustomization } from "$lib/stores/nav-customization.svelte";
  import { appShellNavIconMap } from "./app-shell-nav-icon-map";
  import LogoMark from "./LogoMark.svelte";
  import ChangelogDialog from "./ChangelogDialog.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { APP_VERSION, fetchReleaseUpdateStatus, type ReleaseUpdateStatus } from "$lib/version";

  interface Props {
    collapsed: boolean;
    onToggle: () => void;
  }

  let { collapsed, onToggle }: Props = $props();

  const nav = useNavCustomization();
  const nsfw = useNsfw();

  let hovered = $state(false);
  let releaseStatus = $state<ReleaseUpdateStatus | null>(null);

  const editing = $derived(nav.editing);
  // Editing always needs labels, so force the rail open while editing.
  const isExpanded = $derived(!collapsed || hovered || editing);
  const brandLogoSize = $derived(isExpanded ? 40 : 34);
  const brandLogoNsfw = $derived(nsfw.mode === "show");
  const updateAvailable = $derived(releaseStatus?.updateAvailable === true);
  // Surface the running build's channel (dev/alpha/beta); release is the default and stays clean.
  const channelLabel = $derived.by(() => {
    const channel = releaseStatus?.channel?.trim().toLowerCase();
    if (!channel || channel === "release") return null;
    return channel;
  });
  const pathname = $derived(page.url.pathname);
  const docsHref = "https://pauljoda.github.io/Prismedia/";

  // Normal-mode rendering: drop hidden items and empty sections.
  const visibleSections = $derived(
    nav.resolvedSections
      .map((section) => ({ ...section, items: section.items.filter((i) => !i.hidden) }))
      .filter((section) => section.items.length > 0),
  );

  // --- Drag-and-drop edit state ---------------------------------------------
  interface DndItem {
    id: string;
    href: string;
    label: string;
    icon: string;
    hidden: boolean;
  }
  interface DndSection {
    id: string;
    label: string;
    items: DndItem[];
  }

  const flipMs = $derived(prefersReducedMotion() ? 0 : 220);
  // Remove svelte-dnd-action's default drop outline; we style the drop zone and
  // placeholder ourselves (see .dnd-drop-target / .dnd-shadow below).
  const dropTargetStyle = {};
  const dropTargetClasses = ["dnd-drop-target"];
  let dndSections = $state<DndSection[]>([]);
  let dragging = $state(false);

  // Mirror the store into the local DnD model while editing and idle. Typing in
  // a label input or dragging mutates the local model directly without writing
  // the store on every change, so this rebuild does not fight the interaction.
  $effect(() => {
    const sections = nav.resolvedSections;
    if (!editing || dragging) return;
    dndSections = sections.map((s) => ({
      id: s.id,
      label: s.label,
      items: s.items.map((i) => ({
        id: i.href,
        href: i.href,
        label: i.label,
        icon: i.icon,
        hidden: i.hidden,
      })),
    }));
  });

  // svelte-dnd-action injects a placeholder clone (the "shadow" item) at the
  // hovered drop position; we render it as a brass-tinted slot.
  function isShadow(item: unknown): boolean {
    return !!(item as Record<string, unknown>)[SHADOW_ITEM_MARKER_PROPERTY_NAME];
  }

  function commitLayout() {
    nav.setLayout(
      dndSections.map((s) => ({ id: s.id, label: s.label, items: s.items.map((i) => i.href) })),
    );
  }

  function onSectionsConsider(e: CustomEvent<{ items: DndSection[] }>) {
    dragging = true;
    dndSections = e.detail.items;
  }
  function onSectionsFinalize(e: CustomEvent<{ items: DndSection[] }>) {
    dndSections = e.detail.items;
    dragging = false;
    commitLayout();
  }
  function onItemsConsider(sectionId: string, e: CustomEvent<{ items: DndItem[] }>) {
    dragging = true;
    dndSections = dndSections.map((s) => (s.id === sectionId ? { ...s, items: e.detail.items } : s));
  }
  function onItemsFinalize(sectionId: string, e: CustomEvent<{ items: DndItem[] }>) {
    dndSections = dndSections.map((s) => (s.id === sectionId ? { ...s, items: e.detail.items } : s));
    dragging = false;
    commitLayout();
  }

  function renameLocal(sectionId: string, label: string) {
    dndSections = dndSections.map((s) => (s.id === sectionId ? { ...s, label } : s));
  }

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
        <LogoMark size={brandLogoSize} class="relative z-10" />
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
        class="flex h-8 w-8 items-center justify-center rounded-sm text-text-muted hover:text-text-primary hover:bg-surface-2 transition-colors duration-fast"
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

  <!-- Navigation -->
  {#if editing}
    <!-- Edit mode: drag to reorder, rename inline, hide/show, add sections -->
    <nav class="flex-1 overflow-y-auto overflow-x-hidden px-2 py-3 scrollbar-hidden">
      <section
        use:dragHandleZone={{
          items: dndSections,
          flipDurationMs: flipMs,
          type: "nav-sections",
          dropTargetStyle,
          dropTargetClasses,
        }}
        onconsider={onSectionsConsider}
        onfinalize={onSectionsFinalize}
        class="space-y-3"
      >
        {#each dndSections as section (section.id)}
          <div
            class={cn(
              "rounded-md border border-border-subtle bg-surface-2/40",
              isShadow(section) && "dnd-shadow",
            )}
          >
            <!-- Section header -->
            <div class="flex items-center gap-1 px-1.5 py-1.5">
              <span
                use:dragHandle
                aria-label="Reorder section"
                class="drag-handle flex h-7 w-5 cursor-grab items-center justify-center text-text-disabled hover:text-text-muted"
              >
                <GripVertical class="h-4 w-4" />
              </span>
              <input
                value={section.label}
                oninput={(e) => renameLocal(section.id, e.currentTarget.value)}
                onchange={(e) => nav.renameSection(section.id, e.currentTarget.value.trim() || section.label)}
                onblur={(e) => nav.renameSection(section.id, e.currentTarget.value.trim() || section.label)}
                aria-label="Section name"
                maxlength={40}
                class="section-input allow-compact-input-text text-kicker min-w-0 flex-1 bg-transparent outline-none"
              />
              <button
                type="button"
                class="icon-btn icon-btn-danger"
                aria-label="Delete section"
                disabled={dndSections.length <= 1}
                onclick={() => nav.removeSection(section.id)}
              >
                <Trash2 class="h-3.5 w-3.5" />
              </button>
            </div>

            <!-- Section items -->
            <ul
              use:dragHandleZone={{
                items: section.items,
                flipDurationMs: flipMs,
                type: "nav-items",
                dropTargetStyle,
                dropTargetClasses,
              }}
              onconsider={(e) => onItemsConsider(section.id, e)}
              onfinalize={(e) => onItemsFinalize(section.id, e)}
              class="min-h-[10px] space-y-0.5 px-1.5 pb-1.5"
            >
              {#each section.items as item (item.id)}
                {@const Icon = appShellNavIconMap[item.icon]}
                <li
                  class={cn(
                    "flex items-center gap-1.5 rounded-sm bg-surface-1/60 px-1.5 py-1.5",
                    item.hidden && "opacity-45",
                    isShadow(item) && "dnd-shadow",
                  )}
                >
                  <span
                    use:dragHandle
                    aria-label="Reorder item"
                    class="drag-handle flex h-6 w-4 cursor-grab items-center justify-center text-text-disabled hover:text-text-muted"
                  >
                    <GripVertical class="h-3.5 w-3.5" />
                  </span>
                  <div class="flex w-5 shrink-0 items-center justify-center">
                    {#if Icon}
                      <Icon class="h-4 w-4 text-text-muted" />
                    {/if}
                  </div>
                  <span class="min-w-0 flex-1 truncate text-sm text-text-primary">{item.label}</span>
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
                </li>
              {/each}
            </ul>
          </div>
        {/each}
      </section>

      <button type="button" class="add-section mt-3" onclick={() => nav.addSection("New Section")}>
        <Plus class="h-4 w-4" />
        <span>Add section</span>
      </button>
    </nav>
  {:else}
    <!-- Normal mode -->
    <nav class="flex-1 overflow-y-auto overflow-x-hidden py-3 scrollbar-hidden">
      {#each visibleSections as section (section.id)}
        <div class="mb-4">
          <button
            type="button"
            onclick={() => nav.toggleSectionCollapsed(section.id)}
            tabindex={isExpanded ? 0 : -1}
            aria-expanded={!section.collapsed}
            class={cn(
              "group/sec flex w-full items-center gap-1 px-4 pb-1.5 text-left text-kicker whitespace-nowrap transition-[max-height,opacity] duration-moderate overflow-hidden hover:text-text-muted",
              isExpanded ? "max-h-8 opacity-100" : "max-h-0 opacity-0",
            )}
          >
            <ChevronRight
              class={cn(
                "h-3 w-3 shrink-0 text-text-disabled transition-transform duration-fast group-hover/sec:text-text-muted",
                section.collapsed ? "" : "rotate-90",
              )}
            />
            <span>{section.label}</span>
          </button>
          <div
            class={cn(
              "mx-auto mb-1 w-6 separator transition-[max-height,opacity] duration-moderate overflow-hidden",
              !isExpanded ? "max-h-2 opacity-100" : "max-h-0 opacity-0",
            )}
          ></div>
          {#if !(isExpanded && section.collapsed)}
          <ul class="space-y-0.5 px-2">
            {#each section.items as item (item.href)}
              {@const Icon = appShellNavIconMap[item.icon]}
              {@const active = isActive(item.href)}
              <li>
                <a
                  href={resolve(item.href as "/")}
                  class={cn(
                    "group relative flex items-center rounded-sm px-2.5 py-2 text-sm transition-colors duration-fast whitespace-nowrap",
                    active
                      ? "bg-accent-950 text-glow-accent"
                      : "text-text-muted hover:text-text-primary hover:bg-surface-2",
                  )}
                  title={!isExpanded ? item.label : undefined}
                >
                  {#if active}
                    <span class="absolute left-0 top-1.5 bottom-1.5 w-[3px] rounded-l-full bg-accent-500 shadow-[var(--shadow-glow-accent)]"></span>
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
          {/if}
        </div>
      {/each}
    </nav>
  {/if}

  <!-- Footer actions -->
  <div class="shrink-0 space-y-1 border-t border-border-subtle px-3 py-3">
    {#if editing}
      <button
        type="button"
        onclick={() => nav.reset()}
        class="group flex h-8 w-full items-center overflow-hidden whitespace-nowrap rounded-sm text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
        title={!isExpanded ? "Reset navigation" : undefined}
      >
        <div class="flex w-8 shrink-0 items-center justify-center">
          <RotateCcw class="h-4 w-4" />
        </div>
        <span class="ml-1 text-mono-sm">Reset to default</span>
      </button>
    {/if}

    <ChangelogDialog version={APP_VERSION}>
      <div
        class="group flex h-8 items-center overflow-hidden whitespace-nowrap rounded-sm text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
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
            v{releaseStatus?.localVersion ?? APP_VERSION}
            {#if channelLabel}
              <span
                class="ml-1 rounded-xs bg-surface-2 px-1 text-mono-sm text-[0.6rem] uppercase tracking-wide text-text-muted"
              >{channelLabel}</span>
            {/if}
            {#if updateAvailable}
              <span class="sr-only">Update available</span>
            {/if}
          </span>
        </div>
      </div>
    </ChangelogDialog>
    <div class="flex items-center gap-1">
      <a
        href={docsHref}
        target="_blank"
        rel="noopener noreferrer"
        aria-label="Open Prismedia documentation"
        title={!isExpanded ? "Docs" : undefined}
        class="group flex h-8 flex-1 items-center overflow-hidden whitespace-nowrap rounded-sm text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
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
      {#if isExpanded}
        <button
          type="button"
          onclick={() => nav.toggleEdit()}
          aria-pressed={editing}
          aria-label={editing ? "Done editing navigation" : "Edit navigation"}
          title={editing ? "Done" : "Edit navigation"}
          class={cn(
            "flex h-8 w-8 shrink-0 items-center justify-center rounded-sm transition-colors duration-fast",
            editing
              ? "text-text-accent"
              : "text-text-disabled hover:bg-surface-2 hover:text-text-accent",
          )}
        >
          {#if editing}
            <Check class="h-4 w-4" />
          {:else}
            <Pencil class="h-[0.95rem] w-[0.95rem]" />
          {/if}
        </button>
      {/if}
    </div>
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

  /* Matches the .text-kicker token used by normal-mode section labels. The
     global input font rules (anti-iOS-zoom + font:inherit opt-out) would
     otherwise override the class, so we restate the kicker metrics in this
     unlayered scoped rule. Colour still comes from .text-kicker. */
  .section-input {
    font-size: 0.65rem;
    font-weight: 600;
    letter-spacing: 0.15em;
    text-transform: uppercase;
    padding: 0.2rem 0.3rem;
    border-radius: var(--radius-xs);
  }
  .section-input:focus {
    background: var(--color-surface-1);
    box-shadow: var(--shadow-focus-accent);
  }

  /* Drag-and-drop: replace the library default outline with a rounded,
     brass-tinted drop zone and an empty placeholder slot. */
  :global(.dnd-drop-target) {
    outline: none !important;
    border-radius: var(--radius-md);
    box-shadow: inset 0 0 0 1px var(--color-border-accent-strong);
    background: rgba(242, 194, 106, 0.04);
  }
  :global(.dnd-shadow) {
    border-radius: var(--radius-sm) !important;
    background: rgba(242, 194, 106, 0.1) !important;
    box-shadow: inset 0 0 0 1px var(--color-border-accent);
  }
  :global(.dnd-shadow *) {
    visibility: hidden;
  }

  .icon-btn {
    display: inline-flex;
    height: 1.75rem;
    width: 1.75rem;
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
  .icon-btn-danger:hover {
    color: var(--color-error-text, #f87171);
  }

  .add-section {
    display: flex;
    width: 100%;
    align-items: center;
    justify-content: center;
    gap: 0.4rem;
    border-radius: var(--radius-md);
    border: 1px dashed var(--color-border-default);
    padding: 0.5rem;
    font-size: 0.8rem;
    color: var(--color-text-muted);
    transition:
      color var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      background var(--duration-fast) var(--ease-default);
  }
  .add-section:hover {
    color: var(--color-text-primary);
    border-color: var(--color-border-accent);
    background: var(--color-surface-2);
  }
</style>
