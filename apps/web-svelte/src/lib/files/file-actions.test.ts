import { describe, expect, it } from "vitest";
import { fileContextActions } from "./file-actions";

describe("file actions", () => {
  it("exposes core context actions for directories and files", () => {
    expect(fileContextActions("directory").map((action) => action.id)).toEqual([
      "open",
      "download",
      "new-folder",
      "upload",
      "rename",
      "move",
      "rescan",
      "exclude",
      "delete",
    ]);
    expect(fileContextActions("file").map((action) => action.id)).toEqual([
      "open",
      "download",
      "rename",
      "move",
      "exclude",
      "delete",
    ]);
  });

  it("allows watched roots to be downloaded as archives", () => {
    expect(fileContextActions("directory", true).map((action) => action.id)).toEqual([
      "open",
      "download",
      "new-folder",
      "upload",
      "rescan",
    ]);
  });

  it("keeps external libraries readable without exposing mutations", () => {
    expect(fileContextActions("directory", false, false, true).map(action => action.id)).toEqual(["open", "download", "rescan"]);
    expect(fileContextActions("file", false, false, true).some(action => action.destructive)).toBe(false);
  });

  it("offers removal instead of exclusion for excluded entries", () => {
    expect(fileContextActions("directory", false, true).map((action) => action.id)).toContain("remove-exclusion");
    expect(fileContextActions("directory", false, true).map((action) => action.id)).not.toContain("exclude");
    expect(fileContextActions("file", false, true).map((action) => action.id)).toContain("remove-exclusion");
  });
});
