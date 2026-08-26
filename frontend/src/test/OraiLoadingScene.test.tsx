import React from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OraiLoadingScene } from "../components/OraiLoadingScene";

describe("OraiLoadingScene Component", () => {
  it("renders pipeline stages and default copy in dark mode", () => {
    render(<OraiLoadingScene />);

    expect(screen.getByText("ORAI Webhook Manager")).toBeInTheDocument();
    expect(screen.getByText(/Initializing telemetry & event ingestion pipeline/i)).toBeInTheDocument();
    expect(screen.getByText("Received")).toBeInTheDocument();
    expect(screen.getByText("Queued")).toBeInTheDocument();
    expect(screen.getByText("Processed")).toBeInTheDocument();
    expect(screen.getByText("Dashboard")).toBeInTheDocument();
  });

  it("renders custom copy and supports light theme", () => {
    render(
      <OraiLoadingScene
        title="Authenticating Platform Admin"
        subtitle="Verifying cryptographic tokens..."
        theme="light"
      />
    );

    expect(screen.getByText("Authenticating Platform Admin")).toBeInTheDocument();
    expect(screen.getByText("Verifying cryptographic tokens...")).toBeInTheDocument();
  });
});
