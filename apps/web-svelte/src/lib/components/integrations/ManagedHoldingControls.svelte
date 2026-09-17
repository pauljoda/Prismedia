<script lang="ts">
  import { onMount } from "svelte";
  import { Clock3, Settings2 } from "@lucide/svelte";
  import { Alert, Badge, Button, Checkbox, DialogBase, Disclosure, Select, Toggle } from "@prismedia/ui-svelte";
  import { MANAGED_CONTROL_PHASE } from "$lib/api/generated/codes";
  import type { CreateManagedControlRequest, ManagedControlActionResponse, ManagedControlPreview } from "$lib/api/generated/model";
  import FormField from "$lib/components/forms/FormField.svelte";
  import { cancelControlAction, closeControlAction, fetchControlActions, fetchControlPreview, ManagerActionRejectedError, refreshControlAction, saveControlAction } from "$lib/api/managed-controls";

  let {
    connectionId,
    holdingId,
    canPreview,
    connectionName = "Connected app",
  }: {
    connectionId: string;
    holdingId: string;
    canPreview: boolean;
    connectionName?: string;
  } = $props();
  let dialogOpen = $state(false);
  let actions = $state<ManagedControlActionResponse[]>([]);
  let preview = $state<ManagedControlPreview | null>(null);
  let pending = $state<CreateManagedControlRequest | null>(null);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let profileId = $state("");
  let monitored = $state(false);
  let monitoringChanged = $state(false);
  let search = $state(false);
  let closingId = $state<string | null>(null);
  let closingAcknowledged = $state(false);
  let loadingActions = $state(false);
  let operationId = "";
  let alive = true;
  let loadSequence = 0;
  let loadingSequence = 0;
  let inspectSequence = 0;
  const activePhases = new Set<ManagedControlActionResponse["phase"]>([MANAGED_CONTROL_PHASE.pendingConfiguration, MANAGED_CONTROL_PHASE.configurationUncertain,
    MANAGED_CONTROL_PHASE.pendingSearch, MANAGED_CONTROL_PHASE.searchUncertain, MANAGED_CONTROL_PHASE.awaitingCommand]);
  const attentionPhases = new Set<ManagedControlActionResponse["phase"]>([
    ...activePhases,
    MANAGED_CONTROL_PHASE.closedUnverified,
  ]);
  const hasActive = $derived(actions.some(action => activePhases.has(action.phase)));
  const attentionActions = $derived(actions.filter(action => attentionPhases.has(action.phase)
    || action.reviewRequired || action.canCancel || action.canCloseUnverified));
  const recentActions = $derived(actions.filter(action => !attentionActions.includes(action)).slice(0, 10));
  const changed = $derived(!!preview && (monitoringChanged || profileId !== (preview.state.item.profileId ?? "") || search));
  const phaseLabels = {
    [MANAGED_CONTROL_PHASE.pendingConfiguration]: "Settings queued",
    [MANAGED_CONTROL_PHASE.configurationUncertain]: "Settings outcome uncertain",
    [MANAGED_CONTROL_PHASE.pendingSearch]: "Search queued",
    [MANAGED_CONTROL_PHASE.searchUncertain]: "Search outcome uncertain",
    [MANAGED_CONTROL_PHASE.awaitingCommand]: "Search in progress",
    [MANAGED_CONTROL_PHASE.completed]: "Completed",
    [MANAGED_CONTROL_PHASE.rejected]: "Action refused",
    [MANAGED_CONTROL_PHASE.failed]: "Search failed",
    [MANAGED_CONTROL_PHASE.cancelled]: "Cancelled",
    [MANAGED_CONTROL_PHASE.closedUnverified]: "Closed with outcome unverified",
  };
  onMount(() => {
    const timer = setInterval(() => { if (dialogOpen && !busy && !loadingActions) void load(); }, 5000);
    return () => { alive = false; loadSequence++; loadingSequence++; inspectSequence++; clearInterval(timer); };
  });
  async function load(showLoading = false): Promise<ManagedControlActionResponse[] | null> {
    const sequence = ++loadSequence;
    const loading = showLoading ? ++loadingSequence : null;
    if (loading !== null) loadingActions = true;
    try {
      const result = await fetchControlActions(connectionId, holdingId);
      if (!alive || sequence !== loadSequence) return null;
      actions = result;
      if (pending && result.some(action => action.id === pending?.operationId)) { pending = null; preview = null; error = null; }
      return result;
    } catch (cause) {
      if (alive && sequence === loadSequence) error = message(cause);
      return null;
    } finally {
      if (alive && loading !== null && loading === loadingSequence) loadingActions = false;
    }
  }
  async function initialize() {
    error = null;
    const result = await load(true);
    if (!alive || !dialogOpen || result === null || pending || !canPreview || result.some(action => activePhases.has(action.phase))) return;
    await inspect();
  }
  function setDialogOpen(value: boolean) {
    if (!value && busy) return;
    dialogOpen = value;
    if (value) {
      void initialize();
    } else {
      loadSequence++;
      loadingSequence++;
      inspectSequence++;
      loadingActions = false;
      closingId = null;
      closingAcknowledged = false;
      if (!pending) preview = null;
    }
  }
  async function inspect() {
    const sequence = ++inspectSequence;
    busy = true; error = null;
    try {
      const result = await fetchControlPreview(connectionId, holdingId);
      if (!alive || sequence !== inspectSequence || !dialogOpen) return;
      preview = result; profileId = result.state.item.profileId ?? "";
      monitored = result.state.targets.every(target => target.monitored); monitoringChanged = false; search = false;
      operationId = crypto.randomUUID();
    } catch (cause) { if (alive && sequence === inspectSequence && dialogOpen) error = message(cause); }
    finally { if (alive && sequence === inspectSequence) busy = false; }
  }
  async function submit() {
    if (!pending && (!preview || !changed)) return;
    if (!pending && preview) pending = {
      operationId, scopeFingerprint: preview.scopeFingerprint, expectedPath: preview.state.path,
      expectedProfileId: preview.state.item.profileId!, expectedMonitoring: Object.fromEntries(preview.state.targets.map(target => [target.target.remoteId, target.monitored])),
      changes: { profileId: profileId !== (preview.state.item.profileId ?? "") ? profileId : null, monitored: monitoringChanged ? monitored : null }, search,
    };
    busy = true; error = null; loadSequence++;
    try {
      const saved = await saveControlAction(connectionId, holdingId, pending!);
      if (alive) { actions = [saved, ...actions.filter(action => action.id !== saved.id)]; pending = null; preview = null; }
    } catch (cause) {
      if (alive) { error = message(cause); if (cause instanceof ManagerActionRejectedError) { pending = null; preview = null; } }
    } finally { if (alive) busy = false; }
  }
  async function control(action: ManagedControlActionResponse, operation: typeof cancelControlAction) {
    busy = true; error = null; loadSequence++;
    try {
      const saved = await operation(connectionId, holdingId, action);
      if (alive) { actions = actions.map(item => item.id === saved.id ? saved : item); closingId = null; closingAcknowledged = false; }
    } catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function refresh(action: ManagedControlActionResponse) {
    busy = true; error = null;
    try { await refreshControlAction(connectionId, holdingId, action.id); await load(); }
    catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  function actionLabel(action: ManagedControlActionResponse): string {
    if (action.phase === MANAGED_CONTROL_PHASE.completed) return action.searchRequested ? "Search completed" : "Settings confirmed";
    return action.reviewRequired ? "Needs review" : phaseLabels[action.phase];
  }
  function timestamp(value: string): string {
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
  }
  function message(cause: unknown) { return cause instanceof Error ? cause.message : "Could not complete this manager action"; }
</script>

{#snippet actionRow(action: ManagedControlActionResponse, historical = false)}
  <article class={historical ? "flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 py-2" : "space-y-2 border border-border-subtle bg-surface-2 p-3"}>
    <div class="flex min-w-0 flex-wrap items-center gap-2">
      <Badge>{actionLabel(action)}</Badge>
      {#if action.configurationConfirmed && (action.phase !== MANAGED_CONTROL_PHASE.completed || action.searchRequested)}
        <span class="text-xs text-text-muted">Settings confirmed</span>
      {/if}
    </div>
    <time class="shrink-0 text-xs text-text-muted" datetime={action.updatedAt}>{timestamp(action.updatedAt)}</time>
    {#if action.problem}<p class="w-full break-words text-sm text-text-muted">{action.problem}</p>{/if}
    {#if !historical}
      <div class="flex flex-wrap gap-2">
        {#if activePhases.has(action.phase)}<Button variant="outline" size="sm" disabled={busy} onclick={() => void refresh(action)}>Refresh action</Button>{/if}
        {#if action.canCancel}<Button variant="outline" size="sm" disabled={busy} onclick={() => void control(action, cancelControlAction)}>Cancel unsent stage</Button>{/if}
        {#if action.canCloseUnverified}<Button variant="outline" size="sm" disabled={busy} onclick={() => { closingId = action.id; closingAcknowledged = false; }}>Review unresolved action</Button>{/if}
      </div>
      {#if closingId === action.id}
        <Alert.Root><Alert.Description>Closing this action stops Prismedia from following it. Settings may already have changed and remote work may still be running. Inspect {connectionName} before requesting another action.</Alert.Description></Alert.Root>
        <label class="flex items-center gap-3 text-sm"><Checkbox aria-label={`I reviewed ${connectionName}`} checked={closingAcknowledged} onchange={value => closingAcknowledged = value} />I reviewed {connectionName} and accept the unresolved outcome.</label>
        <Button variant="secondary" size="sm" disabled={busy || !closingAcknowledged} onclick={() => void control(action, closeControlAction)}>Close with outcome unverified</Button>
      {/if}
    {/if}
  </article>
{/snippet}

<div class="min-w-0">
  <Button variant="outline" size="sm" aria-label={`${connectionName} settings and activity`} onclick={() => setDialogOpen(true)}>
    <Settings2 />Settings & activity
  </Button>
</div>

<DialogBase.Root open={dialogOpen} onOpenChange={setDialogOpen}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
    <DialogBase.Header>
      <DialogBase.Title>Settings & activity · {connectionName}</DialogBase.Title>
      <DialogBase.Description>Change monitoring, profile, or request a search for this linked title. Search completion does not confirm a download or readable file.</DialogBase.Description>
    </DialogBase.Header>
    <div class="space-y-4">
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if loadingActions && actions.length === 0}<p class="text-sm text-text-muted">Reading saved action state…</p>{/if}
    {#if attentionActions.length > 0}
      <section class="space-y-2" aria-label="Active and unresolved actions">
        <div>
          <h3 class="text-sm font-semibold">Active and unresolved</h3>
          <p class="mt-1 text-xs text-text-muted">Pending work and saved uncertain outcomes stay visible here. A fresh explicit change is available whenever no action is active.</p>
        </div>
        {#each attentionActions as action (action.id)}{@render actionRow(action)}{/each}
      </section>
    {/if}
    {#if preview}
      <form class="space-y-4 border border-border-subtle bg-surface-2 p-4" onsubmit={event => { event.preventDefault(); void submit(); }}>
        <div>
          <h3 class="text-sm font-semibold">Manager settings</h3>
          <p class="mt-1 text-xs text-text-muted">Applies to {preview.state.targets.length} linked {preview.state.targets.length === 1 ? "item" : "items"} in {connectionName}.</p>
        </div>
        <FormField label="Profile" htmlFor="manager-profile">
          <Select id="manager-profile" ariaLabel="Manager profile" value={profileId} options={preview.options.profiles.map(profile => ({ value: profile.id, label: profile.label }))}
            onchange={value => profileId = value} disabled={busy || !!pending || !preview.state.capabilities.canChangeProfile} />
        </FormField>
        {#if !preview.state.capabilities.canChangeProfile}<p class="text-xs text-text-muted">Change this profile in the connected app.</p>{/if}
        <div class="space-y-1">
          <label class="flex items-center justify-between gap-3 text-sm">Monitoring
            <Toggle ariaLabel="Monitoring" checked={monitored} onchange={value => { monitored = value; monitoringChanged = true; }} disabled={busy || !!pending || !preview.state.capabilities.canChangeMonitoring} />
          </label>
          <p class="text-xs text-text-muted">Asks {connectionName} to keep looking for releases automatically.</p>
        </div>
        {#if preview.state.capabilities.monitoringUnavailableReason}<p class="text-xs text-text-muted">{preview.state.capabilities.monitoringUnavailableReason}</p>{/if}
        {#if !monitoringChanged && preview.state.targets.some(target => target.monitored) && !preview.state.targets.every(target => target.monitored)}<p class="text-xs text-text-muted">Current monitoring varies across linked items. It will remain unchanged unless you change this control.</p>{/if}
        <div class="space-y-1">
          <label class="flex items-center gap-3 text-sm"><Checkbox aria-label="Search now" checked={search} onchange={value => search = value} disabled={busy || !!pending || !preview.state.capabilities.canSearch} />Search now</label>
          <p class="text-xs text-text-muted">Requests one immediate search using the current settings.</p>
        </div>
        <p class="text-xs text-text-muted">Only settings you change are sent. {connectionName} continues to organize its files.</p>
        <Button type="submit" disabled={busy || !canPreview || !pending && (!changed || hasActive)}>{pending ? "Retry same action" : "Apply changes"}</Button>
      </form>
    {/if}
    {#if !preview && !pending && !hasActive && !loadingActions}
      <Button variant="secondary" size="sm" disabled={busy || !canPreview} onclick={inspect}>Change settings</Button>
    {/if}
    {#if recentActions.length > 0}
      <Disclosure title="Recent activity" icon={Clock3} count={recentActions.length}>
        <div class="divide-y divide-border-subtle">
          {#each recentActions as action (action.id)}{@render actionRow(action, true)}{/each}
        </div>
      </Disclosure>
    {/if}
    </div>
    <DialogBase.Footer><Button variant="outline" disabled={busy} onclick={() => setDialogOpen(false)}>Done</Button></DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
