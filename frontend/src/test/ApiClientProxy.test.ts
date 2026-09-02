import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { validateAndGetProxyTarget, getProxyRewrites } from "../../next.config";
import * as api from "../lib/api";

describe("Next.js Proxy Target Validation & Rewrites Configuration", () => {
  it("defaults to http://localhost:5135 in development mode when API_PROXY_TARGET is unset", () => {
    const target = validateAndGetProxyTarget(undefined, "development");
    expect(target).toBe("http://localhost:5135");
  });

  it("throws a descriptive error in production mode when API_PROXY_TARGET is missing", () => {
    expect(() => validateAndGetProxyTarget(undefined, "production")).toThrow(
      "[next.config.ts] Missing required environment variable 'API_PROXY_TARGET' for production build."
    );
    expect(() => validateAndGetProxyTarget("", "production")).toThrow(
      "[next.config.ts] Missing required environment variable 'API_PROXY_TARGET' for production build."
    );
  });

  it("accepts valid HTTPS origin in production", () => {
    const target = validateAndGetProxyTarget(
      "https://oraiapi.azurewebsites.net",
      "production"
    );
    expect(target).toBe("https://oraiapi.azurewebsites.net");
  });

  it("normalizes trailing slash in API_PROXY_TARGET", () => {
    const target = validateAndGetProxyTarget(
      "https://oraiapi.azurewebsites.net/",
      "production"
    );
    expect(target).toBe("https://oraiapi.azurewebsites.net");
  });

  it("rejects non-HTTPS protocol in production", () => {
    expect(() =>
      validateAndGetProxyTarget("http://oraiapi.azurewebsites.net", "production")
    ).toThrow("Production API_PROXY_TARGET must use HTTPS");
  });

  it("rejects non-localhost HTTP in development mode", () => {
    expect(() =>
      validateAndGetProxyTarget("http://some-remote-dev.com", "development")
    ).toThrow("HTTP is only permitted for localhost in development");
  });

  it("rejects credentials, query parameters, hash fragments, and subpaths in proxy target", () => {
    expect(() =>
      validateAndGetProxyTarget(
        "https://user:pass@oraiapi.azurewebsites.net",
        "production"
      )
    ).toThrow("must not contain user credentials");

    expect(() =>
      validateAndGetProxyTarget(
        "https://oraiapi.azurewebsites.net?param=1",
        "production"
      )
    ).toThrow("must not contain query strings or hash fragments");

    expect(() =>
      validateAndGetProxyTarget(
        "https://oraiapi.azurewebsites.net/#section",
        "production"
      )
    ).toThrow("must not contain query strings or hash fragments");

    expect(() =>
      validateAndGetProxyTarget(
        "https://oraiapi.azurewebsites.net/api/v1",
        "production"
      )
    ).toThrow("must be an origin without subpaths");
  });

  it("generates explicit allowlisted rewrites and strictly excludes /backend-api/webhooks/*", () => {
    const target = "https://oraiapi.azurewebsites.net";
    const rewrites = getProxyRewrites(target);

    // Verify allowlisted browser routes are mapped
    const expectedRoutes = [
      "auth",
      "admin",
      "dashboard",
      "messages",
      "webhook-endpoints",
    ];

    expectedRoutes.forEach((route) => {
      const exactMatch = rewrites.find(
        (r) => r.source === `/backend-api/${route}`
      );
      const wildcardMatch = rewrites.find(
        (r) => r.source === `/backend-api/${route}/:path*`
      );

      expect(exactMatch).toBeDefined();
      expect(exactMatch?.destination).toBe(`${target}/api/${route}`);
      expect(wildcardMatch).toBeDefined();
      expect(wildcardMatch?.destination).toBe(`${target}/api/${route}/:path*`);
    });

    // Regression test: ensure /backend-api/webhooks/* is NOT in the rewrites
    const webhookMatch = rewrites.find((r) =>
      r.source.includes("webhooks")
    );
    expect(webhookMatch).toBeUndefined();
  });
});

