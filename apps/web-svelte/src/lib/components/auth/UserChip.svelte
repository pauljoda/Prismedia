<script lang="ts">
  import { LogOut, UserRound, UsersRound } from "@lucide/svelte";
  import { cn, DropdownMenu } from "@prismedia/ui-svelte";
  import { resolve } from "$app/paths";
  import { useSession } from "$lib/stores/session.svelte";
  import UserAvatar from "./UserAvatar.svelte";

  interface Props {
    /** Whether the sidebar rail is expanded (labels visible). */
    expanded: boolean;
  }

  let { expanded }: Props = $props();

  const session = useSession();

  const roleLabel = $derived(session.isAdmin ? "Administrator" : "Member");
</script>

{#if session.user}
  <DropdownMenu.Root>
    <DropdownMenu.Trigger
      class="group flex h-9 w-full items-center overflow-hidden rounded-sm whitespace-nowrap text-text-muted transition-colors duration-fast hover:bg-surface-2 hover:text-text-primary focus-visible:outline-none focus-visible:shadow-focus-accent data-[state=open]:bg-surface-2"
      title={!expanded ? session.user.displayName : undefined}
      aria-label={`${session.user.displayName} ${roleLabel}`}
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
    </DropdownMenu.Trigger>
    <DropdownMenu.Content side="top" align="start" class="w-60">
      <DropdownMenu.Group>
        <DropdownMenu.Label>
          <span class="block truncate">{session.user.displayName}</span>
          <span class="block text-xs font-normal text-text-muted">{roleLabel}</span>
        </DropdownMenu.Label>
        <DropdownMenu.Separator />
        <DropdownMenu.Item>
          {#snippet child({ props })}
            <a {...props} href={resolve("/account")}><UserRound />Account</a>
          {/snippet}
        </DropdownMenu.Item>
        {#if session.isAdmin}
          <DropdownMenu.Item>
            {#snippet child({ props })}
              <a {...props} href={resolve("/settings/users")}><UsersRound />Manage users</a>
            {/snippet}
          </DropdownMenu.Item>
        {/if}
      </DropdownMenu.Group>
      <DropdownMenu.Separator />
      <DropdownMenu.Group>
        <DropdownMenu.Item onSelect={() => void session.logout()}><LogOut />Sign out</DropdownMenu.Item>
      </DropdownMenu.Group>
    </DropdownMenu.Content>
  </DropdownMenu.Root>
{/if}
