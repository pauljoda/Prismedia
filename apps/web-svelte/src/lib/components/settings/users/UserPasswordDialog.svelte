<script lang="ts">
  import { Loader2 } from "@lucide/svelte";
  import { Button, fadeIn, flyUp } from "@prismedia/ui-svelte";
  import type { UserResponse } from "$lib/api/generated/model";
  import { resetUserPassword } from "$lib/api/users";
  import PasswordField from "$lib/components/forms/PasswordField.svelte";

  interface Props {
    open: boolean;
    user: UserResponse | null;
    onDone: () => void;
    onClose: () => void;
  }

  let { open, user, onDone, onClose }: Props = $props();

  let newPassword = $state("");
  let confirm = $state("");
  let saving = $state(false);
  let error = $state<string | null>(null);

  $effect(() => {
    if (!open) return;
    newPassword = "";
    confirm = "";
    error = null;
  });

  const ready = $derived(newPassword.length >= 8 && confirm === newPassword);

  async function save() {
    if (saving || !ready || !user) return;
    saving = true;
    error = null;
    try {
      await resetUserPassword(user.id, newPassword);
      onDone();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to reset the password.";
    } finally {
      saving = false;
    }
  }
</script>

{#if open && user}
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
      aria-label={`Reset password for ${user.username}`}
      class="app-dialog-surface relative z-10 w-full max-w-sm overflow-hidden"
      transition:flyUp
    >
      <div class="border-b border-border-subtle px-5 py-4">
        <h2 class="text-kicker text-text-primary">Reset password</h2>
        <p class="text-[0.68rem] text-text-muted">
          {user.username} is signed out everywhere once the new password applies.
        </p>
      </div>
      <div class="space-y-4 px-5 py-4">
        <PasswordField
          label="New password"
          value={newPassword}
          onChange={(v) => (newPassword = v)}
          autocomplete="new-password"
          error={newPassword.length > 0 && newPassword.length < 8 ? "At least 8 characters." : undefined}
        />
        <PasswordField
          label="Confirm"
          value={confirm}
          onChange={(v) => (confirm = v)}
          autocomplete="new-password"
          error={confirm.length > 0 && confirm !== newPassword ? "Passwords do not match." : undefined}
        />
        {#if error}
          <p class="rounded-xs border border-error/40 bg-error/10 px-3 py-2 text-xs text-error" role="alert">{error}</p>
        {/if}
      </div>
      <div class="flex justify-end gap-2 border-t border-border-subtle px-5 py-3">
        <Button variant="ghost" onclick={onClose} disabled={saving}>Cancel</Button>
        <Button variant="primary" onclick={() => void save()} disabled={saving || !ready}>
          {#if saving}
            <Loader2 class="size-4 animate-spin" />
          {/if}
          Reset password
        </Button>
      </div>
    </div>
  </div>
{/if}