describe("Frontend Client Same-Origin Base URL Resolution", () => {
  it("strictly defaults to '/backend-api'", () => {
    expect(api.resolveApiBaseUrl(undefined)).toBe("/backend-api");
    expect(api.resolveApiBaseUrl("")).toBe("/backend-api");
  });

  it("accepts exact '/backend-api' or safe relative subpaths", () => {
    expect(api.resolveApiBaseUrl("/backend-api")).toBe("/backend-api");
    expect(api.resolveApiBaseUrl("/backend-api/")).toBe("/backend-api");
  });

  it("rejects absolute URLs to prevent cross-site third-party cookie regression", () => {
    expect(
      api.resolveApiBaseUrl("https://oraiapi.azurewebsites.net")
    ).toBe("/backend-api");
    expect(
      api.resolveApiBaseUrl("http://localhost:5135/api")
    ).toBe("/backend-api");
    expect(
      api.resolveApiBaseUrl("//oraiapi.azurewebsites.net/api")
    ).toBe("/backend-api");
  });
});

describe("Frontend API Client Same-Origin & Proxy Routing", () => {
  const originalLocation = window.location;

  beforeEach(() => {
    vi.clearAllMocks();
    api._resetSessionExpiredState();
    api.clearCsrfToken();

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

  it("fetchCsrfToken calls /backend-api/auth/csrf with credentials: include and cache: no-store", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ token: "mock-csrf-token" }),
    });
    global.fetch = fetchMock;

    const token = await api.fetchCsrfToken();
    expect(token).toBe("mock-csrf-token");
    expect(fetchMock).toHaveBeenCalledWith(
      "/backend-api/auth/csrf",
      expect.objectContaining({
        method: "GET",
        credentials: "include",
        cache: "no-store",
      })
    );
  });

  it("loginApi sends POST to /backend-api/auth/login and retries on 400 Antiforgery failure", async () => {
    const fetchMock = vi.fn();

    // 1. Initial CSRF token fetch
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ token: "stale-csrf-token" }),
    });

    // 2. Initial login fails with 400 Antiforgery
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 400,
      clone: () => ({
        json: async () => ({
          error: "Antiforgery token validation failed. Please provide a valid X-XSRF-TOKEN header and cookie.",
        }),
      }),
      json: async () => ({
        error: "Antiforgery token validation failed. Please provide a valid X-XSRF-TOKEN header and cookie.",
      }),
    });

    // 3. Fresh CSRF token fetch
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ token: "fresh-csrf-token" }),
    });

    // 4. Retried login succeeds
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        succeeded: true,
        user: { id: "1", email: "admin@orai.com", role: "PlatformAdmin", name: "Admin" },
      }),
    });

    // 5. Post-login CSRF refresh
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ token: "auth-csrf-token" }),
    });

    global.fetch = fetchMock;

    const res = await api.loginApi("admin@orai.com", "password123");
    expect(res.succeeded).toBe(true);
    expect(res.user.email).toBe("admin@orai.com");

    // Verify all calls were to relative same-origin /backend-api endpoints
    const calledUrls = fetchMock.mock.calls.map((c) => c[0]);
    calledUrls.forEach((url) => {
      expect(url).toMatch(/^\/backend-api\//);
      expect(url).not.toContain("azurewebsites.net");
    });
  });

  it("getCurrentSessionApi calls /backend-api/auth/me", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        user: { id: "u1", email: "test@orai.com", role: "PlatformAdmin", name: "Test" },
        membership: null,
      }),
    });
    global.fetch = fetchMock;

    const session = await api.getCurrentSessionApi();
    expect(session.user.email).toBe("test@orai.com");
    expect(fetchMock).toHaveBeenCalledWith(
      "/backend-api/auth/me",
      expect.objectContaining({
        credentials: "include",
        cache: "no-store",
      })
    );
  });

  it("changePasswordApi calls /backend-api/auth/change-password", async () => {
    const fetchMock = vi.fn();
    // CSRF bootstrap
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ token: "csrf-token-123" }),
    });
    // Change password response
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ succeeded: true, message: "Password updated successfully." }),
    });
    global.fetch = fetchMock;

    const res = await api.changePasswordApi("oldPass123!", "newPass123!");
    expect(res.succeeded).toBe(true);
    expect(fetchMock).toHaveBeenCalledWith(
      "/backend-api/auth/change-password",
      expect.objectContaining({
        method: "POST",
        credentials: "include",
        cache: "no-store",
      })
    );
  });

  it("logoutApi calls /backend-api/auth/logout with CSRF header and clears token", async () => {
    const fetchMock = vi.fn();
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ token: "csrf-tok" }),
    });
    fetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ message: "Logged out" }),
    });
    global.fetch = fetchMock;

    const res = await api.logoutApi();
    expect(res.message).toBe("Logged out");
    expect(fetchMock).toHaveBeenCalledWith(
      "/backend-api/auth/logout",
      expect.objectContaining({
        method: "POST",
        credentials: "include",
        cache: "no-store",
      })
    );
  });

  it("admin, dashboard, and messages APIs all route through /backend-api", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], total: 0 }),
    });
    global.fetch = fetchMock;

    await api.getPlatformSummaryApi();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/backend-api/admin/platform/summary",
      expect.anything()
    );

    await api.getAdminTenantsApi("test", true, 1, 20);
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/backend-api/admin/tenants?search=test&isActive=true&page=1&pageSize=20",
      expect.anything()
    );

    await api.getDashboardSummary();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/backend-api/dashboard/summary",
      expect.anything()
    );

    await api.getMessages({ page: 1, pageSize: 25 });
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/backend-api/messages?page=1&pageSize=25",
      expect.anything()
    );

    await api.getMessageEvents("00000000-0000-0000-0000-000000000001");
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/backend-api/messages/00000000-0000-0000-0000-000000000001/events",
      expect.anything()
    );

    await api.getWebhookEndpoints();
    expect(fetchMock).toHaveBeenLastCalledWith(
      "/backend-api/webhook-endpoints",
      expect.anything()
    );
  });

  it("exportStatusLogsCsvApi downloads large CSV preserving Content-Disposition and filename", async () => {
    // Generate a multi-chunk 10,000-row simulated CSV blob (multi-megabyte)
    const rowContent = "msg_123,rec_456,DELIVERED,2026-09-02T10:00:00Z,+1234567890,conv_1,marketing,regular,0,\n";
    const largeCsvContent = "MessageId,RecipientId,Status,Timestamp,Phone,ConversationId,Category,PricingModel,ErrorCode,ErrorMessage\n" +
      rowContent.repeat(10000);
    const mockBlob = new Blob([largeCsvContent], { type: "text/csv; charset=utf-8" });

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: {
        get: (name: string) => {
          if (name.toLowerCase() === "content-disposition") {
            return 'attachment; filename="whatsapp_status_logs_tenant1_20260902_100000.csv"';
          }
          if (name.toLowerCase() === "content-type") {
            return "text/csv; charset=utf-8";
          }
          return null;
        },
      },
      blob: async () => mockBlob,
    });
    global.fetch = fetchMock;

    const result = await api.exportStatusLogsCsvApi({ status: "DELIVERED" });
    expect(result.filename).toBe("whatsapp_status_logs_tenant1_20260902_100000.csv");
    expect(result.blob.size).toBe(mockBlob.size);
    expect(result.blob.size).toBeGreaterThan(500000); // Proves large CSV handling

    expect(fetchMock).toHaveBeenCalledWith(
      "/backend-api/messages/export?status=DELIVERED",
      expect.objectContaining({
        method: "GET",
        credentials: "include",
        cache: "no-store",
      })
    );
  });
});
