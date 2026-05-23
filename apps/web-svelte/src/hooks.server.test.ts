import { describe, expect, it } from "vitest";
import { handle } from "./hooks.server";

function createEvent(url: string, init: RequestInit = {}) {
  return {
    request: new Request(url, init),
    url: new URL(url),
  };
}

describe("server cache headers", () => {
  it("adds a short private cache policy to page documents", async () => {
    const response = await handle({
      event: createEvent("http://localhost/videos"),
      resolve: async () =>
        new Response("<!doctype html>", {
          headers: { "Content-Type": "text/html; charset=utf-8" },
        }),
    } as never);

    expect(response.headers.get("cache-control")).toBe(
      "private, max-age=30, stale-while-revalidate=300",
    );
    expect(response.headers.get("vary")).toBe("Cookie");
  });

  it("adds the same page policy to SvelteKit data requests", async () => {
    const response = await handle({
      event: createEvent("http://localhost/videos/__data.json"),
      resolve: async () =>
        new Response(JSON.stringify({ nodes: [] }), {
          headers: { "Content-Type": "application/json" },
        }),
    } as never);

    expect(response.headers.get("cache-control")).toBe(
      "private, max-age=30, stale-while-revalidate=300",
    );
    expect(response.headers.get("vary")).toBe("Cookie");
  });

  it("keeps explicit API cache policies intact", async () => {
    const response = await handle({
      event: createEvent("http://localhost/api/assets/images/image-1/thumb"),
      resolve: async () =>
        new Response("bytes", {
          headers: { "Cache-Control": "private, max-age=300" },
        }),
    } as never);

    expect(response.headers.get("cache-control")).toBe("private, max-age=300");
  });
});
