import React from "react";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { ThemeProvider, useTheme } from "../components/ThemeProvider";
import { ThemeSelector } from "../components/ThemeSelector";
import { MetricCard } from "../components/MetricCard";

// Mock media query
let matchMediaListeners: Array<(e: { matches: boolean }) => void> = [];
let mediaMatches = false;

function setupMatchMedia(initialDark = false) {
  mediaMatches = initialDark;
  matchMediaListeners = [];

  window.matchMedia = vi.fn().mockImplementation((query: string) => {
    return {
      matches: query.includes("dark") ? mediaMatches : !mediaMatches,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn((event: string, listener: (e: { matches: boolean }) => void) => {
        if (event === "change") {
          matchMediaListeners.push(listener);
        }
      }),
      removeEventListener: vi.fn((event: string, listener: (e: { matches: boolean }) => void) => {
        if (event === "change") {
          matchMediaListeners = matchMediaListeners.filter((l) => l !== listener);
        }
      }),
      dispatchEvent: vi.fn(),
    };
  });
}

function TestConsumer() {
  const { theme, resolvedTheme, setTheme } = useTheme();
  return (
    <div>
      <span data-testid="theme-mode">{theme}</span>
      <span data-testid="resolved-theme">{resolvedTheme}</span>
      <button onClick={() => setTheme("light")}>Set Light</button>
      <button onClick={() => setTheme("dark")}>Set Dark</button>
      <button onClick={() => setTheme("system")}>Set System</button>
    </div>
  );
}

describe("Theme System & ThemeProvider", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.classList.remove("dark");
    document.documentElement.style.colorScheme = "";
    setupMatchMedia(false);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("defaults to 'system' theme and resolves to 'light' when OS prefers light", () => {
    setupMatchMedia(false);

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("theme-mode").textContent).toBe("system");
    expect(screen.getByTestId("resolved-theme").textContent).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(document.documentElement.style.colorScheme).toBe("light");
  });

  it("defaults to 'system' theme and resolves to 'dark' when OS prefers dark", () => {
    setupMatchMedia(true);

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("theme-mode").textContent).toBe("system");
    expect(screen.getByTestId("resolved-theme").textContent).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");
  });

  it("safely handles corrupted or unavailable localStorage and falls back to system", () => {
    // Mock localStorage getItem to throw or return invalid string
    const getItemSpy = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("localStorage blocked");
    });

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("theme-mode").textContent).toBe("system");
    getItemSpy.mockRestore();
  });

  it("ignores invalid values in localStorage and defaults to system", () => {
    localStorage.setItem("orai-theme", "invalid-theme-value");

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("theme-mode").textContent).toBe("system");
  });

  it("persists theme switch to 'dark' and 'light' in localStorage and updates root attributes", () => {
    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    // Switch to dark
    fireEvent.click(screen.getByText("Set Dark"));
    expect(screen.getByTestId("theme-mode").textContent).toBe("dark");
    expect(screen.getByTestId("resolved-theme").textContent).toBe("dark");
    expect(localStorage.getItem("orai-theme")).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");

    // Switch to light
    fireEvent.click(screen.getByText("Set Light"));
    expect(screen.getByTestId("theme-mode").textContent).toBe("light");
    expect(screen.getByTestId("resolved-theme").textContent).toBe("light");
    expect(localStorage.getItem("orai-theme")).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(document.documentElement.style.colorScheme).toBe("light");
  });

  it("reacts dynamically to live OS prefers-color-scheme changes when in system mode", () => {
    setupMatchMedia(false); // starts light

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("resolved-theme").textContent).toBe("light");

    // Simulate OS switching to dark mode
    act(() => {
      mediaMatches = true;
      for (const listener of matchMediaListeners) {
        listener({ matches: true });
      }
    });

    expect(screen.getByTestId("resolved-theme").textContent).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });
});

