import { describe, expect, it } from "vitest";
import {
  ENTITY_FILE_ROLE,
  ENTITY_KIND,
  ENTITY_KIND_DEFINITIONS,
  ENTITY_KINDS,
  isTopLevelEntityKind,
  labelForEntityKind,
  resolveEntityBrowsePath,
  resolveEntityHref,
} from "./entity-codes";

describe("code registries", () => {
  it("covers every known entity kind with a label", () => {
    for (const kind of ENTITY_KINDS) {
      expect(labelForEntityKind(kind)).not.toBe(kind);
    }
  });

  it("resolves browse paths for top-level entity kinds", () => {
    for (const kind of ENTITY_KINDS.filter(isTopLevelEntityKind)) {
      expect(resolveEntityBrowsePath(kind)).toMatch(/^\//);
    }
  });

  it("uses people as the canonical person route", () => {
    expect(resolveEntityBrowsePath("person")).toBe("/people");
    expect(resolveEntityHref("person", "person-1")).toBe("/people/person-1");
  });

  it("requires parent context for structural child routes", () => {
    expect(resolveEntityHref("book-chapter", "chapter")).toBeUndefined();
    expect(resolveEntityHref("book-chapter", "chapter", { kind: "book", id: "book" })).toBe(
      "/books/book/chapters/chapter",
    );
  });

  it("keeps every collection member kind navigable through its definition", () => {
    for (const kind of ENTITY_KIND_DEFINITIONS[ENTITY_KIND.collection].containableKinds) {
      expect(resolveEntityHref(kind, "entity-1")).toMatch(/^\//);
    }
  });

  it("includes server image roles used by detail artwork", () => {
    expect(ENTITY_FILE_ROLE.backdrop).toBe("backdrop");
    expect(ENTITY_FILE_ROLE.logo).toBe("logo");
    expect(ENTITY_FILE_ROLE.poster).toBe("poster");
  });
});
