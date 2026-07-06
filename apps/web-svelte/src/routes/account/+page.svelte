<script lang="ts">
  import { onMount } from "svelte";
  import { Eye, Flame, KeyRound, Loader2, MonitorSmartphone, Shield, UserRound } from "@lucide/svelte";
  import { Button, Panel, cn } from "@prismedia/ui-svelte";
  import type { UserSessionResponse } from "$lib/api/generated/model";
  import { changePassword, fetchOwnSessions, revokeSession, updateProfile } from "$lib/api/auth";
  import TextField from "$lib/components/forms/TextField.svelte";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";
  import UserAvatar from "$lib/components/auth/UserAvatar.svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  const nsfw = useNsfw();

  let displayName = $state(session.user?.displayName ?? "");
  let profileBusy = $state(false);
  let profileMessage = $state<string | null>(null);
  let profileError = $state<string | null>(null);

  let currentPassword = $state("");
  let newPassword = $state("");
  let confirmPassword = $state("");
  let passwordBusy = $state(false);
  let passwordMessage = $state<string | null>(null);
  let passwordError = $state<string | null>(null);

  let sessions = $state<UserSessionResponse[]>([]);
  let sessionsBusy = $state(false);

  const passwordReady = $derived(
    currentPassword.length > 0 && newPassword.length >= 8 && confirmPassword === newPassword,
  );

  onMount(() => {
    void loadSessions();
  });

  async function loadSessions() {
    try {
      sessions = [...(await fetchOwnSessions()).items];
    } catch {
      // Non-fatal: the sessions panel just stays empty.
    }
  }

  async function saveProfile() {
    if (profileBusy || !displayName.trim()) return;
    profileBusy = true;
    profileError = null;
    try {
      await updateProfile(displayName.trim());
      await session.refresh();
      profileMessage = "Profile saved.";
      setTimeout(() => (profileMessage = null), 2500);
    } catch (err) {
      profileError = err instanceof Error ? err.message : "Failed to save profile.";
    } finally {
      profileBusy = false;
    }
  }

  async function submitPasswordChange() {
    if (passwordBusy || !passwordReady) return;
    passwordBusy = true;
    passwordError = null;
    try {
      await changePassword({ currentPassword, newPassword });
      currentPassword = "";
      newPassword = "";
      confirmPassword = "";
      passwordMessage = "Password changed — other devices were signed out.";
      setTimeout(() => (passwordMessage = null), 4000);
      void loadSessions();
    } catch (err) {
      passwordError = err instanceof Error ? err.message : "Failed to change password.";
    } finally {
      passwordBusy = false;
    }
  }

  async function revoke(sessionItem: UserSessionResponse) {
    sessionsBusy = true;
    try {
      await revokeSession(sessionItem.id);
      if (sessionItem.isCurrent) {
        window.location.replace("/login");
        return;
      }
      sessions = sessions.filter((item) => item.id !== sessionItem.id);
    } catch {
      void loadSessions();
    } finally {
      sessionsBusy = false;
    }
  }

  function describeSession(item: UserSessionResponse): string {
    return [item.client, item.deviceName].filter(Boolean).join(" · ") || "Unknown device";
  }
</script>

<svelte:head>
  <title>Account · Prismedia</title>
</svelte:head>

