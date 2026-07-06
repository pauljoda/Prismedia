<script lang="ts">
  import { onMount } from "svelte";
  import { Flame, FolderPlus, KeyRound, Pencil, ShieldUser, Trash2, UserPlus, UsersRound } from "@lucide/svelte";
  import { Badge, Button, Panel, StatusLed, cn } from "@prismedia/ui-svelte";
  import { USER_ROLE } from "$lib/api/generated/codes";
  import type { LibraryRoot, UserResponse } from "$lib/api/generated/model";
  import { listLibraryRoots } from "$lib/api/generated/prismedia";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import { deleteUser, fetchUsers, updateUser } from "$lib/api/users";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import UserAvatar from "$lib/components/auth/UserAvatar.svelte";
  import UserEditDialog from "$lib/components/settings/users/UserEditDialog.svelte";
  import UserPasswordDialog from "$lib/components/settings/users/UserPasswordDialog.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();

  let users = $state<UserResponse[]>([]);
  let libraries = $state<LibraryRoot[]>([]);
  let loading = $state(true);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);

  let editDialogOpen = $state(false);
  let editTarget = $state<UserResponse | null>(null);
  let passwordTarget = $state<UserResponse | null>(null);

  onMount(() => {
    void load();
  });

  async function load() {
    loading = true;
    try {
      const [userList, rootList] = await Promise.all([
        fetchUsers(),
        listLibraryRoots().then((response) =>
          unwrapGenerated<LibraryRoot[]>(response, "Failed to load libraries"),
        ),
      ]);
      users = userList;
      libraries = rootList;
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load users";
    } finally {
      loading = false;
    }
  }

  function flash(text: string) {
    message = text;
    setTimeout(() => {
      if (message === text) message = null;
    }, 2500);
  }

  function openCreate() {
    editTarget = null;
    editDialogOpen = true;
  }

  function openEdit(user: UserResponse) {
    editTarget = user;
    editDialogOpen = true;
  }

  function onSaved(saved: UserResponse) {
    editDialogOpen = false;
    const existing = users.some((user) => user.id === saved.id);
    users = existing
      ? users.map((user) => (user.id === saved.id ? saved : user))
      : [...users, saved].sort((a, b) => a.username.localeCompare(b.username));
    flash(existing ? "User saved." : "User created.");
    if (saved.id === session.user?.id) void session.refresh();
  }

  async function toggleEnabled(user: UserResponse) {
    busy = true;
    error = null;
    try {
      const updated = await updateUser(user.id, { enabled: !user.enabled });
      users = users.map((item) =>
        item.id === user.id ? { ...updated, libraryRootIds: item.libraryRootIds } : item,
      );
      flash(updated.enabled ? "User enabled." : "User disabled — sessions signed out.");
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to update the user";
    } finally {
      busy = false;
    }
  }

  async function removeUser(user: UserResponse) {
    const isAdminTarget = user.role === USER_ROLE.admin;
    if (isAdminTarget) {
      const typed = window.prompt(
        `Deleting the administrator "${user.username}" removes their watch history and access. Type the username to confirm:`,
      );
      if (typed !== user.username) return;
    } else if (!window.confirm(`Delete "${user.username}"? Their watch history and favorites are removed.`)) {
      return;
    }

    busy = true;
    error = null;
    try {
      await deleteUser(user.id);
      users = users.filter((item) => item.id !== user.id);
      flash("User deleted.");
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to delete the user";
    } finally {
      busy = false;
    }
  }

  function accessSummary(user: UserResponse): string {
    if (user.role === USER_ROLE.admin) return "All libraries";
    const count = user.libraryRootIds?.length ?? 0;
    if (count === 0) return "No libraries";
    if (count === libraries.length && libraries.length > 0) return "All libraries";
    return `${count} ${count === 1 ? "library" : "libraries"}`;
  }
</script>

<svelte:head>
  <title>Users · Settings · Prismedia</title>
</svelte:head>

