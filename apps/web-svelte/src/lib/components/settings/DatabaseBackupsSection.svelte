<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { Archive, Clock, Database, Loader2, RefreshCw, RotateCcw, ShieldAlert } from "@lucide/svelte";
  import { Badge, Button, Panel, Select, TextInput, cn, type BadgeVariant, type SelectOption } from "@prismedia/ui-svelte";
  import { DATABASE_BACKUP_STATUS } from "$lib/api/generated/codes";
  import {
    createDatabaseBackup,
    fetchDatabaseBackups,
    restoreDatabaseBackup,
    type DatabaseBackup,
    type DatabaseBackupList,
  } from "$lib/api/settings";

  let backupState = $state<DatabaseBackupList | null>(null);
  let loading = $state(true);
  let creating = $state(false);
  let restoring = $state(false);
  let selectedBackupId = $state("");
  let confirmationText = $state("");
  let localError = $state<string | null>(null);
  let localMessage = $state<string | null>(null);

  const completedBackups = $derived(
    backupState?.backups.filter((backup) => backup.status === DATABASE_BACKUP_STATUS.completed) ?? [],
  );
  const selectedBackup = $derived(
    completedBackups.find((backup) => backup.id === selectedBackupId) ?? null,
  );
  const backupOptions = $derived<SelectOption[]>(
    completedBackups.map((backup) => ({
      value: backup.id,
      label: `${backup.isManual ? "Manual" : "Auto"} - ${formatDateTime(backup.completedAt ?? backup.createdAt)} - ${formatBytes(backup.sizeBytes ?? 0)}`,
    })),
  );
  const latestBackup = $derived(
    backupState?.backups.find((backup) => backup.status === DATABASE_BACKUP_STATUS.completed) ?? null,
  );
  const manualCount = $derived(
    backupState?.backups.filter((backup) => backup.isManual && backup.status === DATABASE_BACKUP_STATUS.completed).length ?? 0,
  );
  const canRestore = $derived(
    !!selectedBackup && confirmationText === backupState?.restoreConfirmationText && !restoring,
  );

  onMount(() => {
    void refresh();
  });

  async function refresh() {
    loading = true;
    try {
      const nextState = await fetchDatabaseBackups();
      backupState = nextState;
      localError = null;
      const nextCompleted = nextState.backups.filter(
        (backup) => backup.status === DATABASE_BACKUP_STATUS.completed,
      );
      if (!nextCompleted.some((backup) => backup.id === selectedBackupId)) {
        selectedBackupId = nextCompleted[0]?.id ?? "";
      }
    } catch (err) {
      localError = err instanceof Error ? err.message : "Failed to load database backups";
    } finally {
      loading = false;
    }
  }

  async function handleBackupNow() {
    creating = true;
    localError = null;
    try {
      const backup = await createDatabaseBackup();
      backupState = backupState
        ? { ...backupState, backups: [backup, ...backupState.backups.filter((item) => item.id !== backup.id)] }
        : await fetchDatabaseBackups();
      selectedBackupId = backup.id;
      flash("Backup created.");
    } catch (err) {
      localError = err instanceof Error ? err.message : "Failed to create backup";
    } finally {
      creating = false;
    }
  }

  async function handleRestore() {
    if (!selectedBackup || !backupState) return;
    if (confirmationText !== backupState.restoreConfirmationText) {
      localError = `Type ${backupState.restoreConfirmationText} to confirm restore.`;
      return;
    }

    if (!window.confirm("Restore this backup? This will destroy all current data and restart Prismedia.")) {
      return;
    }

    restoring = true;
    localError = null;
    try {
      await restoreDatabaseBackup(selectedBackup.id, confirmationText);
      await goto("/restore", { replaceState: true });
    } catch (err) {
      localError = err instanceof Error ? err.message : "Failed to schedule restore";
      restoring = false;
    }
  }

  function flash(m: string, ms = 2500) {
    localMessage = m;
    setTimeout(() => {
      if (localMessage === m) localMessage = null;
    }, ms);
  }

  function statusLabel(backup: DatabaseBackup): string {
    if (backup.status === DATABASE_BACKUP_STATUS.completed) return backup.isManual ? "Permanent" : "7-day";
    if (backup.status === DATABASE_BACKUP_STATUS.running) return "Running";
    return "Failed";
  }

  function backupBadgeVariant(backup: DatabaseBackup): BadgeVariant {
    if (backup.status === DATABASE_BACKUP_STATUS.failed) return "error";
    if (backup.status === DATABASE_BACKUP_STATUS.running) return "warning";
    return backup.isManual ? "accent" : "default";
  }

  function formatDateTime(value: string | null | undefined): string {
    if (!value) return "Not yet";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "Not yet";
    return new Intl.DateTimeFormat(undefined, {
      month: "short",
      day: "numeric",
      hour: "numeric",
      minute: "2-digit",
    }).format(date);
  }

  function formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
    const units = ["B", "KB", "MB", "GB", "TB"];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }
    const decimals = unit > 0 && value < 100 ? 1 : 0;
    return `${value.toFixed(decimals)} ${units[unit]}`;
  }
</script>

