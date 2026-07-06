<script lang="ts">
  import { Flame, FolderPlus, Loader2 } from "@lucide/svelte";
  import { Button, Checkbox, Select, cn, flyUp, fadeIn } from "@prismedia/ui-svelte";
  import { USER_ROLE, type UserRoleCode } from "$lib/api/generated/codes";
  import type { LibraryRoot, UserResponse } from "$lib/api/generated/model";
  import { createUser, replaceLibraryAccessForUser, updateUser } from "$lib/api/users";
  import TextField from "$lib/components/forms/TextField.svelte";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";
  import ToggleChip from "$lib/components/forms/ToggleChip.svelte";

  interface Props {
    open: boolean;
    /** Null creates a new account; a user edits that account. */
    user: UserResponse | null;
    /** Admin's full library list for the access picker. */
    libraries: LibraryRoot[];
    /** True when editing the signed-in admin (role/enabled stay locked). */
    isSelf?: boolean;
    onSaved: (user: UserResponse) => void;
    onClose: () => void;
  }

  let { open, user, libraries, isSelf = false, onSaved, onClose }: Props = $props();

  let username = $state("");
  let displayName = $state("");
  let password = $state("");
  let role = $state<UserRoleCode>(USER_ROLE.member);
  let allowNsfw = $state(false);
  let canCreateLibraries = $state(false);
  let enabled = $state(true);
  let libraryRootIds = $state<string[]>([]);
  let saving = $state(false);
  let error = $state<string | null>(null);

  const isCreate = $derived(user === null);
  const isAdminRole = $derived(role === USER_ROLE.admin);
  const ready = $derived(
    username.trim().length > 0 && (!isCreate || password.length >= 8),
  );

  $effect(() => {
    if (!open) return;
    username = user?.username ?? "";
    displayName = user?.displayName ?? "";
    password = "";
    role = (user?.role as UserRoleCode) ?? USER_ROLE.member;
    allowNsfw = user?.allowNsfw ?? false;
    canCreateLibraries = user?.canCreateLibraries ?? false;
    enabled = user?.enabled ?? true;
    libraryRootIds = [...(user?.libraryRootIds ?? [])];
    error = null;
  });

  function toggleLibrary(rootId: string, checked: boolean) {
    libraryRootIds = checked
      ? [...libraryRootIds, rootId]
      : libraryRootIds.filter((id) => id !== rootId);
  }

  async function save() {
    if (saving || !ready) return;
    saving = true;
    error = null;
    try {
      let saved: UserResponse;
      if (isCreate) {
        saved = await createUser({
          username: username.trim(),
          password,
          displayName: displayName.trim() || null,
          role,
          allowNsfw,
          canCreateLibraries,
          enabled,
        });
      } else {
        saved = await updateUser(user!.id, {
          username: username.trim() !== user!.username ? username.trim() : null,
          displayName: displayName.trim() || null,
          role: isSelf ? undefined : role,
          allowNsfw,
          canCreateLibraries,
          enabled: isSelf ? null : enabled,
        });
      }

      // Admins bypass grants entirely; only members carry access rows.
      if (!isAdminRole) {
        await replaceLibraryAccessForUser(saved.id, libraryRootIds);
      }

      onSaved({ ...saved, libraryRootIds: isAdminRole ? [] : libraryRootIds });
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to save the user.";
    } finally {
      saving = false;
    }
  }
</script>

