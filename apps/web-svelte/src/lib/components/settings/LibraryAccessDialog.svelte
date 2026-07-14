<script lang="ts">
  import { Loader2, UsersRound } from "@lucide/svelte";
  import { Badge, Button, Checkbox, fadeIn, flyUp } from "@prismedia/ui-svelte";
  import { USER_ROLE } from "$lib/api/generated/codes";
  import type { LibraryRoot as GeneratedLibraryRoot, UserResponse } from "$lib/api/generated/model";
  import { listLibraryRoots, replaceLibraryAccess } from "$lib/api/generated/prismedia";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import { fetchUsers } from "$lib/api/users";
  import UserAvatar from "$lib/components/auth/UserAvatar.svelte";

  interface Props {
    open: boolean;
    rootId: string;
    rootLabel: string;
    onSaved: () => void;
    onClose: () => void;
  }

  let { open, rootId, rootLabel, onSaved, onClose }: Props = $props();

  let members = $state<UserResponse[]>([]);
  let grantedIds = $state<string[]>([]);
  let isNsfwRoot = $state(false);
  let hiddenBlockedMembers = $state(0);
  let loading = $state(true);
  let saving = $state(false);
  let error = $state<string | null>(null);

  $effect(() => {
    if (!open) return;
    loading = true;
    error = null;
    void Promise.all([
      fetchUsers(),
      listLibraryRoots().then((response) =>
        unwrapGenerated<GeneratedLibraryRoot[]>(response, "Failed to load library access"),
      ),
    ])
      .then(([users, roots]) => {
        const root = roots.find((candidate) => candidate.id === rootId);
        isNsfwRoot = root?.isNsfw ?? false;
        const nonAdmins = users.filter((user) => user.role !== USER_ROLE.admin);
        // The NSFW wall: an NSFW library can only be granted to members whose account allows NSFW
        // content (the server enforces the same rule on save).
        members = isNsfwRoot ? nonAdmins.filter((user) => user.allowNsfw) : nonAdmins;
        hiddenBlockedMembers = nonAdmins.length - members.length;
        grantedIds = [...(root?.accessUserIds ?? [])];
      })
      .catch((err) => {
        error = err instanceof Error ? err.message : "Failed to load library access";
      })
      .finally(() => (loading = false));
  });

  function toggle(userId: string, checked: boolean) {
    grantedIds = checked ? [...grantedIds, userId] : grantedIds.filter((id) => id !== userId);
  }

  async function save() {
    if (saving) return;
    saving = true;
    error = null;
    try {
      await replaceLibraryAccess(rootId, { userIds: grantedIds });
      onSaved();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to save library access";
    } finally {
      saving = false;
    }
  }
</script>

{#if open}
  <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
    <button
      type="button"
      class="app-overlay-backdrop absolute inset-0"
      aria-label="Close"
      onclick={onClose}
      transition:fadeIn
    ></button>
    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Library access for ${rootLabel}`}
      class="app-dialog-surface relative z-10 flex max-h-[80dvh] w-full max-w-sm flex-col overflow-hidden"
      transition:flyUp
    >
      <div class="border-b border-border-subtle px-5 py-4">
        <h2 class="text-kicker text-text-primary">Library access</h2>
        <p class="text-[0.68rem] text-text-muted">
          Members who can see “{rootLabel}”. Administrators always have access.
        </p>
      </div>

      <div class="flex-1 overflow-y-auto px-5 py-3">
        {#if loading}
          <p class="py-4 text-center text-xs text-text-disabled">Loading…</p>
        {:else if members.length === 0}
          <div class="flex flex-col items-center gap-1 py-6 text-center">
            <UsersRound class="size-5 text-text-disabled" />
            <p class="text-xs text-text-muted">No member accounts yet — only admins can see this library.</p>
          </div>
        {:else}
          <div class="divide-y divide-border-subtle">
            {#each members as member (member.id)}
              <label class="flex cursor-pointer items-center gap-3 py-2.5">
                <Checkbox
                  checked={grantedIds.includes(member.id)}
                  onchange={(event) =>
                    toggle(member.id, (event.currentTarget as HTMLInputElement).checked)}
                />
                <UserAvatar displayName={member.displayName} username={member.username} />
                <span class="min-w-0 flex-1 truncate text-sm text-text-secondary">{member.displayName}</span>
                {#if !member.enabled}
                  <Badge variant="error">Disabled</Badge>
                {/if}
              </label>
            {/each}
          </div>
        {/if}
        {#if !loading && isNsfwRoot && hiddenBlockedMembers > 0}
          <p class="mt-2 text-[0.68rem] text-text-disabled">
            {hiddenBlockedMembers} member{hiddenBlockedMembers === 1 ? "" : "s"} whose account blocks NSFW
            content cannot be granted this library.
          </p>
        {/if}
        {#if error}
          <p class="mt-2 rounded-xs border border-error/40 bg-error/10 px-3 py-2 text-xs text-error" role="alert">{error}</p>
        {/if}
      </div>

      <div class="flex justify-end gap-2 border-t border-border-subtle px-5 py-3">
        <Button variant="ghost" onclick={onClose} disabled={saving}>Cancel</Button>
        <Button variant="primary" onclick={() => void save()} disabled={saving || loading}>
          {#if saving}<Loader2 class="size-4 animate-spin" />{/if}
          Save access
        </Button>
      </div>
    </div>
  </div>
{/if}