<div class="space-y-8">
  <div>
    <h1 class="flex items-center gap-2.5">
      <UserRound class="h-5 w-5 text-text-accent" />
      Account
    </h1>
    <p class="mt-1 text-[0.78rem] text-text-muted">
      Your profile, password, visibility preference, and signed-in devices
    </p>
  </div>

  <!-- ── Profile ── -->
  <Panel>
    <div class="space-y-5 p-5">
      <div class="flex items-center gap-4">
        <UserAvatar displayName={session.user?.displayName} username={session.user?.username} size="lg" />
        <div>
          <p class="text-sm font-medium text-text-primary">{session.user?.displayName}</p>
          <p class="font-mono text-xs text-text-muted">
            {session.user?.username}
            <span class="ml-2 rounded-xs bg-surface-2 px-1.5 py-0.5 text-[0.6rem] tracking-wide uppercase">
              {session.isAdmin ? "Administrator" : "Member"}
            </span>
          </p>
        </div>
      </div>

      <div class="grid gap-4 sm:max-w-md">
        <TextField label="Display name" value={displayName} onChange={(v) => (displayName = v)} />
        {#if profileError}
          <p class="text-xs text-error" role="alert">{profileError}</p>
        {:else if profileMessage}
          <p class="text-xs text-success">{profileMessage}</p>
        {/if}
        <Button
          variant="secondary"
          class="w-fit"
          disabled={profileBusy || !displayName.trim() || displayName.trim() === session.user?.displayName}
          onclick={() => void saveProfile()}
        >
          {#if profileBusy}<Loader2 class="size-4 animate-spin" />{/if}
          Save profile
        </Button>
      </div>
    </div>
  </Panel>

  <!-- ── Password ── -->
  <Panel>
    <div class="space-y-5 p-5">
      <div class="flex items-center gap-2.5">
        <KeyRound class="h-4 w-4 text-text-accent" />
        <div>
          <h2 class="text-kicker text-text-primary">Password</h2>
          <p class="text-[0.68rem] text-text-muted">
            Changing your password signs out every other device, including Jellyfin apps.
          </p>
        </div>
      </div>
      <form
        class="grid gap-4 sm:max-w-md"
        onsubmit={(event) => {
          event.preventDefault();
          void submitPasswordChange();
        }}
      >
        <PasswordField label="Current password" value={currentPassword} onChange={(v) => (currentPassword = v)} autocomplete="current-password" />
        <PasswordField
          label="New password"
          value={newPassword}
          onChange={(v) => (newPassword = v)}
          autocomplete="new-password"
          error={newPassword.length > 0 && newPassword.length < 8 ? "At least 8 characters." : undefined}
        />
        <PasswordField
          label="Confirm new password"
          value={confirmPassword}
          onChange={(v) => (confirmPassword = v)}
          autocomplete="new-password"
          error={confirmPassword.length > 0 && confirmPassword !== newPassword ? "Passwords do not match." : undefined}
        />
        {#if passwordError}
          <p class="text-xs text-error" role="alert">{passwordError}</p>
        {:else if passwordMessage}
          <p class="text-xs text-success">{passwordMessage}</p>
        {/if}
        <Button type="submit" variant="secondary" class="w-fit" disabled={passwordBusy || !passwordReady}>
          {#if passwordBusy}<Loader2 class="size-4 animate-spin" />{/if}
          Change password
        </Button>
      </form>
    </div>
  </Panel>

  <!-- ── Content visibility (only for accounts allowed NSFW) ── -->
  {#if session.allowNsfw}
    <Panel>
      <div class="space-y-4 p-5">
        <div class="flex items-center gap-2.5">
          <Eye class="h-4 w-4 text-text-accent" />
          <div>
            <h2 class="text-kicker text-text-primary">Content Visibility</h2>
            <p class="text-[0.68rem] text-text-muted">
              Stored in this browser. Does not affect stored data. ⌘⇧Z toggles anywhere.
            </p>
          </div>
        </div>
        <div class="flex max-w-md rounded-sm border border-border-default bg-surface-1 p-1 shadow-well">
          <button
            type="button"
            onclick={() => nsfw.setMode("off")}
            class={cn(
              "flex flex-1 flex-col items-center justify-center gap-1.5 rounded-xs border py-2.5 transition-all duration-fast",
              nsfw.mode === "off"
                ? "border-border-subtle bg-surface-3 text-text-primary shadow-card"
                : "border-transparent text-text-muted hover:bg-surface-2/50 hover:text-text-primary",
            )}
          >
            <Shield class={cn("h-4 w-4", nsfw.mode === "off" && "text-info-text")} />
            <span class="text-[0.75rem] font-medium">Off (SFW)</span>
          </button>
          <button
            type="button"
            onclick={() => nsfw.setMode("show")}
            class={cn(
              "flex flex-1 flex-col items-center justify-center gap-1.5 rounded-xs border py-2.5 transition-all duration-fast",
              nsfw.mode === "show"
                ? "border-border-accent bg-surface-3 text-accent-400 shadow-[var(--shadow-glow-accent)]"
                : "border-transparent text-text-muted hover:bg-surface-2/50 hover:text-text-primary",
            )}
          >
            <Flame class={cn("h-4 w-4", nsfw.mode === "show" && "text-accent-500")} />
            <span class="text-[0.75rem] font-medium">Show</span>
          </button>
        </div>
      </div>
    </Panel>
  {/if}

  <!-- ── Devices ── -->
  <Panel>
    <div class="space-y-4 p-5">
      <div class="flex items-center gap-2.5">
        <MonitorSmartphone class="h-4 w-4 text-text-accent" />
        <div>
          <h2 class="text-kicker text-text-primary">Devices</h2>
          <p class="text-[0.68rem] text-text-muted">
            Everywhere this account is signed in — browsers, Jellyfin apps, and readers.
          </p>
        </div>
      </div>
      <div class="surface-well divide-y divide-border-subtle px-4">
        {#each sessions as item (item.id)}
          <div class="flex items-center gap-3 py-3">
            <div class="min-w-0 flex-1">
              <p class="truncate text-sm text-text-primary">
                {describeSession(item)}
                {#if item.isCurrent}
                  <span class="ml-2 rounded-xs bg-surface-2 px-1.5 py-0.5 font-mono text-[0.6rem] tracking-wide text-text-accent uppercase">
                    This device
                  </span>
                {/if}
              </p>
              <p class="font-mono text-[0.68rem] text-text-muted">
                Last active {new Date(item.lastSeenAt).toLocaleString()}
              </p>
            </div>
            <Button
              variant="ghost"
              size="sm"
              disabled={sessionsBusy}
              onclick={() => void revoke(item)}
            >
              {item.isCurrent ? "Sign out" : "Revoke"}
            </Button>
          </div>
        {:else}
          <p class="py-4 text-xs text-text-disabled">No active sessions found.</p>
        {/each}
      </div>
    </div>
  </Panel>
</div>
