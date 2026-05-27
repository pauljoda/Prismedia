import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import PluginCredentialForm from "./PluginCredentialForm.svelte";

type PluginCredentialFormProps = ComponentProps<typeof PluginCredentialForm>;

function baseProps(overrides: Partial<PluginCredentialFormProps> = {}): PluginCredentialFormProps {
  const props: PluginCredentialFormProps = {
    fields: [{ key: "api_key", label: "API Key", required: true }],
    getPlaceholder: () => "Paste your API key",
    getValueKey: (field) => field.key,
    inputIdPrefix: "auth-test",
    onCancel: vi.fn(),
    onSave: vi.fn(),
    saving: false,
    values: {},
  };

  return {
    ...props,
    ...overrides,
  };
}

describe("PluginCredentialForm", () => {
  it("keeps save disabled until a credential value is entered", async () => {
    render(PluginCredentialForm, {
      props: baseProps(),
    });

    const save = screen.getByRole("button", { name: /save credentials/i });
    expect(save).toBeDisabled();

    await fireEvent.input(screen.getByLabelText(/api key/i), {
      target: { value: "secret" },
    });

    expect(save).not.toBeDisabled();
  });

  it("emits cancel and save actions", async () => {
    const onCancel = vi.fn();
    const onSave = vi.fn();
    render(PluginCredentialForm, {
      props: baseProps({
        onCancel,
        onSave,
        values: { api_key: "secret" },
      }),
    });

    await fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    await fireEvent.click(screen.getByRole("button", { name: /save credentials/i }));

    expect(onCancel).toHaveBeenCalled();
    expect(onSave).toHaveBeenCalled();
  });
});