{#if !session.isAdmin}
  <StatePlaceholder icon={ShieldUser} title="Administrator access required" description="Ask an administrator to manage user accounts." />
{:else}
  <div class="space-y-6">
    <div class="flex flex-wrap items-end justify-between gap-3">
      <div>
        <BackLink fallback="/settings" label="Settings" variant="text" />
        <h1 class="mt-1 flex items-center gap-2.5">
          <UsersRound class="h-5 w-5 text-text-accent" />
          Users
        </h1>
        <p class="mt-1 text-[0.78rem] text-text-muted">
          Accounts for the web app, Jellyfin clients, and OPDS readers
        </p>
      </div>
      <Button variant="primary" onclick={openCreate}>
        <UserPlus class="size-4" />
        Add user
      </Button>
    </div>

    {#if error}
      <div class="surface-panel border-l-2 border-status-error px-4 py-2.5 text-sm text-status-error-text">{error}</div>
    {/if}
    {#if message && !error}
      <div class="surface-panel border-l-2 border-status-success px-4 py-2.5 text-sm text-status-success-text">{message}</div>
    {/if}

    <Panel>
      <div class="divide-y divide-border-subtle">
        {#if loading}
          <StatePlaceholder icon={UsersRound} title="Loading users" busy />
        {:else}
          {#each users as user (user.id)}
            {@const isSelf = user.id === session.user?.id}
            <div class={cn("flex flex-wrap items-center gap-3 px-5 py-4", !user.enabled && "opacity-50")}>
              <UserAvatar displayName={user.displayName} username={user.username} size="md" />
              <div class="min-w-0 flex-1">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="truncate text-sm font-medium text-text-primary">{user.displayName}</span>
                  {#if user.role === USER_ROLE.admin}
                    <Badge variant="warning">Admin</Badge>
                  {/if}
                  {#if user.allowNsfw}
                    <span title="NSFW allowed"><Flame class="size-3.5 text-warning" /></span>
                  {/if}
                  {#if user.canCreateLibraries && user.role !== USER_ROLE.admin}
                    <span title="Can create libraries"><FolderPlus class="size-3.5 text-text-muted" /></span>
                  {/if}
                </div>
                <p class="flex flex-wrap items-center gap-x-2 font-mono text-[0.68rem] text-text-muted">
                  <span class="truncate">{user.username}</span>
                  <span aria-hidden="true">·</span>
                  <span>{accessSummary(user)}</span>
                  {#if user.lastLoginAt}
                    <span aria-hidden="true">·</span>
                    <span>last sign-in {new Date(user.lastLoginAt).toLocaleDateString()}</span>
                  {/if}
                </p>
              </div>
              <div class="flex items-center gap-1">
                <StatusLed status={user.enabled ? "active" : "idle"} size="sm" />
                <Button variant="ghost" size="icon" aria-label={`Edit ${user.username}`} disabled={busy} onclick={() => openEdit(user)}>
                  <Pencil class="size-4" />
                </Button>
                <Button variant="ghost" size="icon" aria-label={`Reset password for ${user.username}`} disabled={busy} onclick={() => (passwordTarget = user)}>
                  <KeyRound class="size-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={busy || isSelf}
                  title={isSelf ? "You cannot disable your own account" : undefined}
                  onclick={() => void toggleEnabled(user)}
                >
                  {user.enabled ? "Disable" : "Enable"}
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Delete ${user.username}`}
                  disabled={busy || isSelf}
                  title={isSelf ? "You cannot delete your own account" : undefined}
                  class="text-status-error-text hover:bg-error-muted/20"
                  onclick={() => void removeUser(user)}
                >
                  <Trash2 class="size-4" />
                </Button>
              </div>
            </div>
          {:else}
            <StatePlaceholder icon={UsersRound} title="No users yet" description="Add the first account to share this library." />
          {/each}
        {/if}
      </div>
    </Panel>
  </div>

  <UserEditDialog
    open={editDialogOpen}
    user={editTarget}
    {libraries}
    isSelf={editTarget?.id === session.user?.id}
    onSaved={onSaved}
    onClose={() => (editDialogOpen = false)}
  />
  <UserPasswordDialog
    open={passwordTarget !== null}
    user={passwordTarget}
    onDone={() => {
      passwordTarget = null;
      flash("Password reset — the user was signed out everywhere.");
    }}
    onClose={() => (passwordTarget = null)}
  />
{/if}
