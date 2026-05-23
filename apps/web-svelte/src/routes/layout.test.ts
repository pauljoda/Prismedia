import { describe, expect, it } from "vitest";
import { load } from "./+layout";

describe("root layout load", () => {
  it("hydrates the sidebar collapsed state from the browser cookie before rendering", async () => {
    document.cookie = "prismedia-sidebar=collapsed;path=/";

    const data = await load({
      fetch: async () =>
        new Response(JSON.stringify({ accepted: true }), {
          headers: { "Content-Type": "application/json" },
        }),
    } as never);

    expect((data as App.PageData).initialCollapsed).toBe(true);
  });
});
