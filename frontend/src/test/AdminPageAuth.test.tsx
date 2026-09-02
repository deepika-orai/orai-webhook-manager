import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import SuperAdminPage from "../app/admin/page";
import * as api from "../lib/api";
import { AuthSession, PlatformSummary } from "../types/auth";

const mockReplace = vi.fn();
const mockPush = vi.fn();
const mockRouter = {
  replace: mockReplace,
  push: mockPush,
};

vi.mock("next/navigation", () => ({
  useRouter: () => mockRouter,
}));

describe("SuperAdminPage Authorization & Data Loading", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("platform admin can access /admin and loads platform data", async () => {
    const adminSession: AuthSession = {
      user: {
        id: "usr-admin-1",
        email: "superadmin@orai.internal",
        fullName: "Chief Administrator",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: null,
    };

    const platformSummary: PlatformSummary = {
      totalTenants: 5,
      activeTenants: 4,
      suspendedTenants: 1,
      totalMessages: 12500,
      failedMessages: 3,
      pendingInbox: 12,
      deadLetterInbox: 0,
    };

    const tenantsResult = {
      items: [
        {
          id: "t-1",
          name: "Acme Corp",
          slug: "acme-corp",
          isActive: true,
          adminEmail: "admin@acme.com",
          adminFullName: "Acme Admin",
          messagesCount: 100,
          endpointsCount: 2,
          createdAt: "2026-08-25T10:00:00Z",
          updatedAt: "2026-08-25T10:00:00Z",
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };

    const getCurrentSessionSpy = vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(adminSession);
    const getPlatformSummarySpy = vi.spyOn(api, "getPlatformSummaryApi").mockResolvedValue(platformSummary);
    const getAdminTenantsSpy = vi.spyOn(api, "getAdminTenantsApi").mockResolvedValue(tenantsResult);

    render(<SuperAdminPage />);

    // Platform admin session verified and content mounted
    await waitFor(() => {
      expect(screen.getByText("ORAI Super Admin")).toBeInTheDocument();
    });

    await waitFor(() => {
      expect(screen.getByText("Acme Corp")).toBeInTheDocument();
    });

    expect(getCurrentSessionSpy).toHaveBeenCalledTimes(1);
    expect(getPlatformSummarySpy).toHaveBeenCalledTimes(1);
    expect(getAdminTenantsSpy).toHaveBeenCalledTimes(1);

    expect(screen.getByText("Chief Administrator")).toBeInTheDocument();
    expect(mockReplace).not.toHaveBeenCalledWith("/dashboard");
  });

  it("tenant user cannot access /admin and does NOT load admin data", async () => {
    const tenantSession: AuthSession = {
      user: {
        id: "usr-tenant-1",
        email: "tenant@acme.com",
        fullName: "Tenant User",
        isPlatformAdmin: false,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: {
        id: "t-1",
        name: "Acme Corp",
        slug: "acme",
        isActive: true,
        role: "TenantAdmin",
      },
    };

    const getCurrentSessionSpy = vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(tenantSession);
    const getPlatformSummarySpy = vi.spyOn(api, "getPlatformSummaryApi").mockResolvedValue({
      totalTenants: 0,
      activeTenants: 0,
      suspendedTenants: 0,
      totalMessages: 0,
      failedMessages: 0,
      pendingInbox: 0,
      deadLetterInbox: 0,
    });
    const getAdminTenantsSpy = vi.spyOn(api, "getAdminTenantsApi").mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
    });

    render(<SuperAdminPage />);

    // Verifying platform auth should redirect to /dashboard
    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/dashboard");
    });

    expect(getCurrentSessionSpy).toHaveBeenCalledTimes(1);
    // Crucial check: Admin APIs must NOT be called when user is not platform admin
    expect(getPlatformSummarySpy).not.toHaveBeenCalled();
    expect(getAdminTenantsSpy).not.toHaveBeenCalled();

    // Admin UI should not be rendered
    expect(screen.queryByText("ORAI Super Admin")).not.toBeInTheDocument();
  });

  it("unexpected admin API 403 is handled gracefully without unhandled rejection or crashing", async () => {
    const adminSession: AuthSession = {
      user: {
        id: "usr-admin-1",
        email: "superadmin@orai.internal",
        fullName: "Chief Administrator",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: null,
    };

    const forbiddenError = new Error("Forbidden") as Error & { status?: number };
    forbiddenError.status = 403;

    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(adminSession);
    vi.spyOn(api, "getPlatformSummaryApi").mockRejectedValue(forbiddenError);
    vi.spyOn(api, "getAdminTenantsApi").mockRejectedValue(forbiddenError);

    render(<SuperAdminPage />);

    // When admin API returns 403, it redirects to /dashboard gracefully
    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/dashboard");
    });
  });

  it("unauthenticated user cannot access /admin and redirects to login with sign_in_required", async () => {
    const unauthError = new Error("Unauthorized") as Error & { status?: number };
    unauthError.status = 401;

    const getCurrentSessionSpy = vi.spyOn(api, "getCurrentSessionApi").mockRejectedValue(unauthError);

    render(<SuperAdminPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/login?reason=sign_in_required");
    });

    expect(getCurrentSessionSpy).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("ORAI Super Admin")).not.toBeInTheDocument();
  });
});
