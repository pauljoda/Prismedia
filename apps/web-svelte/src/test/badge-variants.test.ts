import { cleanup, render } from "@testing-library/svelte";
import { afterEach, describe, expect, it } from "vitest";
import { Badge } from "@prismedia/ui-svelte";

afterEach(cleanup);

describe("shared badge contrast", () => {
  it.each(["outline", "secondary", "success", "warning", "error", "info"] as const)(
    "does not retain the default white fill for %s badges", (variant) => {
      const { container } = render(Badge, { variant });
      const badge = container.querySelector('[data-slot="badge"]');
      expect(badge).not.toHaveClass("bg-primary");
      expect(badge).toHaveClass("min-h-badge", "text-caption");
    },
  );
});
