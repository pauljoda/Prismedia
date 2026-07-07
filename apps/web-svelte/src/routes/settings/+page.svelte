<script lang="ts">
  import { ChevronRight, ShieldUser } from "@lucide/svelte";
  import { Panel, cn } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { useSession } from "$lib/stores/session.svelte";
  import {
    settingsDirectoryIcon,
    visibleSettingsSections,
  } from "$lib/settings/settings-section-catalog";

  const session = useSession();
  const sections = $derived(visibleSettingsSections(session));
  const SettingsIcon = settingsDirectoryIcon;
</script>

<svelte:head>
  <title>Settings · Prismedia</title>
</svelte:head>

{#if !session.canManageServer}
  <StatePlaceholder
    icon={ShieldUser}
    title="Settings access required"
    description="Ask an administrator for access to manage server settings or libraries."
  />
{:else}
  <div class="space-y-6">
    <div>
      <h1 class="flex items-center gap-2.5">
        <SettingsIcon class="h-5 w-5 text-text-accent" />
        Settings
      </h1>
      <p class="mt-1 text-[0.78rem] text-text-muted">
        Choose a section to configure libraries, acquisition, playback, and server maintenance
      </p>
    </div>

    <Panel>
      <div class="divide-y divide-border-subtle">
        {#each sections as section (section.id)}
          {@const SectionIcon = section.icon}
          <a
            href={section.href}
            class={cn(
              "group flex items-center gap-4 px-5 py-4 transition-colors duration-fast",
              "hover:bg-surface-2/45 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500/30",
            )}
          >
            <span
              class="flex size-10 shrink-0 items-center justify-center rounded-sm border border-border-subtle bg-surface-1 text-text-muted shadow-well transition-all duration-fast group-hover:border-border-accent/45 group-hover:text-text-accent group-hover:shadow-[var(--shadow-glow-accent)]"
            >
              <SectionIcon class="h-4 w-4" />
            </span>
            <span class="min-w-0 flex-1">
              <span class="text-sm font-medium text-text-primary">{section.title}</span>
              <span class="mt-1 block text-[0.72rem] leading-relaxed text-text-muted">
                {section.description}
              </span>
            </span>
            <ChevronRight class="h-4 w-4 shrink-0 text-text-disabled transition-colors duration-fast group-hover:text-text-accent" />
          </a>
        {/each}
      </div>
    </Panel>
  </div>
{/if}
