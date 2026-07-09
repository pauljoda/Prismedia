<script lang="ts">
  import { CalendarRange } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { CAPABILITY_KIND } from "$lib/entities/entity-codes";
  import {
    AVAILABILITY_FILTER_DEFS,
    BOOK_FORMAT_FILTER_DEFS,
    BOOK_TYPE_FILTER_DEFS,
    type EntityGridFilterOption,
  } from "$lib/entities/entity-grid";

  interface Props {
    activeFilterIds: string[];
    filterOptions: EntityGridFilterOption[];
    entityKind?: string;
    /** Hide the book type/format chips (constrained views that already lock those filters). */
    lockBookFilters?: boolean;
    onActiveFilterIdsChange: (ids: string[]) => void;
  }

  let {
    activeFilterIds,
    filterOptions,
    entityKind,
    lockBookFilters = false,
    onActiveFilterIdsChange,
  }: Props = $props();

  // Adaptive engagement-status labels. Reading kinds phrase the status as
  // Read/Unread/Reading; everything else uses Watched/Unwatched/In progress.
  const READING_KINDS = new Set(["book", "book-volume", "book-chapter"]);
  const statusChoices = $derived(
    READING_KINDS.has(entityKind ?? "")
      ? [
          { id: "status:watched", label: "Read" },
          { id: "status:unwatched", label: "Unread" },
          { id: "status:in-progress", label: "Reading" },
        ]
      : [
          { id: "status:watched", label: "Watched" },
          { id: "status:unwatched", label: "Unwatched" },
          { id: "status:in-progress", label: "In progress" },
        ],
  );

  const activeSet = $derived(new Set(activeFilterIds));
  const optionMap = $derived(new Map(filterOptions.map((option) => [option.id, option])));

  const hasTechnicalFilters = $derived(
    filterOptions.some((option) => option.capabilityKind === CAPABILITY_KIND.technical),
  );
  const hasRatingFilters = $derived(
    filterOptions.some((option) => option.capabilityKind === CAPABILITY_KIND.rating),
  );
  const hasDateFilters = $derived(
    filterOptions.some((option) => option.capabilityKind === CAPABILITY_KIND.dates),
  );
  const hasAvailabilityFilters = $derived(
    filterOptions.some((option) => AVAILABILITY_FILTER_DEFS.some((definition) => definition.id === option.id)),
  );
  const hasFlagFilters = $derived(
    filterOptions.some((option) => option.capabilityKind === CAPABILITY_KIND.flags),
  );

  // Only kinds that record playback or reading progress get the status filter;
  // for taxonomy and standalone media it would never match. When the kind is
  // unknown (mixed surfaces) the control is shown so it stays discoverable.
  const ENGAGEMENT_KINDS = new Set([
    "video",
    "video-series",
    "video-season",
    "audio-library",
    "audio-track",
    "book",
    "book-volume",
    "book-chapter",
  ]);
  const showStatus = $derived(entityKind == null || ENGAGEMENT_KINDS.has(entityKind));

  // Book type/format are properties of the book detail row, so they only make sense on the
  // Books grid; the server resolves them across the whole library.
  const showBookFilters = $derived(entityKind === "book" && !lockBookFilters);

  // Taxonomy kinds (tags/people/studios) can be filtered by whether anything references them,
  // resolved server-side across the whole library. The two chips are mutually exclusive — neither
  // selected means "all".
  const showReferenceFilters = $derived(
    entityKind === "tag" || entityKind === "person" || entityKind === "studio",
  );
  const REFERENCE_FILTER_IDS = ["taxonomy:referenced", "taxonomy:orphaned"];
  const AVAILABILITY_FILTER_IDS = AVAILABILITY_FILTER_DEFS.map((definition) => definition.id);

  const resolutions = ["4K", "1080p", "720p", "480p"];
  const durationChoices = [
    { id: "lt300", label: "< 5 min" },
    { id: "300-900", label: "5-15 min" },
    { id: "900-1800", label: "15-30 min" },
    { id: "gte1800", label: "30+ min" },
  ];
  const codecs = [
    { id: "h264", label: "H.264" },
    { id: "h265", label: "HEVC" },
    { id: "av1", label: "AV1" },
    { id: "vp9", label: "VP9" },
    { id: "vp8", label: "VP8" },
    { id: "mpeg4", label: "MPEG-4" },
    { id: "prores", label: "ProRes" },
    { id: "wmv", label: "WMV" },
  ];
  const ratingValues = [1, 2, 3, 4, 5];

  function isActive(id: string): boolean {
    return activeSet.has(id);
  }

  function toggleFilter(id: string) {
    onActiveFilterIdsChange(
      isActive(id)
        ? activeFilterIds.filter((filterId) => filterId !== id)
        : [...activeFilterIds, id],
    );
  }

  function replaceRangeFilter(prefix: string, value: string) {
    const next = activeFilterIds.filter((id) => !id.startsWith(prefix));
    if (value) next.push(`${prefix}${value}`);
    onActiveFilterIdsChange(next);
  }

  // Toggles one id within a mutually exclusive group: selecting it clears the others, and
  // re-selecting the active one clears the group back to "all".
  function toggleExclusive(id: string, group: string[]) {
    const without = activeFilterIds.filter((filterId) => !group.includes(filterId));
    onActiveFilterIdsChange(isActive(id) ? without : [...without, id]);
  }

  function chipClass(id: string, variant: "accent" | "info" = "accent"): string {
    const activeClass = variant === "info" ? "tag-chip-info" : "tag-chip-accent";
    const hoverClass =
      variant === "info"
        ? "tag-chip-default hover:tag-chip-info"
        : "tag-chip-default hover:tag-chip-accent";
    return cn("tag-chip cursor-pointer transition-colors duration-fast", isActive(id) ? activeClass : hoverClass);
  }

  function countFor(id: string): number | null {
    return optionMap.get(id)?.count ?? null;
  }

  function dateValue(prefix: string): string {
    return activeFilterIds.find((id) => id.startsWith(prefix))?.slice(prefix.length) ?? "";
  }
