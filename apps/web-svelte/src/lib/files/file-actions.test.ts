import { describe, expect, it } from "vitest";
import { fileContextActions } from "./file-actions";

describe("file actions", () => {
  it("exposes core context actions for directories and files", () => {
    expect(fileContextActions("directory").map((action) => action.id)).toEqual([
      "open",
      "new-folder",
      "rename",
      "move",
      "rescan",
      "delete",
    ]);
    expect(fileContextActions("file").map((action) => action.id)).toEqual([
      "open",
      "rename",
      "move",
      "delete",
    ]);
  });
});

