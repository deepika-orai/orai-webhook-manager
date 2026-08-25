import React from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { EndpointsList } from "../components/EndpointsList";
import { WebhookEndpoint } from "../types/dashboard";

describe("EndpointsList Component", () => {
  it("renders 'Last received: Never' when lastReceivedAt is null or undefined", () => {
    const endpoints: WebhookEndpoint[] = [
      {
        id: "ep-1",
        name: "WhatsApp Primary Ingestion",
        keyPrefix: "wh_live_abc123",
        status: "ACTIVE",
        createdAt: "2026-08-25T10:00:00Z",
        lastReceivedAt: null,
      },
    ];

    render(<EndpointsList endpoints={endpoints} />);

    expect(screen.getByText("WhatsApp Primary Ingestion")).toBeInTheDocument();
    expect(screen.getByText("Last received:")).toBeInTheDocument();
    expect(screen.getByText("Never")).toBeInTheDocument();

    // Ensure no malformed "Never received" or duplicate "received:received" text exists
    expect(screen.queryByText(/Never received/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/received:received/i)).not.toBeInTheDocument();
  });

  it("renders 'Last received: <formatted timestamp>' when lastReceivedAt is provided", () => {
    const endpoints: WebhookEndpoint[] = [
      {
        id: "ep-2",
        name: "Telegram Secondary Ingestion",
        keyPrefix: "wh_live_xyz789",
        status: "ACTIVE",
        createdAt: "2026-08-25T10:00:00Z",
        lastReceivedAt: "2026-08-25T12:30:00Z",
      },
    ];

    render(<EndpointsList endpoints={endpoints} />);

    expect(screen.getByText("Telegram Secondary Ingestion")).toBeInTheDocument();
    expect(screen.getByText("Last received:")).toBeInTheDocument();

    // Should render a formatted date string
    const formattedDate = new Date("2026-08-25T12:30:00Z").toLocaleString(undefined, {
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
    expect(screen.getByText(formattedDate)).toBeInTheDocument();
    expect(screen.queryByText(/Never/i)).not.toBeInTheDocument();
  });
});
