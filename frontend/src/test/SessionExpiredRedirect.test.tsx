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

  it("protected API 401 triggers refresh attempt, and on refresh failure triggers exactly one redirect", async () => {
    const fetchMock = vi.fn();

    // 1. Initial protected request returns 401
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      json: async () => ({ error: "Token expired" }),
    });

    // 2. Refresh attempt returns 401 (refresh failed)
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
  });

  it("protected API 401 does NOT redirect if refresh succeeds and retry succeeds", async () => {
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
  });

  it("multiple concurrent failing protected API requests trigger exactly one redirect", async () => {
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

  it("exportStatusLogsCsvApi returning 401 after refresh failure triggers session expired redirect", async () => {
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
  });

  it("login page does not render session expired message on clean login navigation", () => {
    mockSearchParams = new URLSearchParams("");

    render(<LoginPage />);

    expect(
      screen.queryByText("Your session expired. Please sign in again.")
    ).not.toBeInTheDocument();
  });
});
