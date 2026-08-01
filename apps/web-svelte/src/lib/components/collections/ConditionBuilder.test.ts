import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type { CollectionRuleGroup } from "$lib/collections/models";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import ConditionBuilder from "./ConditionBuilder.svelte";

type ConditionBuilderProps = ComponentProps<typeof ConditionBuilder>;

function initialRule(): CollectionRuleGroup {
  return {
    type: "group",
    operator: "and",
    children: [{ type: "condition", entityTypes: [], field: "title", operator: "contains", value: "cats" }],
  };
}

function baseProps(overrides: Partial<ConditionBuilderProps> = {}): ConditionBuilderProps {
  return {
    rule: initialRule(),
    onChange: vi.fn(),
    ...overrides,
  };
}

describe("ConditionBuilder", () => {
  it("updates a condition from the shared field selector", async () => {
    const onChange = vi.fn();
    render(ConditionBuilder, { props: baseProps({ onChange }) });

    await fireEvent.click(screen.getByRole("button", { name: "Rule field" }));
    await fireEvent.click(screen.getByRole("option", { name: "Rating" }));

    expect(onChange).toHaveBeenLastCalledWith({
      type: "group",
      operator: "and",
      children: [{ type: "condition", entityTypes: [], field: "rating", operator: "equals", value: 0 }],
    });
  });

  it("keeps rule logic and entity type controls accessible and interactive", async () => {
    const onChange = vi.fn();
    render(ConditionBuilder, { props: baseProps({ onChange }) });

    const all = screen.getByRole("radio", { name: "All" });
    expect(all).toHaveAttribute("aria-checked", "true");

    await fireEvent.click(screen.getByRole("radio", { name: "Any" }));
    expect(onChange).toHaveBeenLastCalledWith({
      type: "group",
      operator: "or",
      children: [{ type: "condition", entityTypes: [], field: "title", operator: "contains", value: "cats" }],
    });

    const video = screen.getByRole("button", { name: "Video" });
    expect(video).toHaveAttribute("aria-pressed", "true");
    await fireEvent.click(video);
    expect(onChange).toHaveBeenLastCalledWith({
      type: "group",
      operator: "and",
      children: [{ type: "condition", entityTypes: [ENTITY_KIND.video], field: "title", operator: "contains", value: "cats" }],
    });
  });

  it("updates text values and adds or removes conditions", async () => {
    const onChange = vi.fn();
    render(ConditionBuilder, { props: baseProps({ onChange }) });

    await fireEvent.input(screen.getByLabelText("Text value"), { target: { value: "dogs" } });
    expect(onChange).toHaveBeenLastCalledWith({
      type: "group",
      operator: "and",
      children: [{ type: "condition", entityTypes: [], field: "title", operator: "contains", value: "dogs" }],
    });

    await fireEvent.click(screen.getByRole("button", { name: "Add condition" }));
    expect(onChange).toHaveBeenLastCalledWith({
      type: "group",
      operator: "and",
      children: [
        { type: "condition", entityTypes: [], field: "title", operator: "contains", value: "cats" },
        { type: "condition", entityTypes: [], field: "title", operator: "contains", value: "" },
      ],
    });

    await fireEvent.click(screen.getByRole("button", { name: "Remove condition" }));
    expect(onChange).toHaveBeenLastCalledWith({ type: "group", operator: "and", children: [] });
  });

  it("keeps migrated controls disabled", () => {
    render(ConditionBuilder, { props: baseProps({ disabled: true }) });

    expect(screen.getByRole("button", { name: "Rule field" })).toBeDisabled();
    expect(screen.getByLabelText("Text value")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Remove condition" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Add condition" })).toBeDisabled();
  });
});
