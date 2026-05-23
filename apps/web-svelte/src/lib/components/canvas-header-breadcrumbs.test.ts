import { describe, expect, it } from "vitest";
import { getCanvasHeaderBreadcrumbItems } from "./canvas-header-breadcrumbs";

describe("getCanvasHeaderBreadcrumbItems", () => {
  it("keeps short breadcrumb trails inline", () => {
    expect(
      getCanvasHeaderBreadcrumbItems([
        { label: "Videos", href: "/videos", isLast: false },
        { label: "Episode 1", href: "#", isLast: true },
      ]),
    ).toEqual([
      { kind: "crumb", label: "Videos", href: "/videos", isLast: false },
      { kind: "crumb", label: "Episode 1", href: "#", isLast: true },
    ]);
  });

  it("collapses previous linked levels behind an overflow item", () => {
    expect(
      getCanvasHeaderBreadcrumbItems([
        { label: "Videos", href: "/videos", isLast: false },
        { label: "Series", href: "/series?series=s1", isLast: false },
        { label: "Season 1", href: "/series?series=s1&season=1", isLast: false },
        { label: "Episode 1", href: "#", isLast: true },
      ]),
    ).toEqual([
      {
        kind: "overflow",
        label: "More breadcrumbs",
        separatorAfter: false,
        items: [
          { label: "Videos", href: "/videos", isLast: false },
          { label: "Series", href: "/series?series=s1", isLast: false },
          { label: "Season 1", href: "/series?series=s1&season=1", isLast: false },
        ],
      },
      { kind: "crumb", label: "Episode 1", href: "#", isLast: true },
    ]);
  });

  it("keeps three desktop breadcrumbs inline before collapsing", () => {
    expect(
      getCanvasHeaderBreadcrumbItems([
        { label: "Books", href: "/books", isLast: false },
        { label: "Novel", href: "/books/b1", isLast: false },
        { label: "Volume 1", href: "#", isLast: true },
      ], 3),
    ).toEqual([
      { kind: "crumb", label: "Books", href: "/books", isLast: false },
      { kind: "crumb", label: "Novel", href: "/books/b1", isLast: false },
      { kind: "crumb", label: "Volume 1", href: "#", isLast: true },
    ]);
  });

  it("collapses desktop breadcrumbs after the inline limit", () => {
    expect(
      getCanvasHeaderBreadcrumbItems([
        { label: "Books", href: "/books", isLast: false },
        { label: "Novel", href: "/books/b1", isLast: false },
        { label: "Volume 1", href: "/books/b1/volumes/v1", isLast: false },
        { label: "Chapter 2", href: "#", isLast: true },
      ], 3),
    ).toEqual([
      {
        kind: "overflow",
        label: "More breadcrumbs",
        separatorAfter: false,
        items: [
          { label: "Books", href: "/books", isLast: false },
          { label: "Novel", href: "/books/b1", isLast: false },
          { label: "Volume 1", href: "/books/b1/volumes/v1", isLast: false },
        ],
      },
      { kind: "crumb", label: "Chapter 2", href: "#", isLast: true },
    ]);
  });

  it("does not include unlinked crumbs in the overflow menu", () => {
    expect(
      getCanvasHeaderBreadcrumbItems([
        { label: "Images", href: "/images", isLast: false },
        { label: "Gallery", href: "#", isLast: true },
      ]),
    ).toEqual([
      { kind: "crumb", label: "Images", href: "/images", isLast: false },
      { kind: "crumb", label: "Gallery", href: "#", isLast: true },
    ]);
  });

  it("can collapse a single previous link for tight mobile headers", () => {
    expect(
      getCanvasHeaderBreadcrumbItems([
        { label: "Videos", href: "/videos", isLast: false },
        { label: "A very long video title", href: "#", isLast: true },
      ], 1),
    ).toEqual([
      {
        kind: "overflow",
        label: "More breadcrumbs",
        separatorAfter: false,
        items: [
          { label: "Videos", href: "/videos", isLast: false },
        ],
      },
      { kind: "crumb", label: "A very long video title", href: "#", isLast: true },
    ]);
  });
});
