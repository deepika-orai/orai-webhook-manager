import {
  DashboardSummary,
  MessageFilterState,
  MessageListItem,
  MessageStatusEvent,
  PagedResult,
  WebhookEndpoint,
} from "../types/dashboard";
import {
  AdminTenantListItem,
  AdminTenantSummary,
  AuthSession,
  CreateTenantRequest,
  CreateTenantResponse,
  LoginResponse,
  PlatformSummary,
  ResetPasswordResponse,
  RotateKeyResponse,
} from "../types/auth";

const API_BASE_URL = (
  process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5135/api"
).replace(/\/+$/, "");

const DEMO_TENANT_ID = process.env.NEXT_PUBLIC_DEMO_TENANT_ID || "";

let cachedCsrfToken: string | null = null;
let csrfRefreshPromise: Promise<string> | null = null;
let isRedirectingToLogin = false;

export function clearCsrfToken(): void {
  cachedCsrfToken = null;
}

export function _resetSessionExpiredState(): void {
  isRedirectingToLogin = false;
}

export function isExcludedFromSessionExpiredRedirect(url: string): boolean {
  try {
    const parsed = new URL(url, "http://localhost");
    const pathname = parsed.pathname.toLowerCase();
    const excludedPatterns = [
      "/auth/login",
      "/auth/csrf",
      "/auth/refresh",
      "/auth/logout",
    ];
    return excludedPatterns.some(
      (pattern) => pathname.endsWith(pattern) || pathname.includes(`${pattern}/`)
    );
  } catch {
    const lower = url.toLowerCase();
    return (
      lower.includes("/auth/login") ||
      lower.includes("/auth/csrf") ||
      lower.includes("/auth/refresh") ||
      lower.includes("/auth/logout")
    );
  }
}

export function handleSessionExpiredRedirect(): void {
  if (typeof window === "undefined") return;

  const currentPath = window.location.pathname || "";
  if (currentPath === "/login" || currentPath.startsWith("/login/")) {
    return;
  }

  if (isRedirectingToLogin) {
    return;
  }

  isRedirectingToLogin = true;
  clearCsrfToken();

  try {
    window.location.replace("/login?reason=session_expired");
  } catch {
    if (window.location) {
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination
      window.location.href = "/login?reason=session_expired";
    }
  }
}

export function getCsrfTokenFromCookie(): string | null {
  if (typeof document === "undefined") return null;
  const match = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/);
  return match ? decodeURIComponent(match[1]) : null;
}

