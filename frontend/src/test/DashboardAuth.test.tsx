import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import DashboardPage from "../app/dashboard/page";
import DashboardLayout from "../app/dashboard/layout";
import { ProtectedLayout } from "../components/ProtectedLayout";
import * as api from "../lib/api";
import { AuthSession } from "../types/auth";
import { DashboardSummary, PagedResult, MessageListItem, WebhookEndpoint } from "../types/dashboard";

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

describe("Protected Dashboard Authentication & Layout Guards", () => {
  const mockTenantSession: AuthSession = {
    user: {
      id: "usr-1",
      email: "tenant@acme.com",
      fullName: "Acme Tenant User",
      isPlatformAdmin: false,
      mustChangePassword: false,
      isActive: true,
    },
    tenant: {
      id: "tenant-acme-1",
      name: "Acme Corporation",
      slug: "acme",
      isActive: true,
      role: "TenantAdmin",
    },
  };

  const mockSummary: DashboardSummary = {
    totalMessages: 1540,
    delivered: 1400,
    deliveredRate: 90.9,
    read: 1200,
    readRate: 77.9,
    failed: 10,
    failedRate: 0.6,
    sent: 130,
    pendingInboxCount: 5,
    deadLetterCount: 0,
  };

  const mockEndpoints: WebhookEndpoint[] = [
    {
      id: "ep-1",
      name: "Production Ingestion",
      keyPrefix: "whsec_1234",
      status: "ACTIVE",
      createdAt: "2026-08-01T00:00:00Z",
    },
  ];

  const mockMessages: PagedResult<MessageListItem> = {
    items: [
      {
        id: "msg-1",
        endpointId: "ep-1",
        endpointName: "Production Ingestion",
        wamid: "wamid.HBgL12345",
        currentStatus: "DELIVERED",
        recipientPhone: "+1234567890",
        createdAt: "2026-08-31T10:00:00Z",
        updatedAt: "2026-08-31T10:00:00Z",
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams = new URLSearchParams();
  });

  it("pending authentication: protected dashboard content is NOT rendered and shows loading screen", async () => {
    // Session check promise remains unresolved (pending)
    const pendingPromise = new Promise<AuthSession>(() => {});
    vi.spyOn(api, "getCurrentSessionApi").mockReturnValue(pendingPromise);
    const getSummarySpy = vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    const getMessagesSpy = vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    // Shows loading scene while pending
    expect(screen.getByText("ORAI Webhook Manager")).toBeInTheDocument();
    expect(screen.getByText(/loading tenant telemetry dashboard|verifying/i)).toBeInTheDocument();

    // Critical: Protected dashboard content must NOT be visible/rendered
    expect(screen.queryByText("Total Messages")).not.toBeInTheDocument();
    expect(screen.queryByText("KPI Overview")).not.toBeInTheDocument();
    expect(screen.queryByText("Acme Corporation")).not.toBeInTheDocument();
    expect(screen.queryByText("Messages Feed")).not.toBeInTheDocument();

    // Data APIs must NOT be called while auth is pending
    expect(getSummarySpy).not.toHaveBeenCalled();
    expect(getMessagesSpy).not.toHaveBeenCalled();
  });

  it("unauthenticated: immediately redirects to /login with replace navigation and does not render dashboard content", async () => {
    const unauthError = new Error("Unauthorized") as Error & { status?: number };
    unauthError.status = 401;

    const sessionSpy = vi.spyOn(api, "getCurrentSessionApi").mockRejectedValue(unauthError);
    const getSummarySpy = vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    const getMessagesSpy = vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    // Should trigger replace navigation to /login?reason=sign_in_required
    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/login?reason=sign_in_required");
    });

    expect(sessionSpy).toHaveBeenCalledTimes(1);

    // Protected dashboard data APIs should never be called
    expect(getSummarySpy).not.toHaveBeenCalled();
    expect(getMessagesSpy).not.toHaveBeenCalled();

    // Dashboard shell/content must never be visible
    expect(screen.queryByText("Total Messages")).not.toBeInTheDocument();
    expect(screen.queryByText("Acme Corporation")).not.toBeInTheDocument();
  });

  it("authenticated: renders protected dashboard content and loads telemetry data", async () => {
    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(mockTenantSession);
    const getSummarySpy = vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    const getEndpointsSpy = vi.spyOn(api, "getWebhookEndpoints").mockResolvedValue(mockEndpoints);
    const getMessagesSpy = vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    // Wait for session and data to resolve
    await waitFor(() => {
      expect(screen.getByText("Total Messages")).toBeInTheDocument();
    });

    expect(screen.getByText("Acme Corporation")).toBeInTheDocument();
    expect(screen.getByTitle("Signed in as tenant@acme.com")).toBeInTheDocument();

    expect(getSummarySpy).toHaveBeenCalledTimes(1);
    expect(getEndpointsSpy).toHaveBeenCalledTimes(1);
    expect(getMessagesSpy).toHaveBeenCalledTimes(1);

    expect(mockReplace).not.toHaveBeenCalledWith("/login");
  });

  it("password change required: redirects to /change-password using replace navigation", async () => {
    const tempSession: AuthSession = {
      user: {
        id: "usr-temp",
        email: "temp@acme.com",
        fullName: "Temporary User",
        isPlatformAdmin: false,
        mustChangePassword: true,
        isActive: true,
      },
      tenant: mockTenantSession.tenant,
    };

    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(tempSession);
    const getSummarySpy = vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);

    render(<DashboardPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/change-password");
    });

    expect(getSummarySpy).not.toHaveBeenCalled();
    expect(screen.queryByText("Total Messages")).not.toBeInTheDocument();
  });

  it("platform admin on dashboard without inspection mode redirects to /admin", async () => {
    const adminSession: AuthSession = {
      user: {
        id: "usr-admin",
        email: "superadmin@orai.internal",
        fullName: "Platform Admin",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: null,
    };

    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(adminSession);
    const getSummarySpy = vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);

    render(<DashboardPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/admin");
    });

    expect(getSummarySpy).not.toHaveBeenCalled();
    expect(screen.queryByText("Total Messages")).not.toBeInTheDocument();
  });

  it("platform admin on dashboard with inspectTenantId renders inspection telemetry", async () => {
    mockSearchParams = new URLSearchParams("?inspectTenantId=tenant-target-1&tenantName=Target%20Corp");

    const adminSession: AuthSession = {
      user: {
        id: "usr-admin",
        email: "superadmin@orai.internal",
        fullName: "Platform Admin",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: null,
    };

    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(adminSession);
    vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    vi.spyOn(api, "getWebhookEndpoints").mockResolvedValue(mockEndpoints);
    vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    await waitFor(() => {
      expect(screen.getByText("Total Messages")).toBeInTheDocument();
    });

    expect(screen.getByText("Target Corp")).toBeInTheDocument();
    expect(mockReplace).not.toHaveBeenCalledWith("/admin");
    expect(mockReplace).not.toHaveBeenCalledWith("/login");
  });

  it("shared ProtectedLayout blocks children when unauthenticated and replaces route to /login", async () => {
    const unauthError = new Error("Unauthorized") as Error & { status?: number };
    unauthError.status = 401;

    vi.spyOn(api, "getCurrentSessionApi").mockRejectedValue(unauthError);

    render(
      <ProtectedLayout>
        <div data-testid="secret-protected-child">Secret Protected Content</div>
      </ProtectedLayout>
    );

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/login?reason=sign_in_required");
    });

    expect(screen.queryByTestId("secret-protected-child")).not.toBeInTheDocument();
  });

  it("shared DashboardLayout renders children when authenticated", async () => {
    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(mockTenantSession);

    render(
      <DashboardLayout>
        <div data-testid="dashboard-content-child">Dashboard Child Content</div>
      </DashboardLayout>
    );

    await waitFor(() => {
      expect(screen.getByTestId("dashboard-content-child")).toBeInTheDocument();
    });

    expect(mockReplace).not.toHaveBeenCalledWith("/login");
  });
});
