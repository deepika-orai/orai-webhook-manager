import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import SuperAdminPage from "../app/admin/page";
import * as api from "../lib/api";
import { AuthSession, PlatformSummary, CreateTenantResponse, RotateKeyResponse, AdminTenantSummary } from "../types/auth";

const mockReplace = vi.fn();
const mockPush = vi.fn();
const mockRouter = {
  replace: mockReplace,
  push: mockPush,
};

vi.mock("next/navigation", () => ({
  useRouter: () => mockRouter,
}));

describe("SuperAdminPage Webhook URL Display and Copy Modals", () => {
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
    totalTenants: 1,
    activeTenants: 1,
    suspendedTenants: 0,
    totalMessages: 100,
    failedMessages: 0,
    pendingInbox: 0,
    deadLetterInbox: 0,
  };

  const tenantsResult = {
    items: [
      {
        id: "tenant-1",
        name: "Acme Enterprise",
        slug: "acme-enterprise",
        isActive: true,
        adminEmail: "admin@acme.com",
        adminFullName: "Acme Admin",
        messagesCount: 50,
        endpointsCount: 1,
        createdAt: "2026-08-25T10:00:00Z",
        updatedAt: "2026-08-25T10:00:00Z",
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(adminSession);
    vi.spyOn(api, "getPlatformSummaryApi").mockResolvedValue(platformSummary);
    vi.spyOn(api, "getAdminTenantsApi").mockResolvedValue(tenantsResult);

    Object.defineProperty(navigator, "clipboard", {
      value: {
        writeText: vi.fn().mockResolvedValue(undefined),
      },
      writable: true,
      configurable: true,
    });
  });

  it("displays New Webhook URL label and copies complete URL on tenant onboarding success", async () => {
    const mockOnboardResponse: CreateTenantResponse = {
      tenantId: "tenant-2",
      name: "Cyberdyne Systems",
      slug: "cyberdyne",
      adminUserId: "usr-2",
      adminEmail: "admin@cyberdyne.com",
      tempPassword: "TempPassword!123",
      webhookEndpointId: "ep-1",
      webhookEndpointName: "Default WhatsApp Ingestion",
      webhookUrl: "https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/whk_live_cyberdyne_key_123",
      webhookPlainKey: "whk_live_cyberdyne_key_123",
      webhookKeyPrefix: "whk_live_cyberd",
    };

    vi.spyOn(api, "createTenantApi").mockResolvedValue(mockOnboardResponse);

    render(<SuperAdminPage />);

    await waitFor(() => {
      expect(screen.getByText("ORAI Super Admin")).toBeInTheDocument();
    });

    // Open Onboard modal
    fireEvent.click(screen.getByRole("button", { name: /onboard new client/i }));

    // Fill form
    fireEvent.change(screen.getByPlaceholderText("e.g. Acme Corporation"), {
      target: { value: "Cyberdyne Systems" },
    });
    fireEvent.change(screen.getByPlaceholderText("admin@acme.com"), {
      target: { value: "admin@cyberdyne.com" },
    });
    fireEvent.change(screen.getByPlaceholderText("John Doe"), {
      target: { value: "Miles Dyson" },
    });

    // Submit form
    fireEvent.click(screen.getByRole("button", { name: /create tenant & credentials/i }));

    // Success modal appears
    await waitFor(() => {
      expect(screen.getByText("Client Onboarded Successfully")).toBeInTheDocument();
    });

    // Verify New Webhook URL label and complete URL
    expect(screen.getByText("New Webhook URL:")).toBeInTheDocument();
    expect(screen.getByText("https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/whk_live_cyberdyne_key_123")).toBeInTheDocument();

    // Verify Copy Webhook URL button
    const copyWebhookButton = screen.getByRole("button", { name: /copy webhook url/i });
    expect(copyWebhookButton).toBeInTheDocument();

    // Click copy and verify clipboard call
    fireEvent.click(copyWebhookButton);
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
      "https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/whk_live_cyberdyne_key_123"
    );
  });

  it("displays New Webhook URL label and copies complete URL on webhook key rotation", async () => {
    window.confirm = vi.fn().mockReturnValue(true);
    global.confirm = vi.fn().mockReturnValue(true);

    const mockTenantSummary: AdminTenantSummary = {
      id: "tenant-1",
      name: "Acme Enterprise",
      slug: "acme-enterprise",
      isActive: true,
      createdAt: "2026-08-25T10:00:00Z",
      updatedAt: "2026-08-25T10:00:00Z",
      users: [],
      endpoints: [
        {
          endpointId: "ep-999",
          name: "Default WhatsApp Ingestion",
          keyPrefix: "whk_live_oldpre",
          status: "Active",
          lastReceivedAt: null,
          revokedAt: null,
          createdAt: "2026-08-25T10:00:00Z",
        },
      ],
      totalMessages: 50,
      failedMessages: 0,
    };

    const mockRotateResponse: RotateKeyResponse = {
      endpointId: "ep-999",
      plainKey: "whk_live_rotated_super_secret_key_456",
      keyPrefix: "whk_live_rotate",
      webhookUrl: "https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/whk_live_rotated_super_secret_key_456",
    };

    vi.spyOn(api, "getAdminTenantSummaryApi").mockResolvedValue(mockTenantSummary);
    vi.spyOn(api, "rotateWebhookKeyApi").mockResolvedValue(mockRotateResponse);

    render(<SuperAdminPage />);

    await waitFor(() => {
      expect(screen.getByText("Acme Enterprise")).toBeInTheDocument();
    });

    // Inspect tenant summary modal via endpoints count button
    fireEvent.click(screen.getByTitle("View Endpoints and Users"));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /rotate key/i })).toBeInTheDocument();
    });

    // Rotate key
    fireEvent.click(screen.getByRole("button", { name: /rotate key/i }));

    // Success modal appears
    await waitFor(() => {
      expect(screen.getByText("Webhook Key Rotated Successfully")).toBeInTheDocument();
    });

    // Verify New Webhook URL label and complete URL
    expect(screen.getByText("New Webhook URL:")).toBeInTheDocument();
    expect(screen.getByText("https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/whk_live_rotated_super_secret_key_456")).toBeInTheDocument();

    // Verify Copy Webhook URL button
    const copyWebhookButton = screen.getByRole("button", { name: /copy webhook url/i });
    expect(copyWebhookButton).toBeInTheDocument();

    // Click copy and verify clipboard call with full URL
    fireEvent.click(copyWebhookButton);
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
      "https://oraiapi.azurewebsites.net/api/webhooks/whatsapp/whk_live_rotated_super_secret_key_456"
    );
  });

  it("replaces raw numeric enum values (0, 1, 2) and unknown values with meaningful labels in inspection modal", async () => {
    const mockNumericTenantSummary: AdminTenantSummary = {
      id: "tenant-1",
      name: "Acme Enterprise",
      slug: "acme-enterprise",
      isActive: true,
      createdAt: "2026-08-25T10:00:00Z",
      updatedAt: "2026-08-25T10:00:00Z",
      users: [
        {
          userId: "u-1",
          email: "admin@acme.com",
          fullName: "Alice Admin",
          role: 0, // TenantAdmin
          isActive: true,
          mustChangePassword: false,
          createdAt: "2026-08-25T10:00:00Z",
        },
        {
          userId: "u-2",
          email: "member@acme.com",
          fullName: "Bob Member",
          role: 1, // Member
          isActive: true,
          mustChangePassword: false,
          createdAt: "2026-08-25T10:00:00Z",
        },
        {
          userId: "u-3",
          email: "viewer@acme.com",
          fullName: "Charlie Viewer",
          role: 2, // ReadOnly
          isActive: true,
          mustChangePassword: false,
          createdAt: "2026-08-25T10:00:00Z",
        },
        {
          userId: "u-4",
          email: "unknown@acme.com",
          fullName: "Dana Unknown",
          role: 99, // Unknown
          isActive: true,
          mustChangePassword: false,
          createdAt: "2026-08-25T10:00:00Z",
        },
      ],
      endpoints: [
        {
          endpointId: "ep-0",
          name: "Primary Ingestion",
          keyPrefix: "whk_0",
          status: 0, // Active
          lastReceivedAt: null,
          revokedAt: null,
          createdAt: "2026-08-25T10:00:00Z",
        },
        {
          endpointId: "ep-1",
          name: "Secondary Ingestion",
          keyPrefix: "whk_1",
          status: 1, // Suspended
          lastReceivedAt: null,
          revokedAt: null,
          createdAt: "2026-08-25T10:00:00Z",
        },
        {
          endpointId: "ep-2",
          name: "Legacy Ingestion",
          keyPrefix: "whk_2",
          status: 2, // Revoked
          lastReceivedAt: null,
          revokedAt: null,
          createdAt: "2026-08-25T10:00:00Z",
        },
        {
          endpointId: "ep-3",
          name: "Future Ingestion",
          keyPrefix: "whk_3",
          status: 99, // Unknown
          lastReceivedAt: null,
          revokedAt: null,
          createdAt: "2026-08-25T10:00:00Z",
        },
      ],
      totalMessages: 100,
      failedMessages: 0,
    };

    vi.spyOn(api, "getAdminTenantSummaryApi").mockResolvedValue(mockNumericTenantSummary);

    render(<SuperAdminPage />);

    await waitFor(() => {
      expect(screen.getByText("Acme Enterprise")).toBeInTheDocument();
    });

    // Open inspection modal
    fireEvent.click(screen.getByTitle("View Endpoints and Users"));

    await waitFor(() => {
      expect(screen.getByText("Primary Ingestion")).toBeInTheDocument();
    });

    // Webhook endpoint status labels formatted
    expect(screen.getByText("Prefix: whk_0 • Status: Active")).toBeInTheDocument();
    expect(screen.getByText("Prefix: whk_1 • Status: Suspended")).toBeInTheDocument();
    expect(screen.getByText("Prefix: whk_2 • Status: Revoked")).toBeInTheDocument();
    expect(screen.getByText("Prefix: whk_3 • Status: Unknown")).toBeInTheDocument();

    // Client user role badges formatted
    expect(screen.getByText("Tenant Admin")).toBeInTheDocument();
    expect(screen.getByText("Member")).toBeInTheDocument();
    expect(screen.getByText("Read Only")).toBeInTheDocument();
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });
});
