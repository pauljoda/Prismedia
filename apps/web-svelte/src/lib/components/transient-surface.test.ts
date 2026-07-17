import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

const read = (path: string) => readFileSync(path, "utf8");

describe("transient surface design contract", () => {
  it("defines opaque neutral floating and modal surfaces", () => {
    const styles = read("src/app.css");
    const transientSurfaceRule = styles.match(
      /\.surface-elevated,\s*\.floating-surface,\s*\.app-dialog-surface\s*\{(?<body>[^}]+)\}/,
    )?.groups?.body;

    expect(styles).toContain(".floating-surface");
    expect(styles).toContain(".app-overlay-backdrop");
    expect(styles).toContain(".app-dialog-surface");
    expect(transientSurfaceRule).toContain("background: var(--color-surface-2);");
    expect(transientSurfaceRule).not.toContain("backdrop-filter");
    expect(styles).not.toContain("background: rgba(16, 20, 32, 0.82);");
    expect(styles).not.toContain("background: rgba(21, 26, 40, 0.92);");
  });

  it("keeps the player settings flyout opaque", () => {
    const styles = read("src/app.css");
    const playerDropdownRule = styles.match(/\.player-dropdown\s*\{(?<body>[^}]+)\}/)?.groups?.body;

    expect(playerDropdownRule).toContain("background: var(--color-surface-2);");
    expect(playerDropdownRule).not.toContain("--color-overlay-glass");
  });

  it("uses the shared modal contract for app dialogs", () => {
    const dialogPrimitive = read("../../packages/ui-svelte/src/primitives/Dialog.svelte");
    const commandPalette = read("src/lib/components/CommandPalette.svelte");
    const confirmDialog = read("src/lib/components/entities/ConfirmDialog.svelte");
    const nameDialog = read("src/lib/components/entities/NameInputDialog.svelte");
    const moveDialog = read("src/lib/components/nav/MoveToSectionDialog.svelte");
    const renameDialog = read("src/lib/components/nav/RenameSectionDialog.svelte");

    expect(commandPalette).toContain("app-overlay-backdrop");
    expect(commandPalette).toContain("app-dialog-surface");
    expect(commandPalette).not.toContain("bg-black/60");
    expect(commandPalette).not.toContain("shadow-2xl");

    expect(dialogPrimitive).toContain("<dialog");
    expect(dialogPrimitive).toContain("showModal()");
    expect(dialogPrimitive).toContain("oncancel");
    expect(dialogPrimitive).toContain("app-dialog-surface");

    for (const source of [confirmDialog, nameDialog, moveDialog, renameDialog]) {
      expect(source).toContain("<Dialog");
      expect(source).not.toContain("<dialog");
      expect(source).not.toContain("::backdrop");
    }
  });

  it("uses the shared floating contract for common menus", () => {
    const sources = [
      read("src/lib/components/CanvasHeader.svelte"),
      read("src/lib/components/auth/UserChip.svelte"),
      read("src/lib/components/entities/AddToCollectionMenu.svelte"),
      read("src/lib/components/entities/BulkSelectionBar.svelte"),
      read("src/lib/components/entities/EntityGridPresetDropdown.svelte"),
      read("src/lib/components/forms/EntityPicker.svelte"),
      read("src/lib/components/identify/IdentifyProviderSelect.svelte"),
      read("src/lib/components/PlaybackQueueFlyout.svelte"),
      read("src/lib/components/TrackListRow.svelte"),
    ];

    for (const source of sources) {
      expect(source).toContain("floating-surface");
    }
  });

  it("composes the global search page from the shared search input", () => {
    const searchInput = read("../../packages/ui-svelte/src/primitives/SearchInput.svelte");
    const searchPage = read("src/routes/search/+page.svelte");

    expect(searchInput).toContain('type="search"');
    expect(searchInput).toContain('aria-label={clearLabel}');
    expect(searchInput).toContain("bind:value");
    expect(searchPage).toContain("<SearchInput");
  });

  it("keeps the changelog neutral and material", () => {
    const source = read("src/lib/components/ChangelogDialog.svelte");

    expect(source).toContain("app-dialog-surface");
    expect(source).not.toContain("Release console");
    expect(source).not.toContain("rgb(17 22 29 / 0.97)");
    expect(source).not.toContain("rgb(42 48 56 / 0.36)");
    expect(source).not.toContain("rgb(122 94 32 / 0.24)");
  });

  it("does not fall back to browser-native prompts for app actions", () => {
    const sources = [
      read("src/routes/files/+page.svelte"),
      read("src/routes/collections/[id]/+page.svelte"),
      read("src/routes/settings/users/+page.svelte"),
      read("src/lib/components/settings/DatabaseBackupsSection.svelte"),
      read("src/lib/components/settings/TranscodeCacheSection.svelte"),
      read("src/lib/components/forms/MarkdownEditor.svelte"),
    ].join("\n");

    expect(sources).not.toMatch(/\b(?:window\.)?(?:prompt|confirm|alert)\s*\(/);
  });
});
