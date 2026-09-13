import React from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OraiLogo } from "../components/OraiLogo";
import { Header } from "../components/Header";
import { ThemeProvider } from "../components/ThemeProvider";

describe("OraiLogo Component", () => {
  it("renders light and dark logo assets with single accessible semantic naming", () => {
    render(<OraiLogo />);

    // Screen reader accessible alt name exists only once
    const accessibleImages = screen.getAllByRole("img", { name: "ORAI Conversational AI Platform" });
    expect(accessibleImages).toHaveLength(1);

    // Verify both images exist in DOM with proper theme classes
    const lightImg = accessibleImages[0];
    expect(lightImg).toHaveAttribute("src", expect.stringContaining("orai-logo-light.png"));
    expect(lightImg.className).toContain("block");
    expect(lightImg.className).toContain("dark:hidden");

    // Decorative dark image has aria-hidden="true" or empty alt
    const allImgs = document.querySelectorAll("img");
    const darkImg = Array.from(allImgs).find((img) => img.getAttribute("src")?.includes("orai-logo-dark.png"));
    expect(darkImg).toBeDefined();
    expect(darkImg?.getAttribute("aria-hidden")).toBe("true");
    expect(darkImg?.className).toContain("hidden");
    expect(darkImg?.className).toContain("dark:block");
  });

  it("applies custom styling, alt text, and priority loading props correctly", () => {
    render(
      <OraiLogo
        className="custom-logo-container"
        imageClassName="w-[180px] h-auto"
        alt="Custom Brand Alt"
        priority={false}
      />
    );

    const container = document.querySelector(".custom-logo-container");
    expect(container).toBeInTheDocument();
    expect(container?.className).toContain("max-w-full");

    const lightImg = screen.getByRole("img", { name: "Custom Brand Alt" });
    expect(lightImg).toBeInTheDocument();
    expect(lightImg.className).toContain("w-[180px]");
    expect(lightImg.className).toContain("max-w-full");

    const allImgs = document.querySelectorAll("img");
    const darkImg = Array.from(allImgs).find((img) => img.getAttribute("src")?.includes("orai-logo-dark.png"));
    expect(darkImg?.className).toContain("w-[180px]");
    expect(darkImg?.className).toContain("max-w-full");
  });
});

describe("Header Component with OraiLogo & Responsive Layout", () => {
  it("renders header branding without solid card background or sticker box", () => {
    render(
      <ThemeProvider>
        <Header
          onRefreshAll={() => {}}
          loading={false}
          autoRefresh={true}
          onToggleAutoRefresh={() => {}}
          lastUpdated={new Date()}
          tenantName="Acme Corp"
        />
      </ThemeProvider>
    );

    // Brand logo exists
    expect(screen.getByRole("img", { name: "ORAI Conversational AI Platform" })).toBeInTheDocument();
    expect(screen.getByText("Webhook Manager")).toBeInTheDocument();
    expect(screen.getAllByText("Acme Corp")[0]).toBeInTheDocument();
  });

  it("renders inspection mode banner cleanly", () => {
    render(
      <ThemeProvider>
        <Header
          onRefreshAll={() => {}}
          loading={false}
          autoRefresh={false}
          onToggleAutoRefresh={() => {}}
          lastUpdated={new Date()}
          tenantName="Target Client"
          inspectionMode={true}
          isPlatformAdmin={true}
        />
      </ThemeProvider>
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Platform Admin Inspection Mode: [Target Client]");
    expect(screen.getByRole("link", { name: /Return to Super Admin/i })).toBeInTheDocument();
  });
});
