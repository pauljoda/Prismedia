import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import PluginCredentialForm from "./PluginCredentialForm.svelte";

type PluginCredentialFormProps = ComponentProps<typeof PluginCredentialForm>;

function baseProps(overrides: Partial<PluginCredentialFormProps> = {}): PluginCredentialFormProps {
  const props: PluginCredentialFormProps = {
    fields: [{ key: "api_key", label: "API Key", required: true, url: null }],
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
  it("isolates replacement secrets from the surrounding page's login autofill", () => {
    render(PluginCredentialForm, { props: baseProps() });
    const input = screen.getByLabelText(/api key/i) as HTMLInputElement;
    expect(input.form).not.toBeNull();
    expect(input.form).toHaveAttribute("autocomplete", "off");
    expect(input).toHaveAttribute("autocomplete", "new-password");
    expect(input).toHaveAttribute("name", "auth-test-api_key");
  });

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
});
