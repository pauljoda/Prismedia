import { fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { FileText, Play } from "@lucide/svelte";
import { createRawSnippet } from "svelte";
import { describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, CAPABILITY_KIND } from "$lib/api/generated/codes";
import type { EntityDetailCard, EntityDetailCardFull } from "$lib/entities/entity-detail";
import type { EntityDetailSection } from "./EntityDetail.svelte";
import EntityDetail from "./EntityDetail.test-harness.svelte";

function buildCard(): EntityDetailCard {
  return {
    entity: {
      id: "video-1",
      kind: "video",
      title: "Big Buck Bunny",
      subtitle: null,
      thumbnailUrl: null,
      href: "/videos/video-1",
      parentEntityId: null,
      capabilities: [],
      childrenByKind: [],
    },
    kindLabel: "Video",
    hero: null,
    poster: null,
    posterCard: null,
    description: null,
    rating: { value: 0, max: 5 },
    flags: [
      { code: "favorite", label: "Favorite", active: false },
      { code: "organized", label: "Organized", active: false },
    ],
    tags: [],
    links: [],
    providerIdentity: null,
    files: [],
    presentCapabilities: [],
  } as EntityDetailCard;
}

describe("EntityDetail", () => {
  it("moves detail-tab focus with arrow keys before committing with Enter", async () => {
    const card = buildCard();
    card.description = "Details content";
    card.links = [{ label: "Website", url: "https://example.test" }];
    render(EntityDetail, { card, tabs: [
      { id: "details", label: "Details", sections: ["description"] },
      { id: "links", label: "Links", sections: ["links"] },
    ] });
    const details = screen.getByRole("tab", { name: "Details" });
    const links = screen.getByRole("tab", { name: "Links" });
    details.focus();
    await fireEvent.keyDown(details, { key: "ArrowRight" });
    expect(links).toHaveFocus();
    expect(details).toHaveAttribute("aria-selected", "true");
    await fireEvent.keyDown(links, { key: "Enter" });
    expect(links).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tabpanel", { name: "Links" })).toBeInTheDocument();
  });

  it("renders configured hero action buttons with shared styling", async () => {
    const onClick = vi.fn();
    render(EntityDetail, {
      props: {
        card: buildCard(),
        actionButtons: [
          {
            id: "play-all",
            label: "Play All",
            icon: Play,
            iconFill: "currentColor",
            variant: "primary",
            onClick,
          },
        ],
      },
    });

    const action = screen.getByRole("button", { name: "Play All" });
    expect(action).toHaveClass("h-control-lg", "gap-control-gap", "px-control-pad-lg");
    await fireEvent.click(action);

    expect(onClick).toHaveBeenCalledOnce();
  });

  it("renders the explicit provider identity as an external hero chip beside route badges", () => {
    const card = buildCard();
    card.providerIdentity = {
      pluginId: "metadata-router",
      identityNamespace: "CaseSensitive",
      identityValue: "Show:AbC:01:5",
      url: "https://provider.test/items/Show%3AAbC%3A01%3A5",
    };

    const { container } = render(EntityDetail, {
      props: {
        card,
        heroBadges: createRawSnippet(() => ({
          render: () => '<span class="hero-badge wanted">Wanted</span>',
        })),
      },
    });

    const chip = screen.getByRole("link", {
      name: "Metadata and monitoring source: metadata-router, CaseSensitive ID Show:AbC:01:5. Opens provider in a new tab.",
    });
    expect(chip).toHaveAttribute("href", "https://provider.test/items/Show%3AAbC%3A01%3A5");
    expect(chip).toHaveAttribute("target", "_blank");
    expect(chip).toHaveAttribute("rel", "noopener noreferrer");
    expect(chip).toHaveAttribute(
      "title",
      "Metadata and monitoring source: metadata-router, CaseSensitive ID Show:AbC:01:5",
    );
    expect(chip.textContent?.trim()).toBe("metadata-router");
    expect(chip).not.toHaveTextContent("Show:AbC:01:5");
    expect(chip).toHaveClass("h-control", "border-border");
    expect(screen.getByText("Wanted")).toBeInTheDocument();
    expect(container.querySelector(".position-badges")?.children).toHaveLength(2);
  });

  it("renders an inert provider identity chip without promoting ordinary external IDs", () => {
    const card = buildCard();
    card.links = [{ label: "fallback: arbitrary", url: "https://fallback.test", provider: "fallback" }];
    card.providerIdentity = {
      pluginId: "source-plugin",
      identityNamespace: "opaque",
      identityValue: "Value:Keeps:Case",
      url: null,
    };

    const { unmount } = render(EntityDetail, { props: { card } });

    const chip = screen.getByLabelText(
      "Metadata and monitoring source: source-plugin, opaque ID Value:Keeps:Case",
    );
    expect(chip.tagName).toBe("SPAN");
    expect(chip.textContent?.trim()).toBe("source-plugin");
    expect(chip).not.toHaveTextContent("Value:Keeps:Case");

    unmount();

    card.providerIdentity = null;
    render(EntityDetail, { props: { card } });
    expect(screen.queryByLabelText(/Metadata and monitoring source:/)).not.toBeInTheDocument();
  });

  it("renders detail poster artwork through the shared thumbnail component", () => {
    const card = buildCard();
    card.poster = { src: "/covers/book.jpg", alt: "Cover" };
    card.posterCard = {
      aspectRatio: "poster",
      cover: { src: "/covers/book.jpg", alt: "Cover", role: "cover" },
      entity: {
        id: "book-1",
        kind: "book",
        title: "Book One",
        parentEntityId: null,
        sortOrder: null,
        capabilities: [{
          kind: CAPABILITY_KIND.flags,
          isFavorite: null,
          isNsfw: null,
          isOrganized: null,
          isWanted: true,
        }],
        childrenByKind: [],
        relationships: [],
      },
      fit: "cover",
      hover: {
        kind: "image-sequence",
        assets: [
          { src: "/pages/1.jpg", alt: "Page 1", role: "preview" },
          { src: "/pages/5.jpg", alt: "Page 5", role: "preview" },
        ],
      },
    };

    const { container } = render(EntityDetail, {
      props: {
        card,
        posterSize: "large",
        wantedStatus: ACQUISITION_STATUS.waitingForRelease,
      },
    });

    expect(container.querySelector(".poster-frame .entity-thumbnail")).toBeInTheDocument();
    expect(container.querySelector(".poster-frame img")).toHaveAttribute("src", "/covers/book.jpg");
    expect(screen.getAllByLabelText("Wanted — Waiting for release")).toHaveLength(2);
  });

  it("shows editable poster and header drop zones when artwork is missing", async () => {
    const onMetadataSave = vi.fn().mockResolvedValue(undefined);
    const onImageAssetUpload = vi.fn().mockResolvedValue(undefined);
    const { container, unmount } = render(EntityDetail, {
      props: {
        card: buildCard(),
        onMetadataSave,
        onImageAssetUpload,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit details" }));

    expect(screen.getByRole("region", { name: "Artwork" })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Edit artwork" }));
    expect(screen.getAllByText("No image")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Upload poster" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload header" })).toBeInTheDocument();
    expect(container.querySelector(".header-asset-placeholder")).not.toBeInTheDocument();
    expect(container.querySelector(".hero button[aria-label='Upload poster']")).not.toBeInTheDocument();
    expect(container.querySelector(".hero button[aria-label='Upload header']")).not.toBeInTheDocument();
    expect(container.querySelector('[data-asset-dropzone="poster"]')).toBeInTheDocument();
    expect(container.querySelector('[data-asset-dropzone="backdrop"]')).toBeInTheDocument();

    unmount();
  });

  it("keeps the poster upload target edit-only with the entity thumbnail shape", async () => {
    const card = buildCard();
    card.entity.kind = "gallery";

    const { container, unmount } = render(EntityDetail, {
      props: {
        card,
        posterSize: "none",
        onMetadataSave: vi.fn().mockResolvedValue(undefined),
        onImageAssetUpload: vi.fn().mockResolvedValue(undefined),
      },
    });

    expect(container.querySelector(".poster-frame")).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Edit details" }));

    const posterThumbnail = container.querySelector<HTMLElement>(".poster-frame .entity-thumbnail");
    const posterFrame = container.querySelector<HTMLElement>(".poster-frame");
    expect(posterFrame).toBeInTheDocument();
    expect(posterFrame?.style.aspectRatio).toBe("1 / 1");
    expect(posterThumbnail?.style.aspectRatio).toBe("1 / 1");

    unmount();
  });

  it("renders caller-provided detail tabs with section mappings and custom content", async () => {
    const card = buildCard();
    card.description = "A gentle rabbit adventure.";
    card.tags = [{ id: "tag-animation", kind: "tag", title: "animation", href: "/tags/tag-animation" }];
    card.files = [{ role: "source", path: "/media/bunny.mp4", mimeType: "video/mp4" }];

    render(EntityDetail, {
      props: {
        card,
        tabs: [
          {
            id: "details",
            label: "Details",
            sections: ["description", "tags"],
          },
          {
            id: "files",
            label: "Files",
            count: 1,
            icon: FileText,
            sections: ["custom-files"],
          },
        ],
        sections: [
          {
            id: "custom-files",
            label: "File Notes",
          },
        ],
        sectionContent: createRawSnippet<[EntityDetailSection]>((section) => ({
          render: () => (section().id === "custom-files" ? "<p>File info panel</p>" : ""),
        })),
      },
    });

    expect(screen.getByRole("tablist", { name: "Detail sections" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Details" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("A gentle rabbit adventure.")).toBeInTheDocument();
    expect(screen.getByText("animation")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("tab", { name: "Files 1" }));

    expect(screen.getByRole("tab", { name: "Files 1" })).toHaveAttribute("aria-selected", "true");
    expect(document.querySelector("svg.lucide-file-text")).toBeInTheDocument();
    expect(screen.getByText("File info panel")).toBeInTheDocument();
    expect(screen.queryByText("/media/bunny.mp4")).not.toBeInTheDocument();
    expect(screen.queryByText("A gentle rabbit adventure.")).not.toBeInTheDocument();
  });

  it("renders built-in extended metadata sections without route custom content", async () => {
    const card = {
      ...buildCard(),
      studio: { id: "studio-1", kind: "studio", title: "Blender Foundation", thumbnail: null, roles: [], characters: [] },
      credits: [
        { id: "person-1", kind: "person", title: "Sacha Goedegebure", thumbnail: null, roles: ["director"], characters: [] },
        { id: "person-2", kind: "person", title: "Nathan Vegdahl", thumbnail: null, roles: [], characters: [] },
        { id: "person-3", kind: "person", title: "Jan Morgenstern", thumbnail: null, roles: [], characters: [] },
      ],
      stats: [{ code: "views", label: "Views", value: "1842" }],
      dates: [
        { code: "release", label: "Released", value: "2008-05-30", display: "May 30, 2008", sortable: "2008-05-30" },
      ],
      technical: [{ label: "Resolution", value: "1920×1080 (1080p)" }],
      fingerprints: [{ algorithm: "oshash", value: "a1b2c3d4" }],
      markers: [],
      subtitles: [],
      progress: { index: 12, total: 18, percent: 67, unit: "episodes", mode: "watching", completed: false },
      positions: [{ code: "episode", value: 2, label: "Episode 2" }],
      classification: { value: "animation", label: "Animation", system: "content-type" },
      sources: [{ code: "stash-compat", value: "scene-42" }],
    } satisfies EntityDetailCardFull;

    render(EntityDetail, {
      props: {
        card,
        tabs: [
          {
            id: "metadata",
            label: "Metadata",
            sections: [
              "studio",
              "credits",
              "stats",
              "dates",
              "technical",
              "progress",
              "positions",
              "classification",
              "sources",
              "fingerprints",
            ],
          },
        ],
      },
    });

    expect(screen.getByRole("heading", { name: "Studio 1" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Blender Foundation" })).toHaveAttribute("href", "/studios/studio-1");
    expect(screen.getByRole("heading", { name: "Credits 3" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sacha Goedegebure" })).toHaveAttribute("href", "/people/person-1");
    expect(screen.getByText("Views")).toBeInTheDocument();
    expect(screen.getByText("1842")).toBeInTheDocument();
    expect(screen.getByText("Released")).toBeInTheDocument();
    expect(screen.getByText("May 30, 2008")).toBeInTheDocument();
    expect(screen.getByText("Resolution")).toBeInTheDocument();
    expect(screen.getByText("1920×1080 (1080p)")).toBeInTheDocument();
    expect(screen.getByText("watching")).toBeInTheDocument();
    expect(screen.getByText("Episode 2")).toBeInTheDocument();
    expect(screen.getByText("animation")).toBeInTheDocument();
    expect(screen.getByText("Stash compat")).toBeInTheDocument();
    expect(screen.getByText("oshash")).toBeInTheDocument();
  });

  it("renders reference sections with non-selectable entity thumbnails", () => {
    const card = {
      ...buildCard(),
      studio: { id: "studio-1", kind: "studio", title: "Blender Foundation", thumbnail: null, roles: [], characters: [] },
      credits: [
        { id: "person-1", kind: "person", title: "Sacha Goedegebure", thumbnail: null, roles: ["director"], characters: [] },
        { id: "person-2", kind: "person", title: "Nathan Vegdahl", thumbnail: null, roles: [], characters: [] },
        { id: "person-3", kind: "person", title: "Jan Morgenstern", thumbnail: null, roles: [], characters: [] },
      ],
      stats: [],
      dates: [],
      technical: [],
      fingerprints: [],
      markers: [],
      subtitles: [],
      progress: null,
      positions: [],
      classification: null,
      sources: [],
    } satisfies EntityDetailCardFull;

    const { container } = render(EntityDetail, {
      props: {
        card,
        tabs: [{ id: "references", label: "References", sections: ["studio", "credits"] }],
      },
    });

    const thumbnails = container.querySelectorAll(".entity-thumbnail");

    expect(thumbnails).toHaveLength(4);
    expect(screen.getByRole("region", { name: "Studio" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Credits" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Blender Foundation" })).toHaveAttribute("href", "/studios/studio-1");
    expect(screen.getByRole("link", { name: /Sacha Goedegebure/ })).toHaveAttribute("href", "/people/person-1");
    // The primary role surfaces as the credit subtitle.
    expect(screen.getByText("Director")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(container.querySelector(".selection")).not.toBeInTheDocument();
  });

  it("renders tags as links to the tag entity", () => {
    const card = buildCard();
    card.tags = [{ id: "tag-comedy", kind: "tag", title: "COMEDY", href: "/tags/tag-comedy" }];

    render(EntityDetail, { props: { card } });

    expect(screen.getByRole("link", { name: "COMEDY" })).toHaveAttribute("href", "/tags/tag-comedy");
  });

  it("edits the active tab sections and saves a scoped metadata patch", async () => {
    const card = buildCard();
    card.description = "Old description";
    card.links = [{ label: "https://example.test", url: "https://example.test" }];
    const onMetadataSave = vi.fn().mockResolvedValue(undefined);

    render(EntityDetail, {
      props: {
        card,
        tabs: [{ id: "links", label: "Links", sections: ["links"] }],
        onMetadataSave,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit Links" }));
    await fireEvent.click(screen.getByRole("button", { name: "Edit https://example.test" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "Links item" }), {
      target: { value: "https://new-link.test" },
    });
    await fireEvent.keyDown(screen.getByRole("textbox", { name: "Links item" }), { key: "Enter" });
    await fireEvent.click(screen.getByRole("button", { name: "Save Links" }));

    expect(onMetadataSave).toHaveBeenCalledWith({
      fields: ["urls", "externalIds"],
      patch: expect.objectContaining({ urls: ["https://new-link.test"] }),
    });
  });

  it("uses the shared edit grid on untabbed pages and keeps non-editable tags read-only", async () => {
    const card = buildCard();
    card.tags = [{ id: "tag-comedy", kind: "tag", title: "COMEDY", href: "/tags/tag-comedy" }];
    render(EntityDetail, {
      card,
      standaloneMetadataSectionIds: ["links"],
      sections: [{ id: "tags", label: "Tags", editable: false }],
      onMetadataSave: vi.fn().mockResolvedValue(undefined),
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit details" }));

    expect(within(screen.getByRole("region", { name: "Editable fields" })).getByRole("textbox", { name: "Title" })).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "References" })).getByRole("textbox", { name: "New Provider" })).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Add Tags" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "COMEDY" })).toBeInTheDocument();
  });

  it("excludes hidden and non-editable standalone sections from the saved patch", async () => {
    const card = { ...buildCard(), classification: { value: "Author", label: "Known for", system: "kind" } };
    const onMetadataSave = vi.fn().mockResolvedValue(undefined);
    render(EntityDetail, {
      card,
      standaloneMetadataSectionIds: ["classification"],
      sections: [
        { id: "tags", label: "Tags", editable: false },
        { id: "classification", label: "Classification", hidden: true },
      ],
      onMetadataSave,
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit details" }));
    expect(screen.queryByRole("textbox", { name: "Classification" })).not.toBeInTheDocument();
    await fireEvent.input(screen.getByRole("textbox", { name: "Title" }), { target: { value: "Updated title" } });
    await fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

    expect(onMetadataSave).toHaveBeenCalledWith({
      fields: ["title", "description", "rating", "flags"],
      patch: expect.objectContaining({ title: "Updated title" }),
    });
  });

  it("saves shared editable metadata fields through the tab patch", async () => {
    const card = {
      ...buildCard(),
      description: "Old description",
      stats: [{ code: "runtimeMinutes", label: "Runtime", value: "92" }],
      dates: [],
      technical: [],
      fingerprints: [],
      markers: [],
      subtitles: [],
      progress: null,
      positions: [{ code: "episodeNumber", value: 2, label: "Episode 2" }],
      classification: { value: "movie", label: "Movie", system: "kind" },
      sources: [],
      studio: null,
      credits: [],
    } satisfies EntityDetailCardFull;
    const onMetadataSave = vi.fn().mockResolvedValue(undefined);

    render(EntityDetail, {
      props: {
        card,
        tabs: [{ id: "details", label: "Details", sections: ["description", "stats", "positions", "classification"] }],
        onMetadataSave,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit Details" }));
    expect(screen.getByRole("region", { name: "Editable fields" })).toBeInTheDocument();
    await fireEvent.input(screen.getByRole("textbox", { name: "Title" }), {
      target: { value: "Big Buck Bunny Remastered" },
    });
    await fireEvent.input(screen.getByRole("spinbutton", { name: "Rating" }), {
      target: { value: "4.5" },
    });
    await fireEvent.click(screen.getByRole("button", { name: "Favorite" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "runtimeMinutes Value" }), { target: { value: "94" } });
    await fireEvent.input(screen.getByPlaceholderText("count"), { target: { value: "voteCount" } });
    await fireEvent.input(screen.getByPlaceholderText("12"), { target: { value: "12" } });
    await fireEvent.click(screen.getAllByRole("button", { name: "Add entry" })[0]);
    await fireEvent.input(screen.getByRole("textbox", { name: "episodeNumber Value" }), { target: { value: "3" } });
    await fireEvent.input(screen.getByRole("textbox", { name: "Classification" }), {
      target: { value: "short" },
    });
    await fireEvent.click(screen.getByRole("button", { name: "Save Details" }));

    expect(onMetadataSave).toHaveBeenCalledWith({
      fields: ["title", "description", "rating", "flags", "stats", "positions", "classification"],
      patch: expect.objectContaining({
        title: "Big Buck Bunny Remastered",
        description: "Old description",
        rating: 4.5,
        flags: { isFavorite: true, isNsfw: false, isOrganized: false },
        stats: { runtimeMinutes: 94, voteCount: 12 },
        positions: { episodeNumber: 3 },
        classification: "short",
      }),
    });
  });

  it("blocks dirty tab navigation until the user discards edits", async () => {
    const card = buildCard();
    card.description = "A visible details tab";
    card.links = [{ label: "https://example.test", url: "https://example.test" }];

    render(EntityDetail, {
      props: {
        card,
        tabs: [
          { id: "links", label: "Links", sections: ["links"] },
          { id: "details", label: "Details", sections: ["description"] },
        ],
        onMetadataSave: vi.fn().mockResolvedValue(undefined),
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit Links" }));
    expect(screen.getByRole("region", { name: "References" })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Edit https://example.test" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "Links item" }), {
      target: { value: "https://changed.test" },
    });
    await fireEvent.keyDown(screen.getByRole("textbox", { name: "Links item" }), { key: "Enter" });
    await fireEvent.click(screen.getByRole("tab", { name: "Details" }));

    expect(screen.getByRole("dialog", { name: "Discard unsaved edits?" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Links" })).toHaveAttribute("aria-selected", "true");

    await fireEvent.click(screen.getByRole("button", { name: "Stay here" }));

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Discard unsaved edits?" })).not.toBeInTheDocument());
    expect(screen.getByRole("tab", { name: "Links" })).toHaveAttribute("aria-selected", "true");

    await fireEvent.click(screen.getByRole("tab", { name: "Details" }));

    await fireEvent.click(screen.getByRole("button", { name: "Discard changes" }));

    expect(screen.getByRole("tab", { name: "Details" })).toHaveAttribute("aria-selected", "true");
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Discard unsaved edits?" })).not.toBeInTheDocument());
  });

  it("shows inline validation and disables save for invalid editable fields", async () => {
    const card = buildCard();
    card.links = [{ label: "Site", url: "https://example.test" }];

    render(EntityDetail, {
      props: {
        card,
        tabs: [{ id: "links", label: "Links", sections: ["links"] }],
        onMetadataSave: vi.fn().mockResolvedValue(undefined),
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit Links" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "Add item" }), { target: { value: "not-a-url" } });
    await fireEvent.click(screen.getByRole("button", { name: "Add item" }));

    expect(screen.getByText("Invalid URL")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save Links" })).toBeDisabled();
  });

  it("edits external IDs separately from URL links", async () => {
    const card = buildCard();
    card.links = [
      { label: "https://example.test", url: "https://example.test" },
      { label: "tmdb: 6515881", url: null, provider: "tmdb" },
    ];
    const onMetadataSave = vi.fn().mockResolvedValue(undefined);

    render(EntityDetail, {
      props: {
        card,
        tabs: [{ id: "links", label: "Links", sections: ["links"] }],
        onMetadataSave,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Edit Links" }));

    expect(screen.queryByText("Links must be absolute http or https URLs.")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit https://example.test" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "tmdb ID" })).toHaveValue("6515881");

    await fireEvent.input(screen.getByRole("textbox", { name: "tmdb ID" }), {
      target: { value: "6515882" },
    });
    await fireEvent.click(screen.getByRole("button", { name: "Save Links" }));

    expect(onMetadataSave).toHaveBeenCalledWith({
      fields: ["urls", "externalIds"],
      patch: expect.objectContaining({
        urls: ["https://example.test"],
        externalIds: { tmdb: "6515882" },
      }),
    });
  });

  it("shows provider IDs separately from URL links in the read view", () => {
    const card = buildCard();
    card.links = [
      { label: "The Movie Database", url: "https://www.themoviedb.org/tv/271267" },
      { label: "tmdb: 418214", url: "https://www.themoviedb.org/tv/418214", provider: "tmdb" },
    ];

    const { container } = render(EntityDetail, {
      props: {
        card,
        tabs: [{ id: "links", label: "Links", sections: ["links"] }],
      },
    });

    expect(screen.getByText("Links & Provider IDs")).toBeInTheDocument();
    expect(container.querySelector(".metadata-card-capped")).toBeInTheDocument();
    expect(screen.getByText("Websites")).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Websites" })).getByText("themoviedb.org")).toBeInTheDocument();
    expect(screen.getByText("https://www.themoviedb.org/tv/271267")).toBeInTheDocument();
    expect(screen.getByText("Provider IDs")).toBeInTheDocument();
    expect(screen.getByText("tmdb")).toBeInTheDocument();
    expect(screen.getByText("418214")).toBeInTheDocument();
  });
});
