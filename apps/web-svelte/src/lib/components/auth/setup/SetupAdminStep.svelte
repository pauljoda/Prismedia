<script lang="ts">
  import { KeyRound, Loader2, UserRound } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";
  import { login, submitSetup } from "$lib/api/auth";

  interface Props {
    /** True when accounts already exist and the username may match one to promote. */
    hasExistingAccounts: boolean;
    onComplete: () => void;
  }

  let { hasExistingAccounts, onComplete }: Props = $props();

  let username = $state("");
  let displayName = $state("");
  let password = $state("");
  let confirm = $state("");
  let pending = $state(false);
  let error = $state<string | null>(null);

  const passwordTooShort = $derived(password.length > 0 && password.length < 8);
  const mismatch = $derived(confirm.length > 0 && confirm !== password);
  const ready = $derived(
    username.trim().length > 0 && password.length >= 8 && confirm === password,
  );

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    if (pending || !ready) return;
    error = null;
    pending = true;
    try {
      await submitSetup({
        username: username.trim(),
        password,
        displayName: displayName.trim() || null,
      });
      onComplete();
    } catch (setupError) {
      // The setup response normally signs the admin in; fall back to an explicit
      // login in case the cookie was not applied (e.g. exotic proxy setups).
      try {
        await login({ username: username.trim(), password });
        onComplete();
      } catch {
        pending = false;
        error = setupError instanceof Error ? setupError.message : "Setup failed.";
      }
    }
  }
</script>

<form onsubmit={submit} class="flex flex-col gap-4">
  <TextField
    label="Username"
    icon={UserRound}
    value={username}
    onChange={(value) => (username = value)}
    autocomplete="username"
    helper={hasExistingAccounts
      ? "Using an existing account's username promotes it to administrator."
      : undefined}
    required
  />
  <TextField
    label="Display name"
    value={displayName}
    onChange={(value) => (displayName = value)}
    placeholder="Optional"
  />
  <PasswordField
    label="Password"
    icon={KeyRound}
    value={password}
    onChange={(value) => (password = value)}
    autocomplete="new-password"
    error={passwordTooShort ? "At least 8 characters." : undefined}
    required
  />
  <PasswordField
    label="Confirm password"
    value={confirm}
    onChange={(value) => (confirm = value)}
    autocomplete="new-password"
    error={mismatch ? "Passwords do not match." : undefined}
    required
  />

  {#if error}
    <p class="rounded-xs border border-error/40 bg-error/10 px-3 py-2 text-xs text-error" role="alert">
      {error}
    </p>
  {/if}

  <Button type="submit" variant="primary" size="lg" class="mt-2 w-full" disabled={pending || !ready}>
    {#if pending}
      <Loader2 class="size-4 animate-spin" />
      Creating account…
    {:else}
      Create administrator
    {/if}
  </Button>
</form>
