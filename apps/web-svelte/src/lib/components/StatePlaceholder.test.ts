import { render, screen } from "@testing-library/svelte";
import { CircleAlert } from "@lucide/svelte";
import { describe, expect, it } from "vitest";
import StatePlaceholder from "./StatePlaceholder.svelte";

describe("StatePlaceholder", () => {
  it("composes empty states from the shared control base", () => {
    const { container } = render(StatePlaceholder, {
      icon: CircleAlert,
      title: "Nothing here yet",
      description: "New items will appear here.",
    });

    expect(screen.getByRole("status")).toHaveTextContent("Nothing here yet");
    expect(screen.getByText("New items will appear here.")).toBeInTheDocument();
    expect(container.querySelector('[data-slot="empty"]')).toBeInTheDocument();
    expect(container.querySelector('[data-slot="empty-icon"]')).toBeInTheDocument();
  });
});