export async function fetchCsrfToken(): Promise<string> {
  if (csrfRefreshPromise) {
    return csrfRefreshPromise;
  }

  csrfRefreshPromise = (async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/auth/csrf`, {
        method: "GET",
        credentials: "include",
        headers: {
          Accept: "application/json",
        },
      });
      if (res.ok) {
        const data = await res.json();
        if (data?.token) {
          cachedCsrfToken = data.token;
          return data.token;
        }
      }
    } catch {
      // Ignore error
    } finally {
      csrfRefreshPromise = null;
    }
    return cachedCsrfToken || "";
  })();

  return csrfRefreshPromise;
}

export async function ensureCsrfToken(forceFresh = false): Promise<string> {
  if (forceFresh) {
    cachedCsrfToken = null;
    return await fetchCsrfToken();
  }
  const cookieToken = getCsrfTokenFromCookie();
  if (cookieToken) {
    cachedCsrfToken = cookieToken;
    return cookieToken;
  }
  if (cachedCsrfToken) {
    return cachedCsrfToken;
  }
  return await fetchCsrfToken();
}

function getHeaders(customHeaders?: Record<string, string>): HeadersInit {
  const headers: Record<string, string> = {
    Accept: "application/json",
    ...customHeaders,
  };

  if (DEMO_TENANT_ID && typeof window !== "undefined" && !customHeaders?.["X-Explicit-Tenant"]) {
    // Only fallback if explicitly in demo mode without credentials
    // headers["X-Tenant-Id"] = DEMO_TENANT_ID;
  }

  return headers;
}

async function requestWithRefresh<T>(
  url: string,
  options: RequestInit = {},
  isAuthRetry = false,
  isCsrfRetry = false
): Promise<T> {
  const method = (options.method || "GET").toUpperCase();
  const customHeaders: Record<string, string> = {
    ...(options.headers as Record<string, string>),
  };

  if (["POST", "PUT", "PATCH", "DELETE"].includes(method)) {
    const csrfToken = await ensureCsrfToken();
    if (csrfToken && !customHeaders["X-XSRF-TOKEN"]) {
      customHeaders["X-XSRF-TOKEN"] = csrfToken;
    }
  }

  const finalOptions: RequestInit = {
    ...options,
    credentials: "include",
    headers: getHeaders(customHeaders),
  };

  const response = await fetch(url, finalOptions);

  // Handle stale CSRF token: retry exactly once only when response is specifically an Antiforgery 400 failure
  if (response.status === 400 && !isCsrfRetry && ["POST", "PUT", "PATCH", "DELETE"].includes(method)) {
    let isCsrfFailure = false;
    try {
      const cloned = response.clone();
      const errBody = await cloned.json();
      if (
        errBody &&
        typeof errBody === "object" &&
        typeof errBody.error === "string" &&
        errBody.error.toLowerCase().includes("antiforgery")
      ) {
        isCsrfFailure = true;
      }
    } catch {
      // Non-JSON response
    }

    if (isCsrfFailure) {
      const freshToken = await ensureCsrfToken(true);
      const retryHeaders: Record<string, string> = {
        ...(options.headers as Record<string, string>),
      };
      if (freshToken) {
        retryHeaders["X-XSRF-TOKEN"] = freshToken;
      }
      return requestWithRefresh<T>(
        url,
        {
          ...options,
          headers: retryHeaders,
        },
        isAuthRetry,
        true
      );
    }
  }

  if (response.status === 401) {
    if (!isAuthRetry && !isExcludedFromSessionExpiredRedirect(url)) {
      try {
        const csrfToken = await ensureCsrfToken();
        const refreshHeaders: Record<string, string> = {};
        if (csrfToken) {
          refreshHeaders["X-XSRF-TOKEN"] = csrfToken;
        }

        const refreshRes = await fetch(`${API_BASE_URL}/auth/refresh`, {
          method: "POST",
          credentials: "include",
          headers: getHeaders(refreshHeaders),
        });

        if (refreshRes.ok) {
          await ensureCsrfToken(true);
          return requestWithRefresh<T>(url, options, true, isCsrfRetry);
        }
      } catch {
        // Refresh failed
      }
    }

    if (!isExcludedFromSessionExpiredRedirect(url)) {
      handleSessionExpiredRedirect();
    }
  }

  if (!response.ok) {
    let errorMessage = `HTTP Error ${response.status}: ${response.statusText}`;
    try {
      const errBody = await response.json();
      if (errBody && typeof errBody === "object") {
        if ("error" in errBody && typeof errBody.error === "string") {
          errorMessage = errBody.error;
        } else if ("message" in errBody && typeof errBody.message === "string") {
          errorMessage = errBody.message;
        }
      }
    } catch {
      // Non JSON
    }
    const error = new Error(errorMessage) as Error & { status?: number };
    error.status = response.status;
    throw error;
  }

  return response.json() as Promise<T>;
}

// ---------------- Authentication APIs ----------------

export async function loginApi(email: string, password: string): Promise<LoginResponse> {
  const csrfToken = await ensureCsrfToken();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    Accept: "application/json",
  };
  if (csrfToken) {
    headers["X-XSRF-TOKEN"] = csrfToken;
  }

  let res = await fetch(`${API_BASE_URL}/auth/login`, {
    method: "POST",
    headers,
    credentials: "include",
    body: JSON.stringify({ email, password }),
  });

  if (res.status === 400) {
    try {
      const cloned = res.clone();
      const errBody = await cloned.json();
      if (
        errBody &&
        typeof errBody === "object" &&
        typeof errBody.error === "string" &&
        errBody.error.toLowerCase().includes("antiforgery")
      ) {
        const freshToken = await ensureCsrfToken(true);
        const retryHeaders = { ...headers, "X-XSRF-TOKEN": freshToken };
        res = await fetch(`${API_BASE_URL}/auth/login`, {
          method: "POST",
          headers: retryHeaders,
          credentials: "include",
          body: JSON.stringify({ email, password }),
        });
      }
    } catch {
      // Ignore
    }
  }

  if (!res.ok) {
    let errorMsg = "Invalid email or password";
    try {
      const data = await res.json();
      if (data?.error) errorMsg = data.error;
    } catch {
      // Ignore
    }
    throw new Error(errorMsg);
  }

  const result: LoginResponse = await res.json();

  // After successful login: clear anonymous CSRF token and fetch fresh authenticated token
  cachedCsrfToken = null;
  await fetchCsrfToken();

  return result;
}

export async function refreshApi(): Promise<LoginResponse> {
  return requestWithRefresh<LoginResponse>(`${API_BASE_URL}/auth/refresh`, {
    method: "POST",
  });
}

export async function logoutApi(): Promise<{ message: string }> {
  const csrfToken = await ensureCsrfToken();
  const headers: Record<string, string> = {};
  if (csrfToken) {
    headers["X-XSRF-TOKEN"] = csrfToken;
  }

  let res = await fetch(`${API_BASE_URL}/auth/logout`, {
    method: "POST",
    credentials: "include",
    headers: getHeaders(headers),
  });

  if (res.status === 400) {
    try {
      const cloned = res.clone();
      const errBody = await cloned.json();
      if (
        errBody &&
        typeof errBody === "object" &&
        typeof errBody.error === "string" &&
        errBody.error.toLowerCase().includes("antiforgery")
      ) {
        const freshToken = await ensureCsrfToken(true);
        const retryHeaders = { ...headers, "X-XSRF-TOKEN": freshToken };
        res = await fetch(`${API_BASE_URL}/auth/logout`, {
          method: "POST",
          credentials: "include",
          headers: getHeaders(retryHeaders),
        });
      }
    } catch {
      // Ignore
    }
  }

  // Clear cached token only after the response
  cachedCsrfToken = null;

  return res.ok ? res.json() : { message: "Logged out" };
}

export async function changePasswordApi(
  currentPassword: string,
  newPassword: string
): Promise<{ succeeded: boolean; message: string }> {
  return requestWithRefresh<{ succeeded: boolean; message: string }>(
    `${API_BASE_URL}/auth/change-password`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentPassword, newPassword }),
    }
  );
}

export async function getCurrentSessionApi(): Promise<AuthSession> {
  return requestWithRefresh<AuthSession>(`${API_BASE_URL}/auth/me`);
}

// ---------------- Admin APIs ----------------

export async function getPlatformSummaryApi(): Promise<PlatformSummary> {
  return requestWithRefresh<PlatformSummary>(`${API_BASE_URL}/admin/platform/summary`);
}

export async function getAdminTenantsApi(
  search?: string,
  isActive?: boolean,
  page = 1,
  pageSize = 20
): Promise<PagedResult<AdminTenantListItem>> {
  const params = new URLSearchParams();
  if (search && search.trim()) params.set("search", search.trim());
  if (typeof isActive === "boolean") params.set("isActive", isActive.toString());
  params.set("page", page.toString());
  params.set("pageSize", pageSize.toString());

  const qs = params.toString();
  return requestWithRefresh<PagedResult<AdminTenantListItem>>(
    `${API_BASE_URL}/admin/tenants${qs ? `?${qs}` : ""}`
  );
}

export async function getAdminTenantSummaryApi(tenantId: string): Promise<AdminTenantSummary> {
  return requestWithRefresh<AdminTenantSummary>(
    `${API_BASE_URL}/admin/tenants/${encodeURIComponent(tenantId)}/summary`
  );
}

export async function createTenantApi(
  payload: CreateTenantRequest
): Promise<CreateTenantResponse> {
  return requestWithRefresh<CreateTenantResponse>(`${API_BASE_URL}/admin/tenants`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

export async function updateTenantStatusApi(
  tenantId: string,
  isActive: boolean
): Promise<{ succeeded: boolean; message: string }> {
  return requestWithRefresh<{ succeeded: boolean; message: string }>(
    `${API_BASE_URL}/admin/tenants/${encodeURIComponent(tenantId)}/status`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ isActive }),
    }
  );
}

export async function resetClientPasswordApi(
  tenantId: string
): Promise<ResetPasswordResponse> {
  return requestWithRefresh<ResetPasswordResponse>(
    `${API_BASE_URL}/admin/tenants/${encodeURIComponent(tenantId)}/reset-client-password`,
    {
      method: "POST",
    }
  );
}

export async function rotateWebhookKeyApi(
  endpointId: string
): Promise<RotateKeyResponse> {
  return requestWithRefresh<RotateKeyResponse>(
    `${API_BASE_URL}/admin/webhook-endpoints/${encodeURIComponent(endpointId)}/rotate-key`,
    {
      method: "POST",
    }
  );
}

// ---------------- Tenant Dashboard APIs ----------------

export async function getDashboardSummary(customTenantHeader?: string): Promise<DashboardSummary> {
  const headers: Record<string, string> = {};
  if (customTenantHeader) {
    headers["X-Tenant-Id"] = customTenantHeader;
  }
  return requestWithRefresh<DashboardSummary>(`${API_BASE_URL}/dashboard/summary`, {
    headers,
    cache: "no-store",
  });
}

export async function getMessages(
  filters: Partial<MessageFilterState>,
  customTenantHeader?: string
): Promise<PagedResult<MessageListItem>> {
  const params = new URLSearchParams();
  if (filters.page) params.set("page", filters.page.toString());
  if (filters.pageSize) params.set("pageSize", filters.pageSize.toString());
  if (filters.status && filters.status !== "ALL") params.set("status", filters.status);
  if (filters.search && filters.search.trim()) params.set("search", filters.search.trim());
  if (filters.dateFrom) params.set("dateFrom", filters.dateFrom);
  if (filters.dateTo) params.set("dateTo", filters.dateTo);

  const headers: Record<string, string> = {};
  if (customTenantHeader) {
    headers["X-Tenant-Id"] = customTenantHeader;
  }

  const queryString = params.toString();
  const url = `${API_BASE_URL}/messages${queryString ? `?${queryString}` : ""}`;

  return requestWithRefresh<PagedResult<MessageListItem>>(url, {
    headers,
    cache: "no-store",
  });
}

export async function getMessageEvents(
  messageId: string,
  customTenantHeader?: string
): Promise<MessageStatusEvent[]> {
  const headers: Record<string, string> = {};
  if (customTenantHeader) {
    headers["X-Tenant-Id"] = customTenantHeader;
  }
  return requestWithRefresh<MessageStatusEvent[]>(
    `${API_BASE_URL}/messages/${encodeURIComponent(messageId)}/events`,
    {
      headers,
      cache: "no-store",
    }
  );
}

export async function getWebhookEndpoints(customTenantHeader?: string): Promise<WebhookEndpoint[]> {
  const headers: Record<string, string> = {};
  if (customTenantHeader) {
    headers["X-Tenant-Id"] = customTenantHeader;
  }
  return requestWithRefresh<WebhookEndpoint[]>(`${API_BASE_URL}/webhook-endpoints`, {
    headers,
    cache: "no-store",
  });
}

export async function exportStatusLogsCsvApi(
  filters: Partial<MessageFilterState>,
  customTenantHeader?: string
): Promise<{ blob: Blob; filename: string }> {
  const params = new URLSearchParams();
  if (filters.status && filters.status !== "ALL") params.set("status", filters.status);
  if (filters.search && filters.search.trim()) params.set("search", filters.search.trim());
  if (filters.dateFrom) params.set("dateFrom", filters.dateFrom);
  if (filters.dateTo) params.set("dateTo", filters.dateTo);

  const headers: Record<string, string> = {
    Accept: "text/csv",
  };
  if (customTenantHeader) {
    headers["X-Tenant-Id"] = customTenantHeader;
  }

  const queryString = params.toString();
  const url = `${API_BASE_URL}/messages/export${queryString ? `?${queryString}` : ""}`;

  let res = await fetch(url, {
    method: "GET",
    credentials: "include",
    headers: getHeaders(headers),
  });

  if (res.status === 401) {
    try {
      const csrfToken = await ensureCsrfToken();
      const refreshHeaders: Record<string, string> = {};
      if (csrfToken) {
        refreshHeaders["X-XSRF-TOKEN"] = csrfToken;
      }
      const refreshRes = await fetch(`${API_BASE_URL}/auth/refresh`, {
        method: "POST",
        credentials: "include",
        headers: getHeaders(refreshHeaders),
      });
      if (refreshRes.ok) {
        await ensureCsrfToken(true);
        res = await fetch(url, {
          method: "GET",
          credentials: "include",
          headers: getHeaders(headers),
        });
      }
    } catch {
      // Refresh failed
    }

    if (res.status === 401) {
      handleSessionExpiredRedirect();
    }
  }

  if (!res.ok) {
    let errorMsg = `Export failed (HTTP ${res.status})`;
    try {
      const errJson = await res.json();
      if (errJson?.error) errorMsg = errJson.error;
    } catch {
      // Non-JSON error
    }
    throw new Error(errorMsg);
  }

  const contentDisposition = res.headers.get("Content-Disposition") || "";
  let filename = "whatsapp_status_logs.csv";
  const match = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/i);
  if (match && match[1]) {
    filename = match[1].replace(/['"]/g, "").trim();
  }

  const blob = await res.blob();
  return { blob, filename };
}