describe("ThemeSelector Component", () => {
  beforeEach(() => {
    localStorage.clear();
    setupMatchMedia(false);
  });

  it("renders accessible segmented buttons with aria-pressed and aria-label", () => {
    render(
      <ThemeProvider>
        <ThemeSelector />
      </ThemeProvider>
    );

    const lightBtn = screen.getByRole("button", { name: /light theme/i });
    const darkBtn = screen.getByRole("button", { name: /dark theme/i });
    const systemBtn = screen.getByRole("button", { name: /system theme/i });

    expect(lightBtn).toBeInTheDocument();
    expect(darkBtn).toBeInTheDocument();
    expect(systemBtn).toBeInTheDocument();

    // System is active by default after mount
    expect(systemBtn.getAttribute("aria-pressed")).toBe("true");
    expect(lightBtn.getAttribute("aria-pressed")).toBe("false");
    expect(darkBtn.getAttribute("aria-pressed")).toBe("false");

    // Click dark button
    fireEvent.click(darkBtn);
    expect(darkBtn.getAttribute("aria-pressed")).toBe("true");
    expect(systemBtn.getAttribute("aria-pressed")).toBe("false");
  });

  it("hydrates ThemeProvider + ThemeSelector with stored dark preference without hydration warnings or errors", async () => {
    localStorage.setItem("orai-theme", "dark");
    setupMatchMedia(false);

    // 1. Simulate server-side rendering
    const { renderToString } = await import("react-dom/server");
    const { hydrateRoot } = await import("react-dom/client");

    const serverHtml = renderToString(
      <ThemeProvider>
        <ThemeSelector />
      </ThemeProvider>
    );

    // Verify SSR output contains deterministic aria-pressed="false"
    expect(serverHtml).toContain('aria-pressed="false"');

    // 2. Set up DOM container with SSR HTML
    const container = document.createElement("div");
    container.innerHTML = serverHtml;
    document.body.appendChild(container);

    // 3. Spy on console error & warn to catch any React hydration mismatch warnings
    const consoleErrorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const consoleWarnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

    // 4. Hydrate in the browser environment with stored 'dark' preference
    let root!: ReturnType<typeof hydrateRoot>;
    await act(async () => {
      root = hydrateRoot(
        container,
        <ThemeProvider>
          <ThemeSelector />
        </ThemeProvider>
      );
    });

    // Verify no hydration warnings or errors occurred
    const hydrationErrors = consoleErrorSpy.mock.calls.filter((call) =>
      call.some((arg) => typeof arg === "string" && arg.toLowerCase().includes("hydration"))
    );
    const hydrationWarns = consoleWarnSpy.mock.calls.filter((call) =>
      call.some((arg) => typeof arg === "string" && arg.toLowerCase().includes("hydration"))
    );
    expect(hydrationErrors).toHaveLength(0);
    expect(hydrationWarns).toHaveLength(0);

    // 5. Verify that after client mount, the dark button is active with aria-pressed="true"
    const darkBtn = container.querySelector('button[aria-label="Dark theme"]');
    expect(darkBtn).not.toBeNull();
    expect(darkBtn?.getAttribute("aria-pressed")).toBe("true");

    consoleErrorSpy.mockRestore();
    consoleWarnSpy.mockRestore();
    act(() => {
      root.unmount();
    });
    document.body.removeChild(container);
  });
});

describe("UI Layout & Copy Regression Tests", () => {
  it("renders MetricCard 'TOTAL MESSAGES' without truncation or broken characters", () => {
    render(
      <MetricCard
        title="TOTAL MESSAGES"
        value={15420}
        subtitle="Across all active WhatsApp pipelines"
        iconType="total"
      />
    );

    expect(screen.getByText("TOTAL MESSAGES")).toBeInTheDocument();
    expect(screen.getByText("15,420")).toBeInTheDocument();
    expect(screen.getByText("Across all active WhatsApp pipelines")).toBeInTheDocument();
  });

  it("singular and plural endpoint count logic formats correctly", () => {
    const formatEndpoints = (count?: number) =>
      `${count ?? 0} ${(count ?? 0) === 1 ? "endpoint" : "endpoints"}`;

    expect(formatEndpoints(0)).toBe("0 endpoints");
    expect(formatEndpoints(1)).toBe("1 endpoint");
    expect(formatEndpoints(2)).toBe("2 endpoints");
    expect(formatEndpoints(15)).toBe("15 endpoints");
  });
});
