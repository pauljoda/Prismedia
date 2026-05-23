<script lang="ts">
  import { X } from "@lucide/svelte";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { cn } from "@prismedia/ui-svelte";
  import { appShellSections } from "./app-shell-sections";
  import { appShellNavIconMap } from "./app-shell-nav-icon-map";

  interface Props {
    open: boolean;
    onClose: () => void;
  }

  let { open, onClose }: Props = $props();

  const pathname = $derived(page.url.pathname);
  let closeButton = $state<HTMLButtonElement | null>(null);

  $effect(() => {
    if (!open) return;

    document.body.style.overflow = "hidden";
    closeButton?.focus();
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", handler);
    return () => {
      document.body.style.overflow = "";
      window.removeEventListener("keydown", handler);
    };
  });
</script>

{#if open}
  <button
    type="button"
    class="fixed inset-0 z-[60] bg-black/60 backdrop-blur-sm"
    aria-label="Close navigation"
    onclick={onClose}
  ></button>

  <div
    role="dialog"
    aria-modal="true"
    aria-label="Navigation"
    class="fixed inset-x-0 bottom-14 z-[60] border-t border-border-subtle bg-surface-1"
  >
    <div class="flex justify-center pb-1 pt-2">
      <span class="h-1 w-8 bg-border-subtle"></span>
    </div>

    <div class="flex items-center justify-between border-b border-border-subtle px-4 pb-3">
      <span class="text-sm font-medium text-text-primary">Navigate</span>
      <button
        type="button"
        bind:this={closeButton}
        class="flex h-8 w-8 items-center justify-center text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
        aria-label="Close navigation"
        onclick={onClose}
      >
        <X class="h-4 w-4" />
      </button>
    </div>

    <nav class="max-h-[60dvh] overflow-y-auto p-3">
      {#each appShellSections as section (section.id)}
        <div class="mb-4 last:mb-0">
          <div class="px-2 pb-1.5 text-[0.65rem] font-semibold uppercase tracking-widest text-text-accent">
            {section.kicker}
          </div>
          <ul class="space-y-0.5">
            {#each section.items as item (item.href)}
              {@const href = item.href}
              {@const Icon = appShellNavIconMap[item.icon]}
              {@const active = pathname === href || (href !== "/" && pathname.startsWith(href + "/"))}
              <li>
                <a
                  href={resolve(href as "/")}
                  aria-current={active ? "page" : undefined}
                  class={cn(
                    "group relative flex items-center gap-3 px-2.5 py-2.5 text-sm transition-colors",
                    active ? "bg-accent-950 text-glow-accent" : "text-text-muted active:bg-surface-2",
                  )}
                  onclick={onClose}
                >
                  {#if active}
                    <span class="absolute bottom-1.5 left-0 top-1.5 w-[3px] bg-accent-500 shadow-[var(--shadow-glow-accent)]"></span>
                  {/if}
                  {#if Icon}
                    <Icon
                      class={cn(
                        "h-4 w-4 shrink-0",
                        active
                          ? "text-accent-300 drop-shadow-[0_0_8px_rgba(199,155,92,0.5)]"
                          : "text-text-muted group-hover:text-text-primary",
                      )}
                    />
                  {/if}
                  <span>{item.label}</span>
                </a>
              </li>
            {/each}
          </ul>
        </div>
      {/each}
    </nav>
  </div>
{/if}