</script>

<div class="surface-well mt-px p-3">
  <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
    {#if hasTechnicalFilters}
      <section>
        <div class="mb-2 text-kicker">Resolution</div>
        <div class="flex flex-wrap gap-1">
          {#each resolutions as resolution (resolution)}
            {@const id = `technical:resolution:${resolution}`}
            <button type="button" class={chipClass(id)} onclick={() => toggleFilter(id)}>
              {resolution}
              {#if countFor(id) != null}<span class="ml-1 text-text-disabled">{countFor(id)}</span>{/if}
            </button>
          {/each}
        </div>
      </section>
    {/if}

    {#if hasRatingFilters}
      <section>
        <div class="mb-2 text-kicker">Rating</div>
        <div class="space-y-2">
          <div class="font-mono text-[0.6rem] uppercase tracking-wider text-text-disabled">At least</div>
          <div class="flex flex-wrap gap-1">
            {#each ratingValues as value (value)}
              {@const id = `rating:min:${value}`}
              <button type="button" class={chipClass(id)} onclick={() => toggleFilter(id)}>
                {value}★+
              </button>
            {/each}
          </div>
          <div class="font-mono text-[0.6rem] uppercase tracking-wider text-text-disabled">At most</div>
          <div class="flex flex-wrap gap-1">
            {#each ratingValues as value (value)}
              {@const id = `rating:max:${value}`}
              <button type="button" class={chipClass(id)} onclick={() => toggleFilter(id)}>
                ≤{value}★
              </button>
            {/each}
          </div>
          <div class="flex flex-wrap gap-1">
            <button type="button" class={chipClass("rating:unrated")} onclick={() => toggleFilter("rating:unrated")}>
              Unrated
            </button>
          </div>
        </div>
      </section>
    {/if}

    {#if hasDateFilters}
      <section>
        <div class="mb-2 text-kicker">Date</div>
        <div class="flex flex-col gap-2">
          <label class="date-row">
            <CalendarRange class="h-3 w-3 shrink-0 text-text-disabled" />
            <span>From</span>
            <input
              type="date"
              value={dateValue("dates:from:")}
              onchange={(event) => replaceRangeFilter("dates:from:", (event.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <label class="date-row">
            <CalendarRange class="h-3 w-3 shrink-0 text-text-disabled" />
            <span>To</span>
            <input
              type="date"
              value={dateValue("dates:to:")}
              onchange={(event) => replaceRangeFilter("dates:to:", (event.currentTarget as HTMLInputElement).value)}
            />
          </label>
        </div>
      </section>
    {/if}

    {#if hasTechnicalFilters}
      <section>
        <div class="mb-2 text-kicker">Duration</div>
        <div class="flex flex-wrap gap-1">
          {#each durationChoices as duration (duration.id)}
            {@const id = `technical:duration:${duration.id}`}
            <button type="button" class={chipClass(id)} onclick={() => toggleFilter(id)}>
              {duration.label}
            </button>
          {/each}
        </div>
      </section>
    {/if}

    {#if showStatus}
      <section>
        <div class="mb-2 text-kicker">Status</div>
        <div class="flex flex-wrap gap-1">
          {#each statusChoices as item (item.id)}
            <button type="button" class={chipClass(item.id)} onclick={() => toggleFilter(item.id)}>
              {item.label}
            </button>
          {/each}
        </div>
      </section>
    {/if}

    {#if showBookFilters}
      <section>
        <div class="mb-2 text-kicker">Type</div>
        <div class="flex flex-wrap gap-1">
          {#each BOOK_TYPE_FILTER_DEFS as type (type.id)}
            <button type="button" class={chipClass(type.id)} onclick={() => toggleFilter(type.id)}>
              {type.label}
            </button>
          {/each}
        </div>
      </section>

      <section>
        <div class="mb-2 text-kicker">Format</div>
        <div class="flex flex-wrap gap-1">
          {#each BOOK_FORMAT_FILTER_DEFS as format (format.id)}
            <button type="button" class={chipClass(format.id)} onclick={() => toggleFilter(format.id)}>
              {format.label}
            </button>
          {/each}
        </div>
      </section>
    {/if}

    {#if hasAvailabilityFilters}
      <section>
        <div class="mb-2 text-kicker">Availability</div>
        <div class="flex flex-wrap gap-1">
          {#each AVAILABILITY_FILTER_DEFS as item (item.id)}
            <button type="button" class={chipClass(item.id)} onclick={() => toggleExclusive(item.id, AVAILABILITY_FILTER_IDS)}>
              {item.label}
            </button>
          {/each}
        </div>
      </section>
    {/if}

    {#if showReferenceFilters}
      <section>
        <div class="mb-2 text-kicker">References</div>
        <div class="flex flex-wrap gap-1">
          <button
            type="button"
            class={chipClass("taxonomy:referenced")}
            onclick={() => toggleExclusive("taxonomy:referenced", REFERENCE_FILTER_IDS)}
          >
            Has references
          </button>
          <button
            type="button"
            class={chipClass("taxonomy:orphaned")}
            onclick={() => toggleExclusive("taxonomy:orphaned", REFERENCE_FILTER_IDS)}
          >
            No references
          </button>
        </div>
      </section>
    {/if}

    {#if hasFlagFilters}
      <section>
        <div class="mb-2 text-kicker">Library Flags</div>
        <div class="flex flex-wrap gap-1">
          {#each [
            { id: "flags:favorite", label: "Favorites" },
            { id: "flags:organized:true", label: "Organized" },
            { id: "flags:organized:false", label: "Not organized" },
            { id: "flags:nsfw:true", label: "Is NSFW" },
            { id: "flags:nsfw:false", label: "Not NSFW" },
          ] as item (item.id)}
            <button type="button" class={chipClass(item.id)} onclick={() => toggleFilter(item.id)}>
              {item.label}
              {#if countFor(item.id) != null}<span class="ml-1 text-text-disabled">{countFor(item.id)}</span>{/if}
            </button>
          {/each}
        </div>
      </section>
    {/if}

    {#if hasTechnicalFilters}
      <section>
        <div class="mb-2 text-kicker">Codec</div>
        <div class="flex flex-wrap gap-1">
          {#each codecs as codec (codec.id)}
            {@const id = `technical:codec:${codec.id}`}
            <button type="button" class={chipClass(id)} onclick={() => toggleFilter(id)}>
              {codec.label}
              {#if countFor(id) != null}<span class="ml-1 text-text-disabled">{countFor(id)}</span>{/if}
            </button>
          {/each}
        </div>
      </section>
    {/if}
  </div>
</div>

<style>
  .surface-well {
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
  }

  .date-row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    color: var(--color-text-muted);
    font-size: 0.7rem;
  }

  .date-row span {
    width: 2.5rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  .date-row input {
    min-width: 0;
    flex: 1;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-1, #0c0f15);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-primary);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    padding: 0.35rem 0.5rem;
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .date-row input:focus {
    border-color: var(--color-border-accent, rgba(242, 194, 106, 0.25));
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30), 0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15);
    outline: none;
  }

  :global(.tag-chip) {
    border-radius: var(--radius-xs, 4px) !important;
  }
</style>
