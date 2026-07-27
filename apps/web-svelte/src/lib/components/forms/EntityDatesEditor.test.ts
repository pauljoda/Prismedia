import { cleanup, fireEvent, render } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ENTITY_DATE_TYPE, ENTITY_KIND } from "$lib/entities/entity-codes";
import EntityDatesEditor from "./EntityDatesEditor.svelte";

describe("EntityDatesEditor", () => {
  afterEach(cleanup);

  it("shows movie release milestones and writes their canonical date codes", async () => {
    const onChange = vi.fn();
    const view = render(EntityDatesEditor, {
      entityKind: ENTITY_KIND.movie,
      values: [],
      onChange,
    });

    expect(view.getByLabelText("Theatrical release")).toBeInTheDocument();
    expect(view.getByLabelText("Streaming release")).toBeInTheDocument();
    expect(view.getByLabelText("Digital release")).toBeInTheDocument();
    expect(view.getByLabelText("Physical release")).toBeInTheDocument();

    await fireEvent.input(view.getByLabelText("Streaming release"), {
      target: { value: "2026-11-01" },
    });

    expect(onChange).toHaveBeenLastCalledWith([
      { key: ENTITY_DATE_TYPE.streamingRelease, value: "2026-11-01" },
    ]);
  });

  it("keeps an imprecise provider date visible until the user supplies an exact day", () => {
    const view = render(EntityDatesEditor, {
      entityKind: ENTITY_KIND.movie,
      values: [{ key: ENTITY_DATE_TYPE.physicalRelease, value: "2026-12" }],
      onChange: vi.fn(),
    });

    expect(view.getByText(/Current provider value: 2026-12/)).toBeInTheDocument();
    expect(view.getByLabelText("Physical release")).toHaveValue("");
  });
});
