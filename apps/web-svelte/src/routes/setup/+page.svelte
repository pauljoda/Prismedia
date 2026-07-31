<script lang="ts">
  import { onMount } from "svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { USER_ROLE } from "$lib/api/generated/codes";
  import { fetchSetupStatus, type AuthUser } from "$lib/api/auth";
  import { fetchUsers } from "$lib/api/users";
  import AuthShell from "$lib/components/auth/AuthShell.svelte";
  import SetupAdminStep from "$lib/components/auth/setup/SetupAdminStep.svelte";
  import SetupExistingAccountsStep from "$lib/components/auth/setup/SetupExistingAccountsStep.svelte";

  type Step = "welcome" | "admin" | "existing-accounts" | "done";

  let step = $state<Step>("welcome");
  let hasExistingAccounts = $state(false);
  let existingUsers = $state<AuthUser[]>([]);

  // The wizard advances locally after the admin is created: the needs-setup flip must
  // never eject the user mid-flow, so no navigation happens until "Enter Prismedia".
  const stepNumber = $derived(
    step === "welcome" ? 1 : step === "admin" ? 2 : step === "existing-accounts" ? 3 : hasExistingAccounts ? 4 : 3,
  );
  const stepCount = $derived(hasExistingAccounts ? 4 : 3);

  onMount(() => {
    void fetchSetupStatus()
      .then((status) => (hasExistingAccounts = status.hasUsers))
      .catch(() => {});
  });

  async function onAdminCreated() {
    if (!hasExistingAccounts) {
      step = "done";
      return;
    }

    try {
      // Signed in as the new admin now: list pre-existing member accounts for review.
      existingUsers = (await fetchUsers()).filter((user) => user.role !== USER_ROLE.admin);
      step = existingUsers.length > 0 ? "existing-accounts" : "done";
    } catch {
      step = "done";
    }
  }

  const subtitles: Record<Step, string> = {
    welcome: "A private home for everything you keep.",
    admin: "This account manages users, libraries, and the server.",
    "existing-accounts": "Accounts already stored in this Prismedia database.",
    done: "",
  };
</script>

<svelte:head>
  <title>Set up · Prismedia</title>
</svelte:head>

<AuthShell
  title={step === "welcome"
    ? "Welcome to Prismedia"
    : step === "admin"
      ? "Create the administrator account"
      : step === "existing-accounts"
        ? "Review existing accounts"
        : "The stage is set"}
  subtitle={subtitles[step]}
  wide={step === "existing-accounts"}
>
  <p class="mb-6 text-center font-mono text-[0.65rem] tracking-[0.35em] text-text-disabled">
    {String(stepNumber).padStart(2, "0")} / {String(stepCount).padStart(2, "0")}
  </p>

  {#if step === "welcome"}
    <div class="flex flex-col gap-6 text-center">
      <p class="text-sm leading-relaxed text-text-secondary">
        Set up the administrator account to begin. You can invite the rest of the
        household and shape their libraries afterwards.
      </p>
      <Button variant="primary" size="lg" class="w-full" onclick={() => (step = "admin")}>
        Begin
      </Button>
    </div>
  {:else if step === "admin"}
    <SetupAdminStep {hasExistingAccounts} onComplete={() => void onAdminCreated()} />
  {:else if step === "existing-accounts"}
    <SetupExistingAccountsStep users={existingUsers} onContinue={() => (step = "done")} />
  {:else}
    <div class="flex flex-col gap-6 text-center">
      <p class="text-sm leading-relaxed text-text-secondary">
        Your library is ready. Point Prismedia at your media from Settings, or dive
        straight in.
      </p>
      <Button
        variant="primary"
        size="lg"
        class="w-full"
        onclick={() => window.location.replace("/")}
      >
        Enter Prismedia
      </Button>
    </div>
  {/if}
</AuthShell>
