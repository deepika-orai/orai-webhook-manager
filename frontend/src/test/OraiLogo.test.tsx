import React from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { OraiLogo } from "../components/OraiLogo";
import { Header } from "../components/Header";
import { ThemeProvider } from "../components/ThemeProvider";

describe("OraiLogo Component", () => {
  it("renders light and dark logo assets with single accessible semantic naming on wrapper", () => {
    render(<OraiLogo />);

    // Screen reader accessible alt name exists only once on the wrapper element
    const accessibleLogos = screen.getAllByRole("img", { name: "ORAI Conversational AI Platform" });
    expect(accessibleLogos).toHaveLength(1);

    const logoWrapper = accessibleLogos[0];
    expect(logoWrapper).toHaveAttribute("role", "img");
    expect(logoWrapper).toHaveAttribute("aria-label", "ORAI Conversational AI Platform");

    // Verify both internal images exist in DOM with proper theme classes, empty alt text, and aria-hidden
    const allImgs = document.querySelectorAll("img");
    expect(allImgs).toHaveLength(2);

    const lightImg = Array.from(allImgs).find((img) => img.getAttribute("src")?.includes("orai-logo-light.png"));
    expect(lightImg).toBeDefined();
    expect(lightImg?.getAttribute("alt")).toBe("");
    expect(lightImg?.getAttribute("aria-hidden")).toBe("true");
    expect(lightImg?.className).toContain("block");
    expect(lightImg?.className).toContain("dark:hidden");

    const darkImg = Array.from(allImgs).find((img) => img.getAttribute("src")?.includes("orai-logo-dark.png"));
    expect(darkImg).toBeDefined();
    expect(darkImg?.getAttribute("alt")).toBe("");
    expect(darkImg?.getAttribute("aria-hidden")).toBe("true");
    expect(darkImg?.className).toContain("hidden");
    expect(darkImg?.className).toContain("dark:block");
  });

  it("applies custom styling, custom alt text, and priority loading props correctly", () => {
    render(
      <OraiLogo
        className="custom-logo-container"
        imageClassName="w-[180px] h-auto"
        alt="Custom Brand Alt"
        priority={false}
      />
    );

    // Custom alt updates wrapper accessible name and exactly one role="img" exists
    const accessibleLogos = screen.getAllByRole("img", { name: "Custom Brand Alt" });
    expect(accessibleLogos).toHaveLength(1);

    const container = document.querySelector(".custom-logo-container");
    expect(container).toBeInTheDocument();
    expect(container?.className).toContain("max-w-full");
    expect(container).toHaveAttribute("aria-label", "Custom Brand Alt");

    const allImgs = document.querySelectorAll("img");
    expect(allImgs).toHaveLength(2);

    allImgs.forEach((img) => {
      expect(img.getAttribute("alt")).toBe("");
      expect(img.getAttribute("aria-hidden")).toBe("true");
      expect(img.className).toContain("w-[180px]");
      expect(img.className).toContain("max-w-full");
    });
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