<Panel>
  <div class="p-5 space-y-5">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div class="flex items-center gap-2.5">
        <Database class="h-4 w-4 text-text-accent" />
        <div>
          <h2 class="text-kicker text-text-primary">Database Backups</h2>
          <p class="text-[0.68rem] text-text-muted">
            Daily retained snapshots and permanent manual restore points
          </p>
        </div>
      </div>
      <Button
        type="button"
        variant="secondary"
        disabled={creating || loading}
        onclick={() => void handleBackupNow()}
        class="gap-2 text-[0.78rem]"
      >
        {#if creating}
          <Loader2 class="h-4 w-4 animate-spin" />
        {:else}
          <Archive class="h-4 w-4" />
        {/if}
        Backup Now
      </Button>
    </div>

    {#if localError}
      <div class="surface-panel border-l-2 border-status-error px-3 py-2 text-[0.78rem] text-status-error-text">
        {localError}
      </div>
    {:else if localMessage}
      <div class="surface-panel border-l-2 border-status-success px-3 py-2 text-[0.78rem] text-status-success-text">
        {localMessage}
      </div>
    {/if}

    <div class="grid gap-3 sm:grid-cols-3">
      <div class="surface-well min-w-0 px-3 py-3">
        <div class="flex items-center gap-2 text-label text-text-muted">
          <Clock class="h-3.5 w-3.5" />
          Latest
        </div>
        <div class="mt-1 truncate text-sm font-medium text-text-primary">
          {loading && !backupState ? "..." : formatDateTime(latestBackup?.completedAt ?? latestBackup?.createdAt)}
        </div>
      </div>
      <div class="surface-well min-w-0 px-3 py-3">
        <div class="flex items-center gap-2 text-label text-text-muted">
          <RefreshCw class="h-3.5 w-3.5" />
          Next Auto
        </div>
        <div class="mt-1 truncate text-sm font-medium text-text-primary">
          {loading && !backupState ? "..." : formatDateTime(backupState?.nextAutomaticBackupAt)}
        </div>
      </div>
      <div class="surface-well min-w-0 px-3 py-3">
        <div class="flex items-center gap-2 text-label text-text-muted">
          <Archive class="h-3.5 w-3.5" />
          Permanent
        </div>
        <div class="mt-1 truncate text-sm font-medium text-text-primary">
          {manualCount} manual
        </div>
      </div>
    </div>

    <div class="surface-well overflow-hidden p-0">
      <div class="flex items-center justify-between border-b border-border-subtle px-3 py-2">
        <div class="text-label text-text-muted">Backup Files</div>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={loading}
          onclick={() => void refresh()}
          class="no-lift gap-1.5 px-2 py-1 text-[0.68rem]"
        >
          <RefreshCw class={cn("h-3.5 w-3.5", loading && "animate-spin")} />
          Refresh
        </Button>
      </div>
      {#if backupState?.backups.length}
        <div class="max-h-56 divide-y divide-border-subtle overflow-y-auto">
          {#each backupState.backups as backup (backup.id)}
            <div class="grid gap-2 px-3 py-2.5 text-[0.76rem] sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
              <div class="min-w-0">
                <div class="truncate font-medium text-text-primary">{backup.fileName}</div>
                <div class="truncate text-[0.66rem] text-text-muted">
                  {formatDateTime(backup.completedAt ?? backup.createdAt)} - {formatBytes(backup.sizeBytes ?? 0)}
                </div>
              </div>
              <Badge
                variant={backupBadgeVariant(backup)}
                class="w-fit"
              >
                {statusLabel(backup)}
              </Badge>
            </div>
          {/each}
        </div>
      {:else}
        <div class="px-3 py-5 text-center text-[0.78rem] text-text-muted">
          {loading ? "Loading backups..." : "No database backups yet."}
        </div>
      {/if}
    </div>

    <div class="surface-well space-y-3 px-4 py-4">
      <div class="flex items-start gap-2 text-status-error-text">
        <ShieldAlert class="mt-0.5 h-4 w-4 shrink-0" />
        <p class="text-[0.76rem] leading-relaxed">
          Restoring will destroy all current data and replace it with the selected backup.
        </p>
      </div>
      <label class="block space-y-1.5">
        <span class="text-label text-text-muted">Restore File</span>
        <Select
          size="sm"
          value={selectedBackupId}
          options={backupOptions}
          placeholder="Select a completed backup"
          disabled={backupOptions.length === 0 || restoring}
          onchange={(value) => {
            selectedBackupId = value;
            confirmationText = "";
          }}
        />
      </label>
      <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
        <label class="block min-w-0 space-y-1.5">
          <span class="text-label text-text-muted">Confirmation</span>
          <TextInput
            size="sm"
            value={confirmationText}
            disabled={!selectedBackup || restoring}
            placeholder={backupState?.restoreConfirmationText ?? ""}
            aria-describedby="database-restore-confirmation-help"
            autocomplete="off"
            oninput={(event) => (confirmationText = event.currentTarget.value)}
          />
          <p id="database-restore-confirmation-help" class="text-[0.66rem] leading-relaxed text-text-muted">
            Type <span class="font-mono text-text-primary">{backupState?.restoreConfirmationText ?? "DESTROY AND RESTORE"}</span>
            exactly to enable restore.
          </p>
        </label>
        <Button
          type="button"
          variant="secondary"
          disabled={!canRestore}
          onclick={() => void handleRestore()}
          class="gap-2 text-status-error-text hover:border-status-error/40"
        >
          {#if restoring}
            <Loader2 class="h-4 w-4 animate-spin" />
          {:else}
            <RotateCcw class="h-4 w-4" />
          {/if}
          Restore
        </Button>
      </div>
    </div>
  </div>
</Panel>
