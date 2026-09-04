import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type { CollectionRuleGroup } from "$lib/collections/models";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { COLLECTION_RULE_FIELD as FIELD, COLLECTION_RULE_OPERATOR as OP } from "$lib/api/generated/codes";
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

    await fireEvent.keyDown(screen.getByRole("button", { name: "Rule field" }), { key: "ArrowDown" });
    await fireEvent.pointerUp(screen.getByRole("option", { name: "Rating" }));

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

    expect(screen.getByText("All supported types")).toBeVisible();
    await fireEvent.click(screen.getByRole("button", { name: "Add entity types" }));
    await fireEvent.click(screen.getByRole("option", { name: "Video" }));
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
    expect(screen.getByRole("button", { name: "Add entity types" })).toBeDisabled();
  });

  it("preserves nested groups when editing, adding, and removing sibling conditions", async () => {
    const nested = initialRule();
    nested.children[0] = { ...nested.children[0], value: "nested" } as typeof nested.children[0];
    const rule = initialRule();
    rule.children.unshift(nested);
    const onChange = vi.fn();
    render(ConditionBuilder, { props: baseProps({ rule, onChange }) });

    expect(screen.getByDisplayValue("nested")).toBeVisible();
    await fireEvent.input(screen.getByDisplayValue("cats"), { target: { value: "dogs" } });
    expect(onChange.mock.lastCall?.[0].children).toEqual([nested, { ...rule.children[1], value: "dogs" }]);

    await fireEvent.click(screen.getAllByRole("button", { name: "Add condition" }).at(-1)!);
    expect(onChange.mock.lastCall?.[0].children).toHaveLength(3);
    expect(onChange.mock.lastCall?.[0].children[0]).toEqual(nested);

    await fireEvent.click(screen.getAllByRole("button", { name: "Remove condition" }).at(-1)!);
    expect(onChange.mock.lastCall?.[0].children).toEqual([nested]);
  });

  it("removes an explicit type to return to all supported types", async () => {
    const rule = initialRule();
    if (rule.children[0].type === "condition") rule.children[0].entityTypes = [ENTITY_KIND.video];
    const onChange = vi.fn();
    render(ConditionBuilder, { props: baseProps({ rule, onChange }) });
    await fireEvent.click(screen.getByRole("button", { name: "Remove Video" }));
    expect(onChange.mock.lastCall?.[0].children[0].entityTypes).toEqual([]);
  });

  it("keeps date ranges in the shared native date fields", async () => {
    const rule = initialRule();
    rule.children = [{ type: "condition", field: FIELD.date, operator: OP.between,
      value: ["2026-01-01", "2026-02-01"], entityTypes: [] }];
    const onChange = vi.fn();
    render(ConditionBuilder, { props: baseProps({ rule, onChange }) });
    const from = screen.getByLabelText("From");
    expect(from).toHaveAttribute("type", "date");
    expect(from).toHaveClass("appearance-none");
    expect(screen.getByLabelText("To")).toHaveValue("2026-02-01");
    await fireEvent.input(from, { target: { value: "2026-01-15" } });
    expect(onChange.mock.lastCall?.[0].children[0].value).toEqual(["2026-01-15", "2026-02-01"]);
  });

  it("omits unused comparison values for yes/no conditions", () => {
    const rule = initialRule();
    rule.children = [{ type: "condition", field: FIELD.organized, operator: OP.isTrue, value: null, entityTypes: [] }];
    render(ConditionBuilder, { props: baseProps({ rule }) });
    expect(screen.getByRole("button", { name: "Rule operator" })).toHaveTextContent("Yes");
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.queryByText("Value")).not.toBeInTheDocument();
  });

  it("shows only supported, unselected types in the picker", async () => {
    const rule = initialRule();
    rule.children = [{ type: "condition", field: FIELD.duration, operator: OP.greaterThan,
      value: 60, entityTypes: [ENTITY_KIND.video] }];
    render(ConditionBuilder, { props: baseProps({ rule }) });
    await fireEvent.click(screen.getByRole("button", { name: "Add entity types" }));
    expect(screen.getByRole("option", { name: "Audio Track" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Book" })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Video" })).not.toBeInTheDocument();
  });
});
