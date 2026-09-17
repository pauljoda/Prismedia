<script lang="ts">
  import { onMount } from "svelte";
  import { Alert, Badge, Button, Checkbox, Select, Toggle } from "@prismedia/ui-svelte";
  import { MANAGED_CONTROL_PHASE } from "$lib/api/generated/codes";
  import type { CreateManagedControlRequest, ManagedControlActionResponse, ManagedControlPreview } from "$lib/api/generated/model";
  import { cancelControlAction, closeControlAction, fetchControlActions, fetchControlPreview, ManagerActionRejectedError, refreshControlAction, saveControlAction } from "$lib/api/managed-controls";

  let { connectionId, holdingId, canPreview }: { connectionId: string; holdingId: string; canPreview: boolean } = $props();
  let expanded = $state(false);
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
  let operationId = "";
  let alive = true;
  let loadSequence = 0;
  const activePhases = new Set<ManagedControlActionResponse["phase"]>([MANAGED_CONTROL_PHASE.pendingConfiguration, MANAGED_CONTROL_PHASE.configurationUncertain,
    MANAGED_CONTROL_PHASE.pendingSearch, MANAGED_CONTROL_PHASE.searchUncertain, MANAGED_CONTROL_PHASE.awaitingCommand]);
  const hasActive = $derived(actions.some(action => activePhases.has(action.phase)));
  const changed = $derived(!!preview && (monitoringChanged || profileId !== preview.state.item.profileId || search));
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
    const timer = setInterval(() => { if (expanded && !busy) void load(); }, 5000);
    return () => { alive = false; loadSequence++; clearInterval(timer); };
  });
  async function load() {
    const sequence = ++loadSequence;
    try {
      const result = await fetchControlActions(connectionId, holdingId);
      if (!alive || sequence !== loadSequence) return;
      actions = result;
      if (pending && result.some(action => action.id === pending?.operationId)) { pending = null; preview = null; error = null; }
    } catch (cause) { if (alive && sequence === loadSequence) error = message(cause); }
  }
  async function inspect() {
    busy = true; error = null;
    try {
      const result = await fetchControlPreview(connectionId, holdingId);
      if (!alive) return;
      preview = result; profileId = result.state.item.profileId ?? "";
      monitored = result.state.targets.every(target => target.monitored); monitoringChanged = false; search = false;
      operationId = crypto.randomUUID();
    } catch (cause) { if (alive) error = message(cause); }
    finally { if (alive) busy = false; }
  }
  async function submit() {
    if (!pending && (!preview || !changed)) return;
    if (!pending && preview) pending = {
      operationId, scopeFingerprint: preview.scopeFingerprint, expectedPath: preview.state.path,
      expectedProfileId: preview.state.item.profileId!, expectedMonitoring: Object.fromEntries(preview.state.targets.map(target => [target.target.remoteId, target.monitored])),
      changes: { profileId: profileId !== preview.state.item.profileId ? profileId : null, monitored: monitoringChanged ? monitored : null }, search,
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
  function message(cause: unknown) { return cause instanceof Error ? cause.message : "Could not complete this manager action"; }
</script>

<div class="min-w-0 space-y-3">
  <Button variant="outline" size="sm" aria-expanded={expanded} onclick={() => { expanded = !expanded; if (expanded) void load(); }}>Manager controls</Button>
  {#if expanded}
    <p class="text-xs text-text-muted">The connected app owns monitoring, release selection and file organization. Search completion does not mean a download or local file is available.</p>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if !preview && !pending && !hasActive}<Button variant="secondary" size="sm" disabled={busy || !canPreview} onclick={inspect}>Review manager settings</Button>{/if}
    {#if preview}
      <form class="space-y-4 border border-border-subtle p-3" onsubmit={event => { event.preventDefault(); void submit(); }}>
        <p class="text-xs text-text-muted">Applies to {preview.state.targets.length} linked {preview.state.targets.length === 1 ? "item" : "items"}.</p>
        <Select ariaLabel="Manager profile" value={profileId} options={preview.options.profiles.map(profile => ({ value: profile.id, label: profile.label }))}
          onchange={value => profileId = value} disabled={busy || !!pending || !preview.state.capabilities.canChangeProfile} />
        {#if !preview.state.capabilities.canChangeProfile}<p class="text-xs text-text-muted">Change this profile in the connected app.</p>{/if}
        <label class="flex items-center justify-between gap-3 text-sm">Monitoring
          <Toggle ariaLabel="Monitoring" checked={monitored} onchange={value => { monitored = value; monitoringChanged = true; }} disabled={busy || !!pending || !preview.state.capabilities.canChangeMonitoring} />
        </label>
        {#if preview.state.capabilities.monitoringUnavailableReason}<p class="text-xs text-text-muted">{preview.state.capabilities.monitoringUnavailableReason}</p>{/if}
        {#if !monitoringChanged && preview.state.targets.some(target => target.monitored) && !preview.state.targets.every(target => target.monitored)}<p class="text-xs text-text-muted">Current monitoring varies across linked items. It will remain unchanged unless you change this control.</p>{/if}
        <label class="flex items-center gap-3 text-sm"><Checkbox aria-label="Search now" checked={search} onchange={value => search = value} disabled={busy || !!pending || !preview.state.capabilities.canSearch} />Search now</label>
        <p class="text-xs text-text-muted">Only settings you change are sent. The library folder stays managed by the connected app.</p>
        <Button type="submit" variant="secondary" disabled={busy || !canPreview || !pending && (!changed || hasActive)}>{pending ? "Retry same action" : "Apply manager action"}</Button>
      </form>
    {/if}
    {#each actions.slice(0, 10) as action (action.id)}
      <article class="space-y-2 border-t border-border-subtle pt-3">
        <div class="flex flex-wrap items-center gap-2"><Badge>{action.phase === MANAGED_CONTROL_PHASE.completed ? action.searchRequested ? "Search completed" : "Settings confirmed" : action.reviewRequired ? "Needs review" : phaseLabels[action.phase]}</Badge>
          {#if action.configurationConfirmed && (action.phase !== MANAGED_CONTROL_PHASE.completed || action.searchRequested)}<span class="text-xs text-text-muted">Settings confirmed</span>{/if}</div>
        <p class="text-xs text-text-muted">{new Date(action.createdAt).toLocaleString()}</p>
        {#if action.problem}<p class="break-words text-sm text-text-muted">{action.problem}</p>{/if}
        <div class="flex flex-wrap gap-2">
          {#if activePhases.has(action.phase)}<Button variant="outline" size="sm" disabled={busy} onclick={() => void refresh(action)}>Refresh action</Button>{/if}
          {#if action.canCancel}<Button variant="outline" size="sm" disabled={busy} onclick={() => void control(action, cancelControlAction)}>Cancel unsent stage</Button>{/if}
          {#if action.canCloseUnverified}<Button variant="outline" size="sm" disabled={busy} onclick={() => { closingId = action.id; closingAcknowledged = false; }}>Review unresolved action</Button>{/if}
        </div>
        {#if closingId === action.id}
          <Alert.Root><Alert.Description>Closing this action stops Prismedia from following it. Settings may already have changed and remote work may still be running. Inspect the connected app before requesting another action.</Alert.Description></Alert.Root>
          <label class="flex items-center gap-3 text-sm"><Checkbox aria-label="I reviewed the connected app" checked={closingAcknowledged} onchange={value => closingAcknowledged = value} />I reviewed the connected app and accept the unresolved outcome.</label>
          <Button variant="secondary" size="sm" disabled={busy || !closingAcknowledged} onclick={() => void control(action, closeControlAction)}>Close with outcome unverified</Button>
        {/if}
      </article>
    {/each}
  {/if}
</div>
