<script lang="ts">
  import { onMount } from "svelte";
  import { Alert, Button, Checkbox, Panel, Select } from "@prismedia/ui-svelte";
  import {
    BOOK_RENDITION,
    type BookRenditionCode,
  } from "$lib/api/generated/codes";
  import type {
    ConnectionResponse,
    CreateManagedRequestInput,
    AcquisitionDetail,
    ExternalLibraryMount,
    ExternalBookRenditionProvenance,
    ManagedRequestPreview,
    ManagedRequestResponse,
    MonitorView,
  } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { fetchLibraryMounts } from "$lib/api/managed-libraries";
  import {
    fetchManagedRequestPreview,
    ManagedRequestRejectedError,
    saveManagedRequest,
  } from "$lib/api/managed-requests";
  import { createUuid } from "$lib/utils/uuid";
  import { supportsBookManager } from "$lib/requests/book-manager-connection";
  import { bookRenditionCanRequest, bookRenditionManagerOwner, bookRenditionRows } from "$lib/requests/book-rendition-acquisition";

  let {
    bookId,
    title,
    hasEbook,
    hasAudiobook,
    acquisitions,
    monitors,
    managedRenditions,
    onChanged,
  }: {
    bookId: string;
    title: string;
    hasEbook: boolean;
    hasAudiobook: boolean;
    acquisitions: readonly AcquisitionDetail[];
    monitors: readonly MonitorView[];
    managedRenditions: readonly ExternalBookRenditionProvenance[];
    onChanged?: () => void | Promise<void>;
  } = $props();

  const renditions = [BOOK_RENDITION.ebook, BOOK_RENDITION.audiobook] as const;
  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state("");
  let mounts = $state<ExternalLibraryMount[]>([]);
  let selected = $state<BookRenditionCode[]>([]);
  let rootIds = $state<Partial<Record<BookRenditionCode, string>>>({});
  let previews = $state<Partial<Record<BookRenditionCode, ManagedRequestPreview>>>({});
  let pending = $state<Partial<Record<BookRenditionCode, CreateManagedRequestInput>>>({});
  let accepted = $state<Partial<Record<BookRenditionCode, ManagedRequestResponse>>>({});
  let search = $state(false);
  let loading = $state(true);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let needsReview = $state(false);
  let alive = true;
  let loadSequence = 0;

  const available = $derived(bookRenditionRows(acquisitions, monitors, {
    ebook: hasEbook,
    audiobook: hasAudiobook,
  }).filter(row => bookRenditionCanRequest(row)
    && !bookRenditionManagerOwner(row.rendition, managedRenditions))
    .map(row => row.rendition));
  const selectedConnection = $derived(connections.find(connection => connection.id === connectionId) ?? null);
  const reviewed = $derived(selected.length > 0 && selected.every(rendition =>
    Boolean(accepted[rendition] || previews[rendition])));
  const hasAccepted = $derived(Object.values(accepted).some(Boolean));
  const canSubmit = $derived(reviewed && selected.every(rendition =>
    Boolean(pending[rendition] || previews[rendition])));

  onMount(() => {
    void loadConnections();
    return () => { alive = false; loadSequence++; };
  });

  async function loadConnections() {
    try {
      const result = (await fetchConnections()).filter(supportsBookManager);
      if (!alive) return;
      connections = result;
      if (result.length === 1) await selectConnection(result[0].id);
    } catch (cause) {
      if (alive) error = message(cause);
    } finally {
      if (alive) loading = false;
    }
  }

  async function selectConnection(id: string) {
    const sequence = ++loadSequence;
    connectionId = id;
    mounts = [];
    invalidateReview();
    if (!id) return;
    loading = true;
    try {
      const result = await fetchLibraryMounts(id);
      if (alive && sequence === loadSequence) mounts = result;
    } catch (cause) {
      if (alive && sequence === loadSequence) error = message(cause);
    } finally {
      if (alive && sequence === loadSequence) loading = false;
    }
  }

  function invalidateReview() {
    if (hasAccepted) return;
    previews = {};
    pending = {};
    accepted = {};
    error = null;
    needsReview = false;
  }

  function toggleRendition(rendition: BookRenditionCode, enabled: boolean) {
    selected = enabled ? [...selected, rendition] : selected.filter(item => item !== rendition);
    invalidateReview();
  }

  function setRoot(rendition: BookRenditionCode, rootId: string) {
    rootIds = { ...rootIds, [rendition]: rootId };
    invalidateReview();
  }

  async function review() {
    if (!connectionId || selected.length === 0 || selected.some(rendition => !rootIds[rendition])) return;
    busy = true;
    error = null;
    const previousPreviews = previews;
    previews = {};
    try {
      const results: Partial<Record<BookRenditionCode, ManagedRequestPreview>> = {};
      for (const rendition of selected) {
        if (accepted[rendition]) {
          results[rendition] = previousPreviews[rendition];
          continue;
        }
        results[rendition] = await fetchManagedRequestPreview(connectionId, {
          entityId: bookId,
          libraryRootId: rootIds[rendition]!,
          bookRendition: rendition,
        });
      }
      if (alive) { previews = results; needsReview = false; }
    } catch (cause) {
      if (alive) error = message(cause);
    } finally {
      if (alive) busy = false;
    }
  }

  async function submit() {
    if (!connectionId || !canSubmit || needsReview) return;
    busy = true;
    error = null;
    let changed = false;
    for (const rendition of selected) {
      if (accepted[rendition]) continue;
      const preview = previews[rendition];
      if (!preview) continue;
      const intent = pending[rendition] ?? {
        operationId: createUuid(),
        entityId: bookId,
        libraryRootId: preview.mount.libraryRootId,
        reviewedWork: preview.work,
        profileId: null,
        monitored: true,
        search,
      };
      pending = { ...pending, [rendition]: intent };
      try {
        const result = await saveManagedRequest(connectionId, intent);
        if (!alive) return;
        accepted = { ...accepted, [rendition]: result };
        pending = { ...pending, [rendition]: undefined };
        changed = true;
      } catch (cause) {
        if (!alive) return;
        if (cause instanceof ManagedRequestRejectedError) {
          pending = { ...pending, [rendition]: undefined };
          needsReview = true;
        }
        error = `${label(rendition)}: ${message(cause)}${changed ? " The other format was accepted." : ""}`;
        break;
      }
    }
    if (alive) {
      busy = false;
      if (changed && selected.every(rendition => Boolean(accepted[rendition]))) await onChanged?.();
    }
  }

  function label(rendition: BookRenditionCode): string {
    return rendition === BOOK_RENDITION.audiobook ? "Audiobook" : "Ebook";
  }

  function message(cause: unknown): string {
    return cause instanceof Error ? cause.message : "Could not review this manager request";
  }
