import { render, screen } from "@testing-library/svelte";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { CollectionRuleCondition } from "@prismedia/contracts";
import ConditionRow from "./ConditionRow.svelte";

function buildCondition(
  overrides: Partial<CollectionRuleCondition> = {},
): CollectionRuleCondition {
  return {
    type: "condition",
    entityTypes: [],
    field: "title",
    operator: "contains",
    value: "test",
    ...overrides,
  };
}

describe("ConditionRow", () => {
  it("stores the video entity type from the Video chip", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(ConditionRow, {
      props: {
        condition: buildCondition(),
        onChange,
        onDelete: vi.fn(),
      },
    });

    await user.click(screen.getByRole("button", { name: "Video" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ entityTypes: ["video"] }),
    );
  });

  it("removes the video entity type when the Video chip is already active", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(ConditionRow, {
      props: {
        condition: buildCondition({ entityTypes: ["video"] }),
        onChange,
        onDelete: vi.fn(),
      },
    });

    await user.click(screen.getByRole("button", { name: "Video" }));

    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ entityTypes: [] }),
    );
  });
});
