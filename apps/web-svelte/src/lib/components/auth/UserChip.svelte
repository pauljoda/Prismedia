<script lang="ts">
  import { LogOut, UserRound, UsersRound } from "@lucide/svelte";
  import { cn, flyUp } from "@prismedia/ui-svelte";
  import { resolve } from "$app/paths";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import { useSession } from "$lib/stores/session.svelte";
  import UserAvatar from "./UserAvatar.svelte";

  interface Props {
    /** Whether the sidebar rail is expanded (labels visible). */
    expanded: boolean;
  }

  let { expanded }: Props = $props();

  const session = useSession();
  let open = $state(false);
  let container = $state<HTMLElement | null>(null);

  const roleLabel = $derived(session.isAdmin ? "Administrator" : "Member");

  function handleWindowClick(event: MouseEvent) {
    if (open && container && !container.contains(event.target as Node)) {
      open = false;
    }
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === "Escape") {
      open = false;
    }
  }
</script>

<svelte:window onclick={handleWindowClick} onkeydown={handleKeydown} />

{#if session.user}
  <div class="relative" bind:this={container}>
    {#if open}
      <div
        class="floating-surface absolute bottom-full left-0 z-30 mb-2 w-56 overflow-hidden"
        use:keepFlyoutOnScreen
        transition:flyUp={{ duration: 160 }}
        role="menu"
      >
        <div class="border-b border-border-subtle px-3 py-2.5">
          <p class="truncate text-sm text-text-primary">{session.user.displayName}</p>
          <p class="font-mono text-[0.65rem] tracking-wide text-text-disabled uppercase">{roleLabel}</p>
        </div>
        <div class="p-1">
          <a
            href={resolve("/account")}
            role="menuitem"
            class="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm text-text-secondary transition-colors hover:bg-surface-2 hover:text-text-primary"
            onclick={() => (open = false)}
          >
            <UserRound class="size-4" />
            Account
          </a>
          {#if session.isAdmin}
            <a
              href={resolve("/settings/users")}
              role="menuitem"
              class="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm text-text-secondary transition-colors hover:bg-surface-2 hover:text-text-primary"
              onclick={() => (open = false)}
            >
              <UsersRound class="size-4" />
              Manage users
            </a>
          {/if}
          <div class="my-1 border-t border-border-subtle"></div>
          <button
            type="button"
            role="menuitem"
            class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm text-text-secondary transition-colors hover:bg-surface-2 hover:text-error"
            onclick={() => void session.logout()}
          >
            <LogOut class="size-4" />
            Sign out
          </button>
        </div>
      </div>
    {/if}

    <button
      type="button"
      class="group flex h-9 w-full items-center overflow-hidden rounded-sm whitespace-nowrap text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary"
      title={!expanded ? session.user.displayName : undefined}
      aria-haspopup="menu"
      aria-expanded={open}
      onclick={() => (open = !open)}
    >
      <div class="flex w-8 shrink-0 items-center justify-center">
        <UserAvatar displayName={session.user.displayName} username={session.user.username} />
      </div>
      <div
        class={cn(
          "flex flex-col items-start overflow-hidden text-left transition-[max-width,opacity] duration-moderate",
          expanded ? "ml-1 max-w-[160px] opacity-100" : "ml-0 max-w-0 opacity-0",
        )}
      >
        <span class="max-w-full truncate text-mono-sm text-text-primary">{session.user.displayName}</span>
        <span class="font-mono text-[0.6rem] tracking-wide text-text-disabled uppercase">{roleLabel}</span>
      </div>
    </button>
  </div>
{/if}
