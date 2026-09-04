<script lang="ts">
  import { Check, ChevronDown, Loader2 } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";
  import UserAvatar from "$lib/components/auth/UserAvatar.svelte";
  import { resetUserPassword } from "$lib/api/users";
  import type { AuthUser } from "$lib/api/auth";

  interface Props {
    users: AuthUser[];
    onContinue: () => void;
  }

  let { users, onContinue }: Props = $props();

  let expandedId = $state<string | null>(null);
  let newPassword = $state("");
  let confirm = $state("");
  let pending = $state(false);
  let error = $state<string | null>(null);
  let resetIds = $state<string[]>([]);

  function toggle(userId: string) {
    expandedId = expandedId === userId ? null : userId;
    newPassword = "";
    confirm = "";
    error = null;
  }

  async function applyReset(userId: string) {
    if (pending || newPassword.length < 8 || newPassword !== confirm) return;
    pending = true;
    error = null;
    try {
      await resetUserPassword(userId, newPassword);
      resetIds = [...resetIds, userId];
      expandedId = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to reset the password.";
    } finally {
      pending = false;
    }
  }
</script>

<div class="flex flex-col gap-4">
  <p class="text-sm text-text-secondary">
    These accounts already existed in this Prismedia database. Review them and set a
    known password for anyone who still needs access.
  </p>

  <ul class="flex flex-col gap-2">
    {#each users as user (user.id)}
      <li class="rounded-sm border border-border-subtle bg-surface-2/60">
        <div class="flex items-center gap-3 px-3 py-2.5">
          <UserAvatar displayName={user.displayName} username={user.username} size="md" />
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm text-text-primary">{user.displayName}</p>
            <p class="truncate font-mono text-xs text-text-disabled">{user.username}</p>
          </div>
          {#if user.allowNsfw}
            <Badge>NSFW</Badge>
          {/if}
          {#if resetIds.includes(user.id)}
            <span class="flex items-center gap-1 text-xs text-success">
              <Check class="size-3.5" />
              Password set
            </span>
          {:else}
            <Button variant="outline" size="sm"
              type="button"
              class="flex items-center gap-1 text-xs"
              onclick={() => toggle(user.id)}
              aria-expanded={expandedId === user.id}
            >
              Set new password
              <ChevronDown
                class={["size-3.5 transition-transform", expandedId === user.id && "rotate-180"]}
              />
            </Button>
          {/if}
        </div>
        {#if expandedId === user.id}
          <div class="flex flex-col gap-3 border-t border-border-subtle px-3 py-3">
            <PasswordField
              label="New password"
              value={newPassword}
              onChange={(value) => (newPassword = value)}
              autocomplete="new-password"
              error={newPassword.length > 0 && newPassword.length < 8 ? "At least 8 characters." : undefined}
            />
            <PasswordField
              label="Confirm"
              value={confirm}
              onChange={(value) => (confirm = value)}
              autocomplete="new-password"
              error={confirm.length > 0 && confirm !== newPassword ? "Passwords do not match." : undefined}
            />
            {#if error}
              <p class="text-xs text-error" role="alert">{error}</p>
            {/if}
            <Button
              variant="secondary"
              size="sm"
              disabled={pending || newPassword.length < 8 || newPassword !== confirm}
              onclick={() => void applyReset(user.id)}
            >
              {#if pending}
                <Loader2 class="size-4 animate-spin" />
              {/if}
              Apply
            </Button>
          </div>
        {/if}
      </li>
    {/each}
  </ul>

  <Button variant="primary" size="lg" class="w-full" onclick={onContinue}>Continue</Button>
</div>
