import { describe, expect, it } from "vitest";
import {
  bookVolumeFolderName,
  duplicatedBookVolumeFolderNameRepair,
} from "./index.js";

describe("book volume folder names", () => {
  it("does not append titles that repeat the volume number in another format", () => {
    expect(bookVolumeFolderName("1", "Volume 1")).toBe("Volume 01");
    expect(bookVolumeFolderName("1", "Vol. 1")).toBe("Volume 01");
    expect(bookVolumeFolderName("1", "v01")).toBe("Volume 01");
  });

  it("keeps descriptive volume titles", () => {
    expect(bookVolumeFolderName("1", "Entrance Exam")).toBe("Volume 01 - Entrance Exam");
  });

  it("plans a canonical rename for old duplicated single-digit volume folders", () => {
    expect(duplicatedBookVolumeFolderNameRepair("Volume 01 - Volume 1")).toBe("Volume 01");
    expect(duplicatedBookVolumeFolderNameRepair("Volume 09 - Vol. 9")).toBe("Volume 09");
    expect(duplicatedBookVolumeFolderNameRepair("Volume 10")).toBeNull();
  });
});
