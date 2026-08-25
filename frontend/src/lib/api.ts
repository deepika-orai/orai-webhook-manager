import {
  DashboardSummary,
  MessageFilterState,
  MessageListItem,
  MessageStatusEvent,
  PagedResult,
  WebhookEndpoint,
} from "../types/dashboard";

const API_BASE_URL = (
  process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:51234/api"
).replace(/\/+$/, "");

const DEMO_TENANT_ID = process.env.NEXT_PUBLIC_DEMO_TENANT_ID || "";

function getHeaders(): HeadersInit {
  const headers: Record<string, string> = {
    Accept: "application/json",
  };

  if (DEMO_TENANT_ID) {
    headers["X-Tenant-Id"] = DEMO_TENANT_ID;
  }

  return headers;
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let errorMessage = `HTTP Error ${response.status}: ${response.statusText}`;
    try {
      const errBody = await response.json();
      if (errBody && typeof errBody === "object" && "error" in errBody) {
        errorMessage = String(errBody.error);
      }
    } catch {
      // Body not JSON
    }
    throw new Error(errorMessage);
  }
  return response.json() as Promise<T>;
}

export async function getDashboardSummary(): Promise<DashboardSummary> {
  const res = await fetch(`${API_BASE_URL}/dashboard/summary`, {
    headers: getHeaders(),
    cache: "no-store",
  });
  return handleResponse<DashboardSummary>(res);
}

export async function getMessages(
  filters: Partial<MessageFilterState>
): Promise<PagedResult<MessageListItem>> {
  const params = new URLSearchParams();
  if (filters.page) params.set("page", filters.page.toString());
  if (filters.pageSize) params.set("pageSize", filters.pageSize.toString());
  if (filters.status && filters.status !== "ALL") params.set("status", filters.status);
  if (filters.search && filters.search.trim()) params.set("search", filters.search.trim());
  if (filters.dateFrom) params.set("dateFrom", filters.dateFrom);
  if (filters.dateTo) params.set("dateTo", filters.dateTo);

  const queryString = params.toString();
  const url = `${API_BASE_URL}/messages${queryString ? `?${queryString}` : ""}`;

  const res = await fetch(url, {
    headers: getHeaders(),
    cache: "no-store",
  });
  return handleResponse<PagedResult<MessageListItem>>(res);
}

export async function getMessageEvents(
  messageId: string
): Promise<MessageStatusEvent[]> {
  const res = await fetch(`${API_BASE_URL}/messages/${encodeURIComponent(messageId)}/events`, {
    headers: getHeaders(),
    cache: "no-store",
  });
  return handleResponse<MessageStatusEvent[]>(res);
}

export async function getWebhookEndpoints(): Promise<WebhookEndpoint[]> {
  const res = await fetch(`${API_BASE_URL}/webhook-endpoints`, {
    headers: getHeaders(),
    cache: "no-store",
  });
  return handleResponse<WebhookEndpoint[]>(res);
}
