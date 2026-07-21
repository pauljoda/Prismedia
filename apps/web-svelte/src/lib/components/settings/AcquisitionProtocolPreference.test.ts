import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { DOWNLOAD_PROTOCOL } from "$lib/api/generated/codes";
import AcquisitionProtocolPreference from "./AcquisitionProtocolPreference.svelte";

describe("AcquisitionProtocolPreference", () => {
  it("lets the user choose a preference when both protocols are available", async () => {
    const onchange = vi.fn();

    render(AcquisitionProtocolPreference, {
      props: {
        availableProtocols: [DOWNLOAD_PROTOCOL.usenet, DOWNLOAD_PROTOCOL.torrent],
        value: DOWNLOAD_PROTOCOL.usenet,
        onchange,
      },
    });

    await fireEvent.click(screen.getByLabelText("Preferred download type"));
    await fireEvent.click(screen.getByRole("option", { name: "Torrent" }));

    expect(onchange).toHaveBeenCalledWith(DOWNLOAD_PROTOCOL.torrent);
  });

  it("shows the only available protocol without allowing a preference", () => {
    render(AcquisitionProtocolPreference, {
      props: {
        availableProtocols: [DOWNLOAD_PROTOCOL.usenet],
        value: DOWNLOAD_PROTOCOL.usenet,
        onchange: vi.fn(),
      },
    });

    expect(screen.queryByLabelText("Preferred download type")).not.toBeInTheDocument();
    expect(screen.getByText("Usenet only")).toBeInTheDocument();
    expect(screen.getByText(/client for another protocol/)).toBeInTheDocument();
  });

  it("explains that a client is required when no protocol is available", () => {
    render(AcquisitionProtocolPreference, {
      props: {
        availableProtocols: [],
        value: DOWNLOAD_PROTOCOL.usenet,
        onchange: vi.fn(),
      },
    });

    expect(screen.getByText(/No enabled download clients/)).toBeInTheDocument();
  });
});
