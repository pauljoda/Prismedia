<script lang="ts">
  import { ArrowUpRight } from "@lucide/svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { Button, cn } from "@prismedia/ui-svelte";
  import BackLink from "$lib/components/BackLink.svelte";
  import SettingsSectionPage from "$lib/components/settings/SettingsSectionPage.svelte";
  import {
    SETTING_SECTION,
    visibleSettingsSections,
    type SettingsSection,
    type SettingsSectionId,
  } from "$lib/settings/settings-section-catalog";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();

  /** Rail groups, in reading order. Sections not listed fall into the last group. */
  const GROUPS: ReadonlyArray<{ title: string; ids: readonly SettingsSectionId[] }> = [
    { title: "Library", ids: [SETTING_SECTION.libraries, SETTING_SECTION.autoIdentify, SETTING_SECTION.generation] },
    { title: "Acquisition", ids: [SETTING_SECTION.acquisition, SETTING_SECTION.connections] },
    { title: "Playback", ids: [SETTING_SECTION.playback, SETTING_SECTION.subtitles, SETTING_SECTION.transcodeCache] },
    { title: "Server", ids: [SETTING_SECTION.users, SETTING_SECTION.databaseBackups, SETTING_SECTION.diagnostics] },
  ];

  /** Sections with their own full pages open there instead of in the pane. */
  const STANDALONE: ReadonlySet<string> = new Set([SETTING_SECTION.users, SETTING_SECTION.connections]);

  const sections = $derived(visibleSettingsSections(session));
  const groups = $derived(
    GROUPS.map((group) => ({
      title: group.title,
      sections: group.ids
        .map((id) => sections.find((section) => section.id === id))
        .filter((section): section is SettingsSection => Boolean(section)),
    })).filter((group) => group.sections.length > 0),
  );
  const activeId = $derived.by(() => {
    const requested = page.url.searchParams.get("section");
    const inline = sections.filter((section) => !STANDALONE.has(section.id));
    return inline.find((section) => section.id === requested)?.id ?? inline[0]?.id ?? null;
  });

  function open(section: SettingsSection) {
    if (STANDALONE.has(section.id)) {
      void goto(section.href);
      return;
    }
    void goto(`?section=${section.id}`, { replaceState: true, noScroll: true, keepFocus: true });
  }
</script>

<svelte:head><title>Settings rail · Concepts · Prismedia</title></svelte:head>

{#snippet railItem(section: SettingsSection, compact: boolean)}
  {@const Icon = section.icon}
  {@const active = section.id === activeId}
  <Button
    variant="ghost"
    size="sm"
    class={cn(
      "relative justify-start gap-2.5 font-normal",
      compact ? "shrink-0" : "w-full",
      active ? "bg-[var(--color-surface-3)] text-text-primary" : "text-text-secondary",
    )}
    aria-current={active ? "page" : undefined}
    onclick={() => open(section)}
  >
    {#if active && !compact}
      <span class="absolute inset-y-1.5 left-0 w-[2px] rounded-full bg-[var(--color-text-primary)]" aria-hidden="true"></span>
    {/if}
    <Icon class="size-4" color={active ? section.accent : undefined} aria-hidden="true" />
    <span class="truncate">{section.title}</span>
    {#if STANDALONE.has(section.id)}
      <ArrowUpRight class="ml-auto size-3.5 text-text-disabled" aria-hidden="true" />
    {/if}
  </Button>
{/snippet}

<div class="mx-auto flex w-full max-w-7xl min-w-0 flex-col gap-4 pb-16">
  <BackLink fallback="/concepts" label="Concepts" variant="text" />

  <!-- Phones: the rail becomes one scrolling row above the section. -->
  <nav class="-mx-3 flex gap-1 overflow-x-auto px-3 pb-1 scrollbar-hidden lg:hidden" aria-label="Settings sections">
    {#each groups as group (group.title)}
      {#each group.sections as section (section.id)}
        {@render railItem(section, true)}
      {/each}
    {/each}
  </nav>

  <div class="grid min-w-0 gap-8 lg:grid-cols-[14rem_minmax(0,1fr)]">
    <nav class="hidden lg:block" aria-label="Settings sections">
      <div class="sticky top-20 flex flex-col gap-5">
        {#each groups as group (group.title)}
          <div class="flex flex-col gap-0.5">
            <p class="px-2.5 pb-1 font-mono text-[0.6rem] uppercase tracking-[0.14em] text-text-disabled">{group.title}</p>
            {#each group.sections as section (section.id)}
              {@render railItem(section, false)}
            {/each}
          </div>
        {/each}
      </div>
    </nav>

    <div class="min-w-0">
      {#if activeId}
        {#key activeId}
          <SettingsSectionPage sectionId={activeId} embedded />
        {/key}
      {/if}
    </div>
  </div>
</div>
