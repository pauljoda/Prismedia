<script lang="ts">
  import {
    CheckCircle2,
    CircleAlert,
    Clock3,
    FileCheck2,
    FileText,
    FolderCog,
    FolderOpen,
    HardDrive,
    Link2,
    LoaderCircle,
    Server,
  } from "@lucide/svelte";
  import { Badge, Button, DialogBase, Panel } from "@prismedia/ui-svelte";
  import { ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
  import type {
    ConnectionResponse,
    ManagedItemSnapshot,
    ManagedLibraryFile,
    ManagerOptions,
    MappedLibraryFile,
  } from "$lib/api/generated/model";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import type { EntityDetailCard } from "$lib/entities/entity-detail";
  import { displayNameForEntityKind } from "$lib/entities/entity-codes";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { formatBytes } from "$lib/utils/format";
  import ExternalLibraryMappings from "./ExternalLibraryMappings.svelte";
  import ManagedHoldingTracking from "./ManagedHoldingTracking.svelte";

  const INITIAL_FILE_LIMIT = 50;
  const FILE_PAGE_SIZE = 50;
  const overviewSectionId = "connected-overview";
  const filesSectionId = "connected-files";
  const librarySectionId = "connected-library";

  let {
    connection,
    detail,
    options = null,
    localFiles = null,
    checking = false,
    availabilityError = null,
    showControls = false,
    canControl = false,
    canRelease = false,
    onRecheckAvailability,
  }: {
    connection: ConnectionResponse;
    detail: ManagedItemSnapshot;
    options?: ManagerOptions | null;
    localFiles?: MappedLibraryFile[] | null;
    checking?: boolean;
    availabilityError?: string | null;
    showControls?: boolean;
    canControl?: boolean;
    canRelease?: boolean;
    onRecheckAvailability?: () => void;
  } = $props();

  let visibleFiles = $state(INITIAL_FILE_LIMIT);
  let mappingOpen = $state(false);
  const kindLabel = $derived(displayNameForEntityKind(detail.item.entityKind));
  const isTrackable = $derived(
    detail.item.entityKind === ENTITY_KIND.movie || detail.item.entityKind === ENTITY_KIND.videoSeries,
  );
  const profile = $derived(
    detail.item.profileId
      ? options?.profiles.find((candidate) => candidate.id === detail.item.profileId)?.label ?? detail.item.profileId
      : null,
  );
  const presentation = $derived(detail.item.presentation ?? null);
  const runtimeLabel = $derived(formatRuntimeMinutes(presentation?.runtimeMinutes));
  const visibleReportedFiles = $derived(detail.files.slice(0, visibleFiles));
  const remainingFileCount = $derived(Math.max(0, detail.files.length - visibleFiles));
  const verifiedFileCount = $derived(
    localFiles === null
      ? 0
      : detail.files.filter((file) => localFiles.some((candidate) =>
        candidate.remoteId === file.remoteId && candidate.isReadable && candidate.sizeMatches,
      )).length,
  );
  const hasMissingMapping = $derived(
    localFiles !== null && detail.files.some((file) => localFiles?.some((candidate) =>
      candidate.remoteId === file.remoteId && candidate.libraryRootId === null && candidate.localPath === null,
    )),
  );
  const hasMappedProblem = $derived(
    localFiles !== null && detail.files.some((file) => localFiles?.some((candidate) =>
      candidate.remoteId === file.remoteId
        && (candidate.libraryRootId !== null || candidate.localPath !== null)
        && (!candidate.isReadable || !candidate.sizeMatches),
    )),
  );
  const hasRelevantEvidence = $derived(
    localFiles !== null && detail.files.some((file) => localFiles?.some((candidate) => candidate.remoteId === file.remoteId)),
  );
  const canConfigureMapping = $derived(connection.effectiveCapabilities.some((capability) =>
    capability.kind === PLUGIN_CAPABILITY.externalManager
      && capability.operations.includes(INTEGRATION_OPERATION.managerOptions),
  ));
  const localSummary = $derived.by(() => {
    if (detail.files.length === 0) {
      return {
        label: "No files reported",
        detail: `${connection.name} has not reported any final files for this title.`,
        variant: "default" as const,
        state: "empty" as const,
      };
    }
    if (checking) {
      return {
        label: "Checking availability",
        detail: `Prismedia is matching the paths reported by ${connection.name} to your mapped folders and checking read access.`,
        variant: "default" as const,
        state: "checking" as const,
      };
    }
    if (availabilityError) {
      return {
        label: "Availability unknown",
        detail: `The title details loaded, but Prismedia could not complete the file check. ${availabilityError}`,
        variant: "warning" as const,
        state: "unknown" as const,
      };
    }
    if (localFiles === null) {
      return {
        label: "Availability unknown",
        detail: "Prismedia has not completed a read-access check for these source files.",
        variant: "default" as const,
        state: "unknown" as const,
      };
    }
    if (verifiedFileCount === detail.files.length) {
      return {
        label: "Files readable by Prismedia",
        detail: `Every reported file can be opened read-only at its mapped path and matches the size reported by ${connection.name}. This does not mean the title is scanned into your Prismedia library or ready to play.`,
        variant: "success" as const,
        state: "readable" as const,
      };
    }
    if (hasMissingMapping) {
      return {
        label: "Library folder setup needed",
        detail: `Prismedia has the file paths from ${connection.name}, but no library folder mapping covers one or more of them. Map the source folder to the same folder as mounted on the Prismedia server.`,
        variant: "warning" as const,
        state: "setup" as const,
      };
    }
    if (!hasRelevantEvidence) {
      return {
        label: "Availability unknown",
        detail: "The availability check returned no matching file evidence, so Prismedia cannot confirm access.",
        variant: "warning" as const,
        state: "unknown" as const,
      };
    }
    return {
      label: verifiedFileCount > 0
        ? `${verifiedFileCount} of ${detail.files.length} files readable`
        : "Files missing or unavailable",
      detail: hasMappedProblem
        ? `One or more mapped paths are missing, unreadable, or a different size than ${connection.name} reported.`
        : "Prismedia could not confirm read access for every reported file.",
      variant: "warning" as const,
      state: "attention" as const,
    };
  });
  const card = $derived.by((): EntityDetailCard => {
    const posterCard = entityReferenceToThumbnailCard({
      id: detail.item.remoteId,
      kind: detail.item.entityKind,
      title: detail.item.title,
    }, {
      cover: presentation?.posterUrl
        ? { src: presentation.posterUrl, alt: `${detail.item.title} poster` }
        : null,
    });
    return {
      entity: posterCard.entity,
      kindLabel,
      hero: presentation?.backdropUrl
        ? { src: presentation.backdropUrl, alt: `${detail.item.title} backdrop` }
        : null,
      poster: presentation?.posterUrl
        ? { src: presentation.posterUrl, alt: `${detail.item.title} poster` }
        : null,
      posterCard,
      description: presentation?.overview ?? null,
      rating: null,
      flags: [],
      tags: (presentation?.genres ?? []).map((genre) => ({
        id: `source-genre:${genre}`,
        kind: ENTITY_KIND.tag,
        title: genre,
        href: null,
      })),
      links: [],
      providerIdentity: null,
      files: [],
      presentCapabilities: [],
    };
  });
  const sections: EntityDetailSection[] = [
    { id: overviewSectionId },
    { id: filesSectionId },
    { id: librarySectionId },
  ];
  const tabs = $derived.by((): EntityDetailTab[] => [
    { id: "overview", label: "Overview", icon: Server, sections: ["description", "tags", overviewSectionId] },
    { id: "files", label: "Files", count: detail.files.length, icon: FileText, sections: [filesSectionId] },
    ...(isTrackable
      ? [{ id: "library", label: "Library link", icon: Link2, sections: [librarySectionId] }]
      : []),
  ]);
  const actionButtons = $derived.by((): EntityDetailActionButton[] => hasMissingMapping && canConfigureMapping ? [{
    id: "set-up-library-folder",
    label: "Set up library folder",
    ariaLabel: "Set up library folder",
    title: `Map a ${connection.name} folder to the same folder mounted for Prismedia`,
    icon: FolderCog,
    onClick: () => { mappingOpen = true; },
    variant: "primary",
  }] : []);

  function setMappingOpen(value: boolean) {
    const wasOpen = mappingOpen;
    mappingOpen = value;
    if (wasOpen && !value) onRecheckAvailability?.();
  }

  $effect(() => {
    detail.item.remoteId;
    visibleFiles = INITIAL_FILE_LIMIT;
  });

  function fileName(file: ManagedLibraryFile): string {
    return file.path.split("/").filter(Boolean).at(-1) ?? file.path;
  }

  function targetLabel(target: ManagedLibraryFile["targets"][number]): string {
    if (target.issueLabel != null) return `#${target.issueLabel}: ${target.title}`;
    if (target.seasonNumber != null && target.episodeNumber != null) {
      return `S${target.seasonNumber} E${target.episodeNumber}: ${target.title}`;
    }
    return target.title;
  }

  function observedAt(value: string): string {
    return new Date(value).toLocaleString();
  }

  function formatRuntimeMinutes(value: number | string | null | undefined): string | null {
    if (value == null) return null;
    const minutes = Number(value);
    if (!Number.isFinite(minutes) || minutes <= 0) return null;
    const hours = Math.floor(minutes / 60);
    const remainingMinutes = Math.floor(minutes % 60);
    if (hours === 0) return `${remainingMinutes}m`;
    return remainingMinutes === 0 ? `${hours}h` : `${hours}h ${remainingMinutes}m`;
  }

  function localEvidence(file: ManagedLibraryFile): MappedLibraryFile | null {
    const matches = localFiles?.filter((candidate) => candidate.remoteId === file.remoteId) ?? [];
    return matches.find((candidate) => candidate.isReadable && candidate.sizeMatches) ?? matches[0] ?? null;
  }
</script>

{#snippet heroMeta()}
  <span class="flex flex-wrap items-center gap-x-2 gap-y-1">
    <span>{kindLabel} in {connection.name}</span>
    {#if detail.item.year != null}<span aria-hidden="true">·</span><span>{detail.item.year}</span>{/if}
    {#if runtimeLabel}<span aria-hidden="true">·</span><span>{runtimeLabel}</span>{/if}
    {#if profile}<span aria-hidden="true">·</span><span>Profile: {profile}</span>{/if}
  </span>
{/snippet}

{#snippet heroBadges()}
  <Badge variant={localSummary.variant}>
    {#if localSummary.state === "readable"}<CheckCircle2 aria-hidden="true" />
    {:else if localSummary.state === "attention"}<CircleAlert aria-hidden="true" />
    {:else if localSummary.state === "setup"}<FolderCog aria-hidden="true" />
    {:else if localSummary.state === "checking"}<LoaderCircle class="animate-spin" aria-hidden="true" />
    {:else if localSummary.state === "empty"}<Clock3 aria-hidden="true" />
    {:else}<HardDrive aria-hidden="true" />{/if}
    {localSummary.label}
  </Badge>
  {#if presentation?.contentRating}
    <Badge variant="outline">{presentation.contentRating}</Badge>
  {/if}
{/snippet}

{#snippet overviewContent()}
  <div class="holding-overview-grid">
    <MetadataCard title="Availability" icon={HardDrive} wide>
      <div class="space-y-3">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <strong class="text-sm font-semibold text-text-primary">{localSummary.label}</strong>
          {#if localFiles !== null && detail.files.length > 0 && !checking}
            <span class="font-mono text-xs text-text-muted">{verifiedFileCount}/{detail.files.length} readable</span>
          {/if}
        </div>
        <p class="text-sm leading-relaxed text-text-muted">{localSummary.detail}</p>
        <p class="text-xs text-text-disabled">Prismedia matches each source path to a configured folder and opens the file read-only. It does not import, copy, or move files.</p>
      </div>
    </MetadataCard>
    <MetadataCard title="Source details" icon={Server} rows={[
      { label: "Source", value: connection.name },
      { label: "Media type", value: kindLabel },
      ...(profile ? [{ label: "Profile", value: profile }] : []),
      ...(runtimeLabel ? [{ label: "Runtime", value: runtimeLabel }] : []),
      ...(presentation?.contentRating ? [{ label: "Content rating", value: presentation.contentRating }] : []),
      { label: "Files reported", value: String(detail.files.length) },
      { label: "Observed", value: observedAt(detail.observedAt) },
    ]} />
    <MetadataCard title="Source folder" icon={FolderOpen} wide stacked monospace rows={[
      { label: `${connection.name} folder`, value: detail.path },
    ]} />
  </div>
{/snippet}

{#snippet filesContent()}
  <div class="space-y-3">
    <p class="text-sm text-text-muted">
      Files reported by {connection.name}. Prismedia checks mapped folders automatically when this page opens or refreshes.
    </p>
    {#if detail.files.length}
      <div class="files-list grid gap-3">
        {#each visibleReportedFiles as file (file.remoteId)}
          {@const local = localEvidence(file)}
          <Panel class="file-row min-w-0 p-4">
            <div class="file-glyph"><FileText aria-hidden="true" /></div>
            <div class="min-w-0 space-y-2">
              <div class="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
                <div class="min-w-0">
                  <h3 class="break-all text-sm font-medium">{fileName(file)}</h3>
                  <p class="mt-1 break-all font-mono text-xs text-text-muted">{file.path}</p>
                </div>
                <span class="shrink-0 font-mono text-xs text-text-muted">{formatBytes(Number(file.sizeBytes))}</span>
              </div>
              <div class="flex flex-wrap gap-x-3 gap-y-1 text-xs text-text-muted">
                <span>{file.targets.length} content {file.targets.length === 1 ? "target" : "targets"}</span>
                {#if local}
                  <span>{local.isReadable && local.sizeMatches ? "Readable at mapped path · size matches" : local.problem ?? "Mapped file could not be verified"}</span>
                {/if}
              </div>
              {#if file.targets.length}
                <div class="flex flex-wrap gap-1.5">
                  {#each file.targets.slice(0, 6) as target (target.remoteId)}
                    <Badge variant="outline" class="max-w-full truncate">{targetLabel(target)}</Badge>
                  {/each}
                  {#if file.targets.length > 6}<Badge variant="outline">+{file.targets.length - 6} more</Badge>{/if}
                </div>
              {/if}
            </div>
          </Panel>
        {/each}
      </div>
      {#if remainingFileCount > 0}
        <Button variant="secondary" onclick={() => visibleFiles += FILE_PAGE_SIZE}>
          <FileCheck2 />Show {Math.min(FILE_PAGE_SIZE, remainingFileCount)} more files
        </Button>
      {/if}
    {:else}
      <Panel class="p-6 text-center">
        <Clock3 class="mx-auto size-5 text-text-muted" />
        <h3 class="mt-3 text-sm font-semibold">No final files reported</h3>
        <p class="mx-auto mt-1 max-w-md text-sm text-text-muted">{connection.name} is still waiting for or organizing this title.</p>
      </Panel>
    {/if}
  </div>
{/snippet}

{#snippet libraryContent()}
  <div class="space-y-3">
    <div>
      <h2 class="text-base font-semibold">Link to your Prismedia library</h2>
      <p class="mt-1 text-sm text-text-muted">Keep this title aligned with scanned Prismedia items while {connection.name} continues to organize its files.</p>
    </div>
    {#key detail.item.remoteId}
      <ManagedHoldingTracking connectionId={connection.id} item={detail.item} {showControls} {canControl} {canRelease} />
    {/key}
  </div>
{/snippet}

{#snippet sectionContent(section: EntityDetailSection)}
  {#if section.id === overviewSectionId}
    {@render overviewContent()}
  {:else if section.id === filesSectionId}
    {@render filesContent()}
  {:else if section.id === librarySectionId}
    {@render libraryContent()}
  {/if}
{/snippet}

<EntityDetail
  {card}
  posterSize="medium"
  showFlagActions={false}
  {tabs}
  {sections}
  {heroMeta}
  {heroBadges}
  {actionButtons}
  {sectionContent}
/>

<DialogBase.Root open={mappingOpen} onOpenChange={setMappingOpen}>
  <DialogBase.Content class="max-h-[85dvh] overflow-y-auto sm:max-w-2xl">
    <DialogBase.Header>
      <DialogBase.Title>Library folder setup · {connection.name}</DialogBase.Title>
      <DialogBase.Description>Map the folder reported by {connection.name} to the same folder mounted on the Prismedia server. The files stay where they are and remain under {connection.name}'s control.</DialogBase.Description>
    </DialogBase.Header>
    {#if mappingOpen}<ExternalLibraryMappings {connection} kind={detail.item.entityKind} />{/if}
    <DialogBase.Footer><Button variant="outline" onclick={() => setMappingOpen(false)}>Done</Button></DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>

<style>
  .holding-overview-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
    gap: calc(var(--spacing) * 3);
  }

  .files-list :global(.file-row) {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    gap: 0.8rem;
  }

  .file-glyph {
    display: grid;
    width: 2rem;
    height: 2rem;
    place-items: center;
    border-radius: var(--radius-xs);
    background: var(--color-surface-3);
    color: var(--color-text-muted);
  }

  .file-glyph :global(svg) {
    width: 1rem;
    height: 1rem;
  }
</style>
