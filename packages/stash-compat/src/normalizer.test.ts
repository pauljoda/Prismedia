import { describe, expect, it } from "vitest";
import {
  normalizeSceneResult,
  normalizePerformerResult,
  hasUsableNormalizedSceneResult,
} from "./normalizer";

describe("normalizeSceneResult", () => {
  it("trims whitespace and returns null for blank-ish fields", () => {
    const result = normalizeSceneResult({
      title: "  Hello  ",
      details: "   ",
      url: "",
    });
    expect(result.title).toBe("Hello");
    expect(result.details).toBeNull();
    expect(result.url).toBeNull();
  });

  it("falls back from url to urls[0] when url is missing", () => {
    const result = normalizeSceneResult({
      urls: ["https://example.com/scene", "https://example.com/scene2"],
    });
    expect(result.url).toBe("https://example.com/scene");
  });

  it("decodes HTML entities in text fields", () => {
    const result = normalizeSceneResult({
      title: "Tom &amp; Jerry",
      details: "&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;",
    });
    expect(result.title).toBe("Tom & Jerry");
    expect(result.details).toBe('<script>alert("x")</script>');
  });

  it("dedupes performer and tag names case-insensitively while preserving first casing", () => {
    const result = normalizeSceneResult({
      performers: [
        { name: "Alice Smith" },
        { name: "ALICE SMITH" },
        { name: "  " },
        { name: "Bob Jones" },
      ],
      tags: [
        { name: "Funny" },
        { name: "funny" },
        { name: "Action" },
      ],
    });
    expect(result.performerNames).toEqual(["Alice Smith", "Bob Jones"]);
    expect(result.tagNames).toEqual(["Funny", "Action"]);
  });

  it("normalizes common date formats to YYYY-MM-DD", () => {
    expect(normalizeSceneResult({ date: "2024-03-15" }).date).toBe("2024-03-15");
    expect(normalizeSceneResult({ date: "03/15/2024" }).date).toBe("2024-03-15");
    // Already-partial dates are preserved (not coerced to a false precision)
    expect(normalizeSceneResult({ date: "2024" }).date).toBe("2024");
    expect(normalizeSceneResult({ date: "2024-03" }).date).toBe("2024-03");
  });

  it("rejects garbage that leaked into the date field from a bad selector", () => {
    expect(normalizeSceneResult({ date: "{ json }" }).date).toBeNull();
    expect(normalizeSceneResult({ date: "https://example.com" }).date).toBeNull();
    expect(normalizeSceneResult({ date: "not a date at all" }).date).toBeNull();
  });

  it("only accepts http(s) or data-image URLs for imageUrl", () => {
    expect(
      normalizeSceneResult({ image: "https://example.com/poster.jpg" }).imageUrl,
    ).toBe("https://example.com/poster.jpg");
    expect(
      normalizeSceneResult({ image: "data:image/png;base64,AAAA" }).imageUrl,
    ).toBe("data:image/png;base64,AAAA");
    expect(normalizeSceneResult({ image: "javascript:alert(1)" }).imageUrl).toBeNull();
    expect(normalizeSceneResult({ image: "not-a-url" }).imageUrl).toBeNull();
  });

  it("extracts studio name from nested studio object", () => {
    const result = normalizeSceneResult({
      studio: { name: "  Acme  " },
    });
    expect(result.studioName).toBe("Acme");
  });
});

describe("hasUsableNormalizedSceneResult", () => {
  it("returns true when any meaningful field is populated", () => {
    expect(
      hasUsableNormalizedSceneResult({
        title: "Something",
        date: null,
        details: null,
        url: null,
        studioName: null,
        performerNames: [],
        tagNames: [],
        imageUrl: null,
      }),
    ).toBe(true);
    expect(
      hasUsableNormalizedSceneResult({
        title: null,
        date: null,
        details: null,
        url: null,
        studioName: null,
        performerNames: ["Alice"],
        tagNames: [],
        imageUrl: null,
      }),
    ).toBe(true);
  });

  it("returns false when every field is empty", () => {
    expect(
      hasUsableNormalizedSceneResult({
        title: null,
        date: null,
        details: null,
        url: null,
        studioName: null,
        performerNames: [],
        tagNames: [],
        imageUrl: null,
      }),
    ).toBe(false);
  });
});

describe("normalizePerformerResult", () => {
  it("maps snake_case Stash fields to camelCase Prismedia fields", () => {
    const result = normalizePerformerResult({
      name: "  Alice  ",
      eye_color: "  blue  ",
      hair_color: "brown",
      aliases: " A, B ",
    });
    expect(result.name).toBe("Alice");
    expect(result.eyeColor).toBe("blue");
    expect(result.hairColor).toBe("brown");
    expect(result.aliases).toBe("A, B");
  });

  it("dedupes image URLs and rejects invalid ones", () => {
    const result = normalizePerformerResult({
      name: "P",
      image: "https://example.com/1.jpg",
      images: [
        "https://example.com/1.jpg",
        "https://example.com/2.jpg",
        "javascript:bad",
        "",
      ],
    });
    expect(result.imageUrls).toEqual([
      "https://example.com/1.jpg",
      "https://example.com/2.jpg",
    ]);
    expect(result.imageUrl).toBe("https://example.com/1.jpg");
  });

  it("normalizes birthdate like scene dates", () => {
    expect(normalizePerformerResult({ name: "P", birthdate: "1990-01-02" }).birthdate).toBe(
      "1990-01-02",
    );
    expect(normalizePerformerResult({ name: "P", birthdate: "01/02/1990" }).birthdate).toBe(
      "1990-01-02",
    );
    expect(normalizePerformerResult({ name: "P", birthdate: "not a date" }).birthdate).toBeNull();
  });

  it("dedupes performer tag names case-insensitively", () => {
    const result = normalizePerformerResult({
      name: "P",
      tags: [{ name: "Cat" }, { name: "CAT" }, { name: "Dog" }],
    });
    expect(result.tagNames).toEqual(["Cat", "Dog"]);
  });
});
