<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { KeyRound, Loader2, UserRound } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import AuthShell from "$lib/components/auth/AuthShell.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";
  import { fetchSetupStatusWithRetry, login } from "$lib/api/auth";
  import { ApiError } from "$lib/api/orval-fetch";

  let username = $state("");
  let password = $state("");
  let pending = $state(false);
  let error = $state<string | null>(null);

  const expired = page.url.searchParams.get("expired") === "1";

  // Self-healing net: however this page was reached (including a boot-time failure that
  // made the root guard fall through), a fresh install must end up on the setup wizard.
  onMount(() => {
    void fetchSetupStatusWithRetry().then((setup) => {
      if (setup?.needsSetup) {
        window.location.replace("/setup");
      }
    });
  });

  function safeReturnTo(): string {
    const value = page.url.searchParams.get("returnTo");
    return value && value.startsWith("/") && !value.startsWith("//") ? value : "/";
  }

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    if (pending) return;
    error = null;
    pending = true;
    try {
      await login({ username, password });
      // Full navigation re-boots the app through the root guard with a fresh session.
      window.location.replace(safeReturnTo());
    } catch (err) {
      pending = false;
      if (err instanceof ApiError && err.status === 429) {
        error = "Too many attempts — wait a moment and try again.";
      } else if (err instanceof ApiError && err.status === 401) {
        error = "Incorrect username or password.";
      } else {
        error = err instanceof Error ? err.message : "Sign-in failed.";
      }
    }
  }
</script>

<svelte:head>
  <title>Sign in · Prismedia</title>
</svelte:head>

<AuthShell title="Sign in to your library">
  {#if expired}
    <p
      class="mb-5 rounded-xs border border-border-subtle bg-surface-2/70 px-3 py-2 text-center text-xs text-text-secondary"
    >
      Your session ended — sign in to continue.
    </p>
  {/if}

  <form onsubmit={submit} class="flex flex-col gap-4">
    <TextField
      label="Username"
      icon={UserRound}
      value={username}
      onChange={(value) => (username = value)}
      autocomplete="username"
      required
    />
    <PasswordField
      label="Password"
      icon={KeyRound}
      value={password}
      onChange={(value) => (password = value)}
      autocomplete="current-password"
      required
    />

    {#if error}
      <p class="rounded-xs border border-error/40 bg-error/10 px-3 py-2 text-xs text-error" role="alert">
        {error}
      </p>
    {/if}

    <Button type="submit" variant="primary" size="lg" class="mt-2 w-full" disabled={pending || !username || !password}>
      {#if pending}
        <Loader2 class="size-4 animate-spin" />
        Signing in…
      {:else}
        Sign in
      {/if}
    </Button>
  </form>

  <!-- The no-lockout escape hatch must be discoverable from the door itself: resetting an
       administrator requires host access (an env var + restart), so surfacing it here gives
       away nothing while making recovery findable without reading the docs first. -->
  <p class="mt-6 text-center font-mono text-[0.65rem] leading-relaxed text-text-disabled">
    Locked out? Set the <span class="text-text-muted">PRISMEDIA_RECOVERY_PASSWORD</span> environment
    variable and restart to reset an administrator —
    <a
      href="https://pauljoda.github.io/Prismedia/docs/deployment/authentication#password-recovery"
      target="_blank"
      rel="noreferrer"
      class="underline decoration-border-subtle underline-offset-2 hover:text-text-secondary"
    >password recovery</a>.
  </p>
</AuthShell>
