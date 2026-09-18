<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { Loader2 } from "@lucide/svelte";
  import { Alert, Button, Checkbox, Select } from "@prismedia/ui-svelte";
  import {
    CONNECTION_STATUS,
    ENTITY_KIND,
    INTEGRATION_OPERATION,
    PLUGIN_CAPABILITY,
  } from "$lib/api/generated/codes";
  import { getGetPluginIconUrl } from "$lib/api/generated/prismedia";
  import type {
    ConnectionResponse,
    EntityKind,
    ExternalLibraryMount,
    LibraryRootSummary,
    ReviewedManagedRequest,
    ReviewedRequestCommitRequest,
  } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { fetchLibraryMounts } from "$lib/api/managed-libraries";
  import {
    fetchReviewedManagedRequest,
    type ManagedRequestChoice,
  } from "$lib/api/reviewed-managed-requests";
  import { fetchAccessibleLibraryRoots } from "$lib/api/settings";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";

  interface Props {
    entityKind: EntityKind;
    /** Manager-origin reviews can only be fulfilled through the originating connection. */
    fixedConnection?: ConnectionResponse | null;
    request: ReviewedRequestCommitRequest | null;
    managerDiscoveryRevision?: number | string | null;
    /** Forces a fresh read-only preflight after the server definitely rejects a commit. */
    refreshToken?: number;
    disabled?: boolean;
    onChange: (value: ManagedRequestChoice | null, active: boolean) => void;
  }

  let {
    entityKind,
    fixedConnection = null,
    request,
    managerDiscoveryRevision = null,
    refreshToken = 0,
    disabled = false,
    onChange,
  }: Props = $props();

  let connections = $state<ConnectionResponse[]>([]);
  let connectionId = $state(untrack(() => fixedConnection?.id ?? ""));
  let mounts = $state<ExternalLibraryMount[]>([]);
  let roots = $state<LibraryRootSummary[]>([]);
  let review = $state<ReviewedManagedRequest | null>(null);
  let libraryRootId = $state("");
  let profileId = $state("");
  let monitored = $state(false);
  let search = $state(true);
  let loadingConnections = $state(untrack(() => !fixedConnection));
  let loadingReview = $state(false);
  let mounted = $state(false);
  let error = $state<string | null>(null);
  let retryRevision = $state(0);
  let sequence = 0;

  const active = $derived(Boolean(connectionId));
  const connection = $derived(
    fixedConnection?.id === connectionId
      ? fixedConnection
      : connections.find((candidate) => candidate.id === connectionId) ?? null,
  );
  const isSeries = $derived(entityKind === ENTITY_KIND.videoSeries);
  const requestKey = $derived(request ? JSON.stringify(request) : "");
  const mappedLibraries = $derived(
    mounts.filter((mount) => roots.length === 0 || roots.some((root) => root.id === mount.libraryRootId)),
  );
  const libraryOptions = $derived(mappedLibraries.map((mount) => ({
    value: mount.libraryRootId,
    label: roots.find((root) => root.id === mount.libraryRootId)?.label ?? mount.label,
    annotation: mount.remotePath,
  })));
  const profileOptions = $derived(review?.options.profiles.map((profile) => ({
    value: profile.id,
    label: profile.label,
  })) ?? []);

  onMount(() => {
    mounted = true;
    if (fixedConnection) {
      connections = [fixedConnection];
      connectionId = fixedConnection.id;
      onChange(null, true);
    } else {
      void loadConnections();
    }
    return () => {
      mounted = false;
      sequence++;
    };
  });

  $effect(() => {
    if (!mounted) return;
    const selectedConnectionId = connectionId;
    const payload = request;
    const revision = managerDiscoveryRevision;
    requestKey;
    libraryRootId;
    retryRevision;
    refreshToken;
    if (!selectedConnectionId || !payload) {
      sequence++;
      review = null;
      loadingReview = false;
      onChange(null, Boolean(selectedConnectionId));
      return;
    }
    const current = ++sequence;
    loadingReview = true;
    error = null;
    onChange(null, true);
    const timer = window.setTimeout(() => {
      void loadReview(current, selectedConnectionId, payload, revision);
    }, 240);
    return () => window.clearTimeout(timer);
  });

  async function loadConnections() {
    loadingConnections = true;
    error = null;
    try {
      const result = await fetchConnections();
      if (!mounted) return;
      connections = result.filter((candidate) => supportsReviewedRequest(candidate, entityKind));
    } catch (cause) {
      if (mounted) error = message(cause, "Could not load manager connections");
    } finally {
      if (mounted) loadingConnections = false;
    }
  }

  async function loadReview(
    current: number,
    selectedConnectionId: string,
    payload: ReviewedRequestCommitRequest,
    revision: number | string | null,
  ) {
    const previousReview = review;
    const previousWork = previousReview ? workKey(previousReview) : null;
    try {
      const [result, nextMounts, nextRoots] = await Promise.all([
        fetchReviewedManagedRequest(selectedConnectionId, {
          libraryRootId: libraryRootId || null,
          request: payload,
          managerDiscoveryRevision: revision,
        }),
        fetchLibraryMounts(selectedConnectionId),
        fetchAccessibleLibraryRoots(),
      ]);
      if (!mounted || current !== sequence) return;
      const sameWork = previousWork === workKey(result);
      mounts = nextMounts;
      roots = nextRoots;
      review = result;
      libraryRootId = result.mount.libraryRootId;

      const existingProfile = result.existing?.item.profileId ?? "";
      const validExistingProfile = result.options.profiles.some((profile) => profile.id === existingProfile);
      const retainedProfile = sameWork && result.options.profiles.some((profile) => profile.id === profileId)
        ? profileId
        : "";
      profileId = result.existing
        ? validExistingProfile ? existingProfile : ""
        : retainedProfile || result.options.profiles[0]?.id || "";
      if (!sameWork) {
        monitored = result.existing?.item.monitored ?? false;
        search = true;
      }
      if (isSeries) {
        monitored = false;
        search = true;
      }
      loadingReview = false;
      publishChoice();
    } catch (cause) {
      if (!mounted || current !== sequence) return;
      review = null;
      error = message(cause, "Could not review the manager request");
      onChange(null, true);
    } finally {
      if (mounted && current === sequence) loadingReview = false;
    }
  }

  function selectConnection(value: string) {
    sequence++;
    connectionId = value;
    review = null;
    mounts = [];
    roots = [];
    libraryRootId = "";
    profileId = "";
    error = null;
    onChange(null, Boolean(value));
  }

  function selectLibrary(value: string) {
    if (value === libraryRootId) return;
    sequence++;
    libraryRootId = value;
    loadingReview = true;
    error = null;
    onChange(null, true);
  }

  function selectProfile(value: string) {
    profileId = value;
    publishChoice();
  }

  function publishChoice() {
    if (!connectionId || !review || !profileId || loadingReview || error) {
      onChange(null, Boolean(connectionId));
      return;
    }
    onChange({ connectionId, review, profileId, monitored, search }, true);
  }

  function supportsReviewedRequest(candidate: ConnectionResponse, kind: EntityKind): boolean {
    if (!candidate.enabled || candidate.status !== CONNECTION_STATUS.ready) return false;
    const manager = candidate.effectiveCapabilities.find((capability) =>
      capability.kind === PLUGIN_CAPABILITY.externalManager && capability.entityKinds.includes(kind));
    const library = candidate.effectiveCapabilities.find((capability) =>
      capability.kind === PLUGIN_CAPABILITY.connectedLibrary && capability.entityKinds.includes(kind));
    return Boolean(manager
      && [
        INTEGRATION_OPERATION.lookupManaged,
        INTEGRATION_OPERATION.ensureManaged,
        INTEGRATION_OPERATION.requestManaged,
        INTEGRATION_OPERATION.reconcileManaged,
        INTEGRATION_OPERATION.configureManaged,
      ].every((operation) => manager.operations.includes(operation))
      && library?.operations.includes(INTEGRATION_OPERATION.getLibraryItem)
      && library.operations.includes(INTEGRATION_OPERATION.listLibraries));
  }

  function workKey(value: ReviewedManagedRequest): string {
    return JSON.stringify(value.work);
  }

  function message(cause: unknown, fallback: string): string {
    return cause instanceof Error ? cause.message : fallback;
  }
