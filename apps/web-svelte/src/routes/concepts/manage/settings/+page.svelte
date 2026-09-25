<script lang="ts">
  import { onMount } from "svelte";
  import { SearchX, Settings2, ShieldUser } from "@lucide/svelte";
  import { Button, Empty, SearchInput, StatusLed } from "@prismedia/ui-svelte";
  import { fetchDownloadClients, fetchIndexers } from "$lib/api/acquisitions";
  import { fetchConnections } from "$lib/api/connections";
  import {
    fetchDatabaseBackups,
    fetchLibraryRoots,
    fetchSettings,
    fetchTranscodeCacheStatus,
    type SettingsCatalogResponse,
  } from "$lib/api/settings";
  import { fetchUsers } from "$lib/api/users";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import PrismPageHeader from "$lib/components/concepts/PrismPageHeader.svelte";
  import ManageConceptFrame from "$lib/components/concepts/manage/ManageConceptFrame.svelte";
  import SettingsSectionCard from "$lib/components/concepts/manage/SettingsSectionCard.svelte";
  import {
    DIAGNOSTICS_STATUS,
    SETTINGS_FAMILIES,
    acquisitionStatus,
    autoIdentifyStatus,
    backupsStatus,
    connectionsStatus,
    generationStatus,
    librariesStatus,
    playbackStatus,
    settingsBySection,
    subtitlesStatus,
    transcodeCacheStatus,
    usersStatus,
    type SectionStatus,
  } from "$lib/components/concepts/manage/settings-families";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { SETTING_SECTION, visibleSettingsSections, type SettingsSectionId } from "$lib/settings/settings-section-catalog";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  const nsfw = useNsfw();

  let query = $state("");
  let catalog = $state.raw<SettingsCatalogResponse | null>(null);
  let catalogFailed = $state(false);
  let endpointStatus = $state<Partial<Record<SettingsSectionId, SectionStatus | null>>>({});

  const sections = $derived(visibleSettingsSections(session));
  const sectionById = $derived(new Map(sections.map((section) => [section.id, section])));

  /** Sections whose status comes from their own endpoint. Each resolves independently. */
  function loadEndpointStatuses() {
    const hideNsfw = nsfw.mode === "off";
    const loaders: Partial<Record<SettingsSectionId, () => Promise<SectionStatus>>> = {
      [SETTING_SECTION.libraries]: async () =>
        librariesStatus((await fetchLibraryRoots()).filter((root) => !hideNsfw || !root.isNsfw)),
    };
    if (session.isAdmin) {
      loaders[SETTING_SECTION.users] = async () => usersStatus(await fetchUsers());
      loaders[SETTING_SECTION.acquisition] = async () => {
        const [indexers, clients] = await Promise.all([fetchIndexers(), fetchDownloadClients()]);
        return acquisitionStatus(indexers, clients);
      };
      loaders[SETTING_SECTION.connections] = async () => connectionsStatus(await fetchConnections());
      loaders[SETTING_SECTION.transcodeCache] = async () => transcodeCacheStatus(await fetchTranscodeCacheStatus());
      loaders[SETTING_SECTION.databaseBackups] = async () => backupsStatus(await fetchDatabaseBackups());
    }
    for (const [id, load] of Object.entries(loaders) as [SettingsSectionId, () => Promise<SectionStatus>][]) {
      load().then(
        (status) => (endpointStatus = { ...endpointStatus, [id]: status }),
        () => (endpointStatus = { ...endpointStatus, [id]: null }),
      );
    }
  }

  onMount(() => {
    if (!session.canManageServer) return;
    loadEndpointStatuses();
    if (session.isAdmin) {
      fetchSettings().then(
        (response) => (catalog = response),
        () => (catalogFailed = true),
      );
    }
  });

  /** Sections whose status is read from the settings catalog once it arrives. */
  const catalogStatus = $derived.by((): Partial<Record<SettingsSectionId, SectionStatus | null>> => {
    if (!catalog) {
      return catalogFailed
        ? {
            [SETTING_SECTION.playback]: null,
            [SETTING_SECTION.subtitles]: null,
            [SETTING_SECTION.generation]: null,
            [SETTING_SECTION.autoIdentify]: null,
          }
        : {};
    }
    return {
      [SETTING_SECTION.playback]: playbackStatus(catalog),
      [SETTING_SECTION.subtitles]: subtitlesStatus(catalog),
      [SETTING_SECTION.generation]: generationStatus(catalog),
      [SETTING_SECTION.autoIdentify]: autoIdentifyStatus(catalog),
    };
  });

  const statusById = $derived<Partial<Record<SettingsSectionId, SectionStatus | null>>>({
    ...endpointStatus,
    ...catalogStatus,
    [SETTING_SECTION.diagnostics]: DIAGNOSTICS_STATUS,
  });

  const catalogSettings = $derived(settingsBySection(catalog));
  const needle = $derived(query.trim().toLowerCase());

  function matchingSettings(id: SettingsSectionId): string[] {
    if (needle.length < 2) return [];
    return (catalogSettings.get(id) ?? [])
      .filter((setting) => `${setting.label} ${setting.description}`.toLowerCase().includes(needle))
      .map((setting) => setting.label);
  }

  const families = $derived(
    SETTINGS_FAMILIES.map((family, order) => {
      const cards = family.sections
        .map((id) => sectionById.get(id))
        .filter((section) => section !== undefined)
        .map((section) => {
          const matches = matchingSettings(section.id);
          const status = statusById[section.id];
          const haystack = [family.title, section.title, section.description, status?.text ?? ""].join(" ").toLowerCase();
          return { section, status, matches, visible: needle === "" || haystack.includes(needle) || matches.length > 0 };
        });
      return { ...family, order, cards: cards.filter((card) => card.visible) };
    }).filter((family) => family.cards.length > 0),
  );

  const shownCount = $derived(families.reduce((sum, family) => sum + family.cards.length, 0));
  const attention = $derived(
    sections.filter((section) => {
      const led = statusById[section.id]?.led;
      return led === "warning" || led === "error";
    }).length,
  );
