import React from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import * as api from "../lib/api";
import LoginPage from "../app/login/page";

const mockReplace = vi.fn();
const mockPush = vi.fn();
let mockSearchParams = new URLSearchParams();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    replace: mockReplace,
    push: mockPush,
  }),
  useSearchParams: () => mockSearchParams,
}));

describe("Centralized 401 Session Handling & Redirect Guard", () => {
  const originalLocation = window.location;

  beforeEach(() => {
    vi.clearAllMocks();
    api._resetSessionExpiredState();
    api.clearCsrfToken();
    mockSearchParams = new URLSearchParams();

    // Mock window.location cleanly
    Object.defineProperty(window, "location", {
      value: {
        ...originalLocation,
        pathname: "/dashboard",
        href: "http://localhost:3000/dashboard",
        search: "",
        replace: vi.fn(),
        assign: vi.fn(),
      },
      writable: true,
      configurable: true,
    });
  });

  afterEach(() => {
    Object.defineProperty(window, "location", {
      value: originalLocation,
      writable: true,
      configurable: true,
    });
    vi.restoreAllMocks();
  });

  it("genuine session expiration: previously authenticated session triggers refresh attempt, and on refresh failure triggers redirect to /login?reason=session_expired", async () => {
    // 1. Mark session as previously authenticated
    api.setAuthMarker();

    const fetchMock = vi.fn();

    // 2. Initial protected request returns 401
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Token expired" }),
    });

    // 3. Refresh attempt returns 401 (refresh failed)
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Refresh expired" }),
    });

    global.fetch = fetchMock;

    await expect(api.getDashboardSummary()).rejects.toThrow();

    // Verify refresh was attempted first
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/dashboard/summary"),
      expect.anything()
    );
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/auth/refresh"),
      expect.anything()
    );

    // Verify redirected once to /login?reason=session_expired
    expect(window.location.replace).toHaveBeenCalledTimes(1);
    expect(window.location.replace).toHaveBeenCalledWith("/login?reason=session_expired");
    // Auth marker should be cleared
    expect(api.hasAuthMarker()).toBe(false);
  });

  it("logged-out direct access: 401 on protected endpoint without prior auth marker does NOT trigger session_expired redirect", async () => {
    // Session is not authenticated (clean/logged out state)
    api.clearAuthMarker();

    const fetchMock = vi.fn();

    // Initial protected request returns 401
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Unauthorized" }),
    });

    // Refresh attempt also returns 401
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "No refresh token" }),
    });

    global.fetch = fetchMock;

    await expect(api.getDashboardSummary()).rejects.toThrow();

    // Must NOT redirect to /login?reason=session_expired
    expect(window.location.replace).not.toHaveBeenCalled();
  });

  it("explicit logout: clears auth marker and subsequent 401s do NOT trigger session_expired redirect", async () => {
    api.setAuthMarker();
    expect(api.hasAuthMarker()).toBe(true);

    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url.includes("/auth/logout")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ message: "Logged out" }),
        };
      }
      if (url.includes("/auth/csrf")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ token: "csrf-token" }),
        };
      }
      return {
        ok: false,
        status: 401,
        statusText: "Unauthorized",
        json: async () => ({ error: "Unauthorized" }),
      };
    });

    global.fetch = fetchMock;

    await api.logoutApi();

    // Auth marker is removed
    expect(api.hasAuthMarker()).toBe(false);

    // Subsequent API call returning 401 should not redirect to session_expired
    await expect(api.getDashboardSummary()).rejects.toThrow();
    expect(window.location.replace).not.toHaveBeenCalled();
  });

  it("protected API 401 does NOT redirect if refresh succeeds and retry succeeds", async () => {
    api.setAuthMarker();
    let dashboardAttempts = 0;
    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url.includes("/auth/refresh")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ succeeded: true }),
        };
      }
      if (url.includes("/auth/csrf")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ token: "new-csrf-token" }),
        };
      }
      if (url.includes("/dashboard/summary")) {
        dashboardAttempts++;
        if (dashboardAttempts === 1) {
          return {
            ok: false,
            status: 401,
            statusText: "Unauthorized",
            json: async () => ({ error: "Token expired" }),
          };
        }
        return {
          ok: true,
          status: 200,
          json: async () => ({
            totalMessages: 10,
            sent: 10,
            delivered: 10,
            read: 10,
            failed: 0,
            deliveredRate: 100,
            readRate: 100,
            failedRate: 0,
            pendingInboxCount: 0,
            deadLetterCount: 0,
          }),
        };
      }
      return {
        ok: true,
        status: 200,
        json: async () => ({}),
      };
    });

    global.fetch = fetchMock;

    const summary = await api.getDashboardSummary();
    expect(summary.totalMessages).toBe(10);
    expect(dashboardAttempts).toBe(2);
    expect(window.location.replace).not.toHaveBeenCalled();
    expect(api.hasAuthMarker()).toBe(true);
  });

  it("multiple concurrent failing protected API requests for authenticated session trigger exactly one redirect", async () => {
    api.setAuthMarker();

    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Session invalid" }),
    });

    global.fetch = fetchMock;

    // Launch 3 simultaneous failing API calls
    const results = await Promise.allSettled([
      api.getDashboardSummary(),
      api.getMessages({ page: 1, pageSize: 20 }),
      api.getWebhookEndpoints(),
    ]);

    results.forEach((r) => expect(r.status).toBe("rejected"));

    // Exactly one redirect must occur
    expect(window.location.replace).toHaveBeenCalledTimes(1);
    expect(window.location.replace).toHaveBeenCalledWith("/login?reason=session_expired");
  });

  it("login endpoint returning 401 does NOT trigger session expired redirect", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Invalid email or password" }),
    });

    global.fetch = fetchMock;

    await expect(api.loginApi("bad@email.com", "wrongpass")).rejects.toThrow(
      "Invalid email or password"
    );

    expect(window.location.replace).not.toHaveBeenCalled();
  });

  it("already being on /login does not trigger redirect", async () => {
    api.setAuthMarker();

    Object.defineProperty(window, "location", {
      value: {
        ...originalLocation,
        pathname: "/login",
        href: "http://localhost:3000/login",
        search: "",
        replace: vi.fn(),
        assign: vi.fn(),
      },
      writable: true,
      configurable: true,
    });

    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Unauthorized" }),
    });

    global.fetch = fetchMock;

    await expect(api.getDashboardSummary()).rejects.toThrow();
    expect(window.location.replace).not.toHaveBeenCalled();
  });

  it("exportStatusLogsCsvApi returning 401 after refresh failure triggers session expired redirect when authenticated", async () => {
    api.setAuthMarker();

    const fetchMock = vi.fn();

    // 1. Initial export request returns 401
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Unauthorized" }),
    });

    // 2. Refresh attempt returns 401
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Refresh failed" }),
    });

    global.fetch = fetchMock;

    await expect(api.exportStatusLogsCsvApi({})).rejects.toThrow();

    expect(window.location.replace).toHaveBeenCalledTimes(1);
    expect(window.location.replace).toHaveBeenCalledWith("/login?reason=session_expired");
  });

  it("login page renders concise session expired message when reason=session_expired query param is present", () => {
    mockSearchParams = new URLSearchParams("?reason=session_expired");

    render(<LoginPage />);

    expect(
      screen.getByText("Your session expired. Please sign in again.")
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Please sign in to continue.")
    ).not.toBeInTheDocument();
  });

  it("login page renders neutral sign-in message when reason=sign_in_required query param is present", () => {
    mockSearchParams = new URLSearchParams("?reason=sign_in_required");

    render(<LoginPage />);

    expect(
      screen.getByText("Please sign in to continue.")
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Your session expired. Please sign in again.")
    ).not.toBeInTheDocument();
  });

  it("login page does not render session expired or sign-in message on clean login navigation", () => {
    mockSearchParams = new URLSearchParams("");

    render(<LoginPage />);

    expect(
      screen.queryByText("Your session expired. Please sign in again.")
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText("Please sign in to continue.")
    ).not.toBeInTheDocument();
  });
});