</script>

{#if available.length > 0 && (loading || connections.length > 0 || error)}
  <Panel class="space-y-4 p-4" aria-label="Connected Book manager request">
    <div>
      <h3 class="text-sm font-semibold">Request through a connected book manager</h3>
      <p class="mt-1 text-sm text-text-muted">Review each missing format of {title} against a mapped library before the manager searches for it.</p>
    </div>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    {#if loading}<p class="text-sm text-text-muted">Loading connected libraries…</p>{/if}
    {#if connections.length > 0}
      <Select ariaLabel="Book manager" value={connectionId}
        options={[{ value: "", label: "Choose a book manager" }, ...connections.map(connection => ({ value: connection.id, label: connection.name }))]}
        disabled={busy || hasAccepted || Object.values(pending).some(Boolean)} onchange={value => void selectConnection(value)} />
      {#if selectedConnection && mounts.length > 0}
        {#each available as rendition (rendition)}
          <div class="space-y-2 rounded-sm border border-border-subtle p-3">
            <label class="flex items-center gap-2 text-sm">
              <Checkbox checked={selected.includes(rendition)} disabled={busy || hasAccepted}
                onchange={enabled => toggleRendition(rendition, enabled)} />
              {label(rendition)}
            </label>
            {#if selected.includes(rendition)}
              <Select ariaLabel={`${label(rendition)} mapped library`} value={rootIds[rendition] ?? ""}
                options={[{ value: "", label: "Choose mapped library" }, ...mounts.map(mount => ({ value: mount.libraryRootId, label: mount.label }))]}
                disabled={busy || hasAccepted} onchange={value => setRoot(rendition, value)} />
              {#if previews[rendition]}
                <p class="text-xs text-text-muted">{previews[rendition].existing
                  ? "This work already exists in the manager. Its location will be retained."
                  : "The manager will add this work to its catalog."}</p>
              {/if}
              {#if accepted[rendition]}<p class="text-sm" role="status">{label(rendition)} request accepted.</p>{/if}
            {/if}
          </div>
        {/each}
        <p class="text-xs text-text-muted">The manager will monitor each accepted format until its files are available.</p>
        <label class="flex items-center gap-2 text-sm"><Checkbox checked={search} disabled={busy || reviewed || hasAccepted}
          onchange={value => { search = value; invalidateReview(); }} />Search now</label>
        {#if !reviewed || needsReview}
          <Button variant="secondary" disabled={busy || selected.length === 0 || selected.some(rendition => !rootIds[rendition])}
            onclick={() => void review()}>Review manager request</Button>
        {:else if selected.some(rendition => !accepted[rendition])}
          <Button variant="primary" disabled={busy} onclick={() => void submit()}>
            {Object.values(pending).some(Boolean) ? "Retry same request" : selected.length === 2 ? "Request both formats" : `Request ${label(selected[0]).toLowerCase()}`}
          </Button>
        {/if}
      {:else if selectedConnection && !loading}
        <p class="text-sm text-text-muted">Map a book library for this connection before requesting it.</p>
      {/if}
    {/if}
  </Panel>
{/if}