</script>

<div class="space-y-3">
  {#if !fixedConnection}
    <label class="block max-w-md space-y-1.5">
      <span class="text-sm font-medium text-text-secondary">Fulfillment</span>
      <Select
        ariaLabel="Acquisition owner"
        value={connectionId}
        disabled={disabled || loadingConnections}
        placeholder={loadingConnections ? "Loading connections…" : "Choose fulfillment"}
        options={[
          { value: "", label: "Prismedia downloads" },
          ...connections.map((item) => ({ value: item.id, label: item.name })),
        ]}
        onchange={selectConnection}
      >
        {#snippet optionLeading(option)}
          {#if option.value}
            {@const optionConnection = connections.find((item) => item.id === option.value)}
            <PluginIcon
              name={option.label}
              iconUrl={optionConnection ? getGetPluginIconUrl(optionConnection.pluginId) : null}
              class="size-5"
            />
          {/if}
        {/snippet}
      </Select>
    </label>
  {:else}
    <div class="flex items-center gap-2.5">
      <PluginIcon name={fixedConnection.name} iconUrl={getGetPluginIconUrl(fixedConnection.pluginId)} class="size-8" />
      <div class="min-w-0">
        <p class="truncate text-sm font-medium text-text-primary">{fixedConnection.name}</p>
      </div>
    </div>
  {/if}

  {#if error}
    <Alert.Root variant="destructive">
      <Alert.Description>
        {error}
        {#if mounts.length === 0 || error.toLowerCase().includes("map")}
          <a class="ml-1 font-medium underline underline-offset-2" href="/settings/libraries">Open Settings → Libraries</a>
        {/if}
        {#if active && request}
          <Button class="ml-2" type="button" variant="link" size="sm" disabled={disabled} onclick={() => retryRevision++}>Try again</Button>
        {/if}
      </Alert.Description>
    </Alert.Root>
  {/if}

  {#if active && loadingReview}
    <div class="flex items-center gap-2 py-1 text-sm text-text-muted" role="status">
      <Loader2 class="size-4 animate-spin" />
      Reviewing manager options…
    </div>
  {:else if connection && review}
    <div class="grid gap-3">
      <label class="space-y-1.5">
        <span class="text-sm font-medium text-text-secondary">Library</span>
        <Select
          ariaLabel="Manager library"
          value={libraryRootId}
          options={libraryOptions}
          disabled={disabled || Boolean(review.existing) || mappedLibraries.length <= 1}
          onchange={selectLibrary}
        />
      </label>
      <label class="space-y-1.5">
        <span class="text-sm font-medium text-text-secondary">Quality profile</span>
        <Select
          ariaLabel="Manager quality profile"
          value={profileId}
          options={profileOptions}
          disabled={disabled || Boolean(review.existing)}
          onchange={selectProfile}
        />
      </label>
    </div>

    {#if isSeries}
      <p class="text-sm leading-relaxed text-text-muted">
        {review.work.targets?.length ?? 0} selected episode{review.work.targets?.length === 1 ? "" : "s"} will be searched now; broad series monitoring stays off.
      </p>
    {:else}
      <div class="flex flex-wrap gap-x-6 gap-y-2">
        <label class="flex items-center gap-2 text-sm text-text-secondary">
          <Checkbox
            aria-label={`Monitor in ${connection.name}`}
            checked={monitored}
            disabled={disabled}
            onchange={(value) => { monitored = value; publishChoice(); }}
          />
          Monitor in {connection.name}
        </label>
        <label class="flex items-center gap-2 text-sm text-text-secondary">
          <Checkbox
            aria-label="Search now"
            checked={search}
            disabled={disabled}
            onchange={(value) => { search = value; publishChoice(); }}
          />
          Search now
        </label>
      </div>
    {/if}

    {#if review.existing}
      <p class="text-xs leading-relaxed text-text-muted">
        This title already exists in {connection.name}; its current library and quality profile are retained.
      </p>
    {/if}
    {#if profileOptions.length === 0 || (review.existing && !profileId)}
      <p class="text-sm leading-relaxed text-text-muted">
        {review.existing
          ? "The title's current quality profile is no longer available."
          : "No quality profile is available from this manager."}
        <a class="ml-1 font-medium underline underline-offset-2" href="/settings/connections">Check connection settings</a>
      </p>
    {/if}
  {/if}
</div>