{#if open}
  <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
    <button
      type="button"
      class="absolute inset-0 bg-black/80 backdrop-blur-sm"
      aria-label="Close"
      onclick={onClose}
      transition:fadeIn
    ></button>
    <div
      role="dialog"
      aria-modal="true"
      aria-label={isCreate ? "Add user" : `Edit ${user?.username}`}
      class="surface-elevated relative z-10 flex max-h-[85dvh] w-full max-w-lg flex-col overflow-hidden rounded-lg border border-border-subtle"
      transition:flyUp
    >
      <div class="border-b border-border-subtle px-5 py-4">
        <h2 class="text-kicker text-text-primary">{isCreate ? "Add user" : "Edit user"}</h2>
        <p class="text-[0.68rem] text-text-muted">
          {isCreate
            ? "The same credentials work in the web app, Jellyfin clients, and OPDS readers."
            : user?.username}
        </p>
      </div>

      <div class="flex-1 space-y-4 overflow-y-auto px-5 py-4">
        <div class="grid gap-4 sm:grid-cols-2">
          <TextField label="Username" value={username} onChange={(v) => (username = v)} required />
          <TextField label="Display name" value={displayName} onChange={(v) => (displayName = v)} placeholder="Optional" />
        </div>

        {#if isCreate}
          <PasswordField
            label="Password"
            value={password}
            onChange={(v) => (password = v)}
            autocomplete="new-password"
            error={password.length > 0 && password.length < 8 ? "At least 8 characters." : undefined}
            required
          />
        {/if}

        <div class="grid gap-4 sm:grid-cols-2">
          <label class="flex flex-col gap-1.5">
            <span class="text-label text-text-muted">Role</span>
            <Select
              options={[
                { value: USER_ROLE.member, label: "Member" },
                { value: USER_ROLE.admin, label: "Administrator" },
              ]}
              value={role}
              disabled={isSelf}
              onchange={(value) => (role = value as UserRoleCode)}
            />
          </label>
          <div class="flex flex-wrap items-end gap-2 pb-0.5">
            <ToggleChip value={allowNsfw} onChange={(v) => (allowNsfw = v)} onLabel="NSFW allowed" offLabel="NSFW blocked" icon={Flame} variant="warning" />
            <ToggleChip value={canCreateLibraries} onChange={(v) => (canCreateLibraries = v)} onLabel="Creates libraries" offLabel="No libraries" icon={FolderPlus} />
            {#if !isSelf}
              <ToggleChip value={enabled} onChange={(v) => (enabled = v)} onLabel="Enabled" offLabel="Disabled" />
            {/if}
          </div>
        </div>

        {#if isAdminRole}
          <p class="rounded-xs border border-border-subtle bg-surface-2/70 px-3 py-2 text-xs text-text-muted">
            Administrators always see every library.
          </p>
        {:else}
          <div class="space-y-1.5">
            <span class="text-label text-text-muted">Library access</span>
            <div class="surface-well divide-y divide-border-subtle px-3">
              {#each libraries as library (library.id)}
                <label class="flex cursor-pointer items-center gap-2.5 py-2 text-sm text-text-secondary">
                  <Checkbox
                    checked={libraryRootIds.includes(library.id)}
                    onchange={(event) =>
                      toggleLibrary(library.id, (event.currentTarget as HTMLInputElement).checked)}
                  />
                  <span class="flex-1 truncate">{library.label}</span>
                  {#if library.isNsfw}
                    <span class="font-mono text-[0.6rem] tracking-wide text-warning uppercase">NSFW</span>
                  {/if}
                </label>
              {:else}
                <p class="py-3 text-xs text-text-disabled">No libraries yet.</p>
              {/each}
            </div>
          </div>
        {/if}

        {#if error}
          <p class="rounded-xs border border-error/40 bg-error/10 px-3 py-2 text-xs text-error" role="alert">{error}</p>
        {/if}
      </div>

      <div class={cn("flex justify-end gap-2 border-t border-border-subtle px-5 py-3")}>
        <Button variant="ghost" onclick={onClose} disabled={saving}>Cancel</Button>
        <Button variant="primary" onclick={() => void save()} disabled={saving || !ready}>
          {#if saving}
            <Loader2 class="size-4 animate-spin" />
          {/if}
          {isCreate ? "Create user" : "Save changes"}
        </Button>
      </div>
    </div>
  </div>
{/if}
