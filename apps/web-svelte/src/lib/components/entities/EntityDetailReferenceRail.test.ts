import { render, screen } from "@testing-library/svelte";
import { Users } from "@lucide/svelte";
import { describe, expect, it } from "vitest";
import EntityDetailReferenceRail from "./EntityDetailReferenceRail.svelte";

describe("EntityDetailReferenceRail", () => {
  it("renders references as shared entity thumbnails", () => {
    const { container } = render(EntityDetailReferenceRail, {
      props: {
        icon: Users,
        title: "Credits",
        references: [
          {
            id: "person-1",
            kind: "person",
            title: "Ava Stone",
            thumbnail: "/people/ava.jpg",
          },
        ],
      },
    });

    expect(screen.getByRole("heading", { name: "Credits" })).toBeInTheDocument();
    expect(container.querySelector(".entity-thumbnail")).toBeInTheDocument();
    expect(screen.getByText("Ava Stone")).toBeInTheDocument();
  });
});