</script>

<svelte:head><title>Grouped settings · Concepts · Prismedia</title></svelte:head>

<ManageConceptFrame concept="Grouped settings">
  {#if !session.canManageServer}
    <StatePlaceholder icon={ShieldUser} title="Settings access required" />
  {:else}
    <PrismPageHeader icon={Settings2} title="Settings">
      {#snippet status()}
        <span class="text-caption text-text-muted">
          <span class="font-mono text-text-secondary">{sections.length}</span> sections
        </span>
        {#if attention > 0}
          <span class="flex items-center gap-2 text-caption text-text-secondary">
            <StatusLed status="warning" size="sm" />
            <span class="font-mono text-text-primary">{attention}</span> need attention
          </span>
        {/if}
      {/snippet}
    </PrismPageHeader>

    <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
      <SearchInput
        bind:value={query}
        name="settings-filter"
        ariaLabel="Filter settings"
        placeholder="Filter sections and settings…"
        class="w-full sm:max-w-sm"
      />
      {#if needle}
        <p class="font-mono text-[0.72rem] text-text-muted" role="status">
          {shownCount}/{sections.length}
        </p>
      {/if}
    </div>

    {#if families.length === 0}
      <Empty.Root class="rounded-[var(--radius-md)] border border-dashed border-[var(--color-border-default)] py-12">
        <Empty.Header>
          <Empty.Media variant="icon"><SearchX /></Empty.Media>
          <Empty.Title>No matches</Empty.Title>
        </Empty.Header>
        <Empty.Content>
          <Button variant="outline" size="sm" onclick={() => (query = "")}>Clear filter</Button>
        </Empty.Content>
      </Empty.Root>
    {/if}

    {#each families as family (family.id)}
      <section class="flex flex-col gap-3" aria-labelledby="settings-family-{family.id}">
        <div class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
          <span class="font-mono text-[0.68rem] text-text-disabled">{String(family.order + 1).padStart(2, "0")}</span>
          <h2 id="settings-family-{family.id}" class="font-heading text-base font-semibold text-text-primary">{family.title}</h2>
        </div>
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {#each family.cards as card (card.section.id)}
            <SettingsSectionCard section={card.section} status={card.status} matches={card.matches} />
          {/each}
        </div>
      </section>
    {/each}
  {/if}
</ManageConceptFrame>
