import { fireEvent, render, screen } from "@testing-library/svelte";
import { FileText, Play } from "@lucide/svelte";
import { readFileSync } from "node:fs";
import { createRawSnippet } from "svelte";
import { describe, expect, it, vi } from "vitest";
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
    const appStyles = readFileSync("src/app.css", "utf8");
    expect(action.className).toContain("entity-action-button");
    expect(action.className).toContain("entity-action-button-primary");
    expect(appStyles).toContain(".entity-action-button-primary {");
    expect(appStyles).toContain("color-mix(in srgb, var(--entity-action-accent) 92%, #fff 8%)");

    await fireEvent.click(action);

    expect(onClick).toHaveBeenCalledOnce();
  });

  it("keeps the shared hero action cluster wrappable at every width", () => {
    const source = readFileSync("src/lib/components/entities/EntityDetail.svelte", "utf8");

    expect(source).toContain(
      ".action-row {\n    display: flex;\n    flex-wrap: wrap;",
    );
    expect(source).toContain(
      ".action-group {\n    display: flex;\n    flex: 1 1 auto;\n    flex-wrap: wrap;",
    );
    expect(source).not.toContain("EntityFileManagementAction");
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
    expect(chip).toHaveTextContent("metadata-router · CaseSensitive:Show:AbC:01:5");
    expect(chip).toHaveClass("provider-identity-chip");
    expect(readFileSync("src/lib/components/entities/EntityDetail.svelte", "utf8")).toContain(
      ".provider-identity-chip {\n    gap: 0.35rem;\n    min-width: 0;\n    max-width: 100%;\n    text-decoration: none;\n    text-transform: none;",
    );
    expect(readFileSync("src/lib/components/entities/EntityDetail.svelte", "utf8")).toContain(
      '"rating rating"\n        "badges badges"',
    );
    expect(readFileSync("src/lib/components/entities/EntityDetail.svelte", "utf8")).toContain(
      ".position-badges {\n      grid-area: badges;\n      justify-self: stretch;\n      justify-content: flex-start;\n      width: 100%;",
    );
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
    expect(chip).toHaveTextContent("source-plugin · opaque:Value:Keeps:Case");

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
        capabilities: [],
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

    const { container } = render(EntityDetail, { props: { card, posterSize: "large" } });

    expect(container.querySelector(".poster-frame .entity-thumbnail")).toBeInTheDocument();
    expect(container.querySelector(".poster-frame img")).toHaveAttribute("src", "/covers/book.jpg");
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

    expect(screen.getByText("Poster empty")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Header empty" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload poster" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Upload header" })).toBeInTheDocument();
    expect(container.querySelector(".header-asset-placeholder")).toBeInTheDocument();
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

    expect(screen.getByRole("heading", { name: "Studio" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Blender Foundation" })).toHaveAttribute("href", "/studios/studio-1");
    expect(screen.getByRole("heading", { name: "Credits" })).toBeInTheDocument();
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
    expect(screen.getByText("stash-compat")).toBeInTheDocument();
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
    const creditRails = container.querySelectorAll(".credit-scroller");

    expect(thumbnails).toHaveLength(4);
    expect(creditRails.length).toBeGreaterThan(0);
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
    await fireEvent.click(screen.getByRole("button", { name: "https://example.test" }));
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
    await fireEvent.input(screen.getByRole("textbox", { name: "Title" }), {
      target: { value: "Big Buck Bunny Remastered" },
    });
    await fireEvent.input(screen.getByRole("spinbutton", { name: "Rating" }), {
      target: { value: "4.5" },
    });
    await fireEvent.click(screen.getByRole("button", { name: "Favorite" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "Stats" }), { target: { value: "94" } });
    await fireEvent.input(screen.getByPlaceholderText("count"), { target: { value: "voteCount" } });
    await fireEvent.input(screen.getByPlaceholderText("12"), { target: { value: "12" } });
    await fireEvent.click(screen.getAllByRole("button", { name: "Add entry" })[0]);
    await fireEvent.input(screen.getByRole("textbox", { name: "Positions" }), { target: { value: "3" } });
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
    await fireEvent.click(screen.getByRole("button", { name: "https://example.test" }));
    await fireEvent.input(screen.getByRole("textbox", { name: "Links item" }), {
      target: { value: "https://changed.test" },
    });
    await fireEvent.keyDown(screen.getByRole("textbox", { name: "Links item" }), { key: "Enter" });
    await fireEvent.click(screen.getByRole("tab", { name: "Details" }));

    expect(screen.getByRole("dialog", { name: "Discard unsaved edits?" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Links" })).toHaveAttribute("aria-selected", "true");

    await fireEvent.click(screen.getByRole("button", { name: "Discard changes" }));

    expect(screen.getByRole("tab", { name: "Details" })).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByRole("dialog", { name: "Discard unsaved edits?" })).not.toBeInTheDocument();
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
    await fireEvent.input(screen.getByRole("textbox", { name: "Links" }), { target: { value: "not-a-url" } });
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
    expect(screen.getByRole("button", { name: "https://example.test" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "External IDs" })).toHaveValue("6515881");

    await fireEvent.input(screen.getByRole("textbox", { name: "External IDs" }), {
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
    const metadataCardSource = readFileSync("src/lib/components/MetadataCard.svelte", "utf8");

    expect(screen.getByText("Links & Provider IDs")).toBeInTheDocument();
    expect(container.querySelector(".metadata-card-capped")).toBeInTheDocument();
    expect(metadataCardSource).toContain("max-height: var(--metadata-card-max-height, 24rem);");
    expect(metadataCardSource).toContain("overflow-y: auto;");
    expect(screen.getByText("URLs")).toBeInTheDocument();
    expect(screen.getByText("themoviedb.org")).toBeInTheDocument();
    expect(screen.getByText("https://www.themoviedb.org/tv/271267")).toBeInTheDocument();
    expect(screen.getByText("Provider IDs")).toBeInTheDocument();
    expect(screen.getByText("tmdb")).toBeInTheDocument();
    expect(screen.getByText("418214")).toBeInTheDocument();
  });
});
