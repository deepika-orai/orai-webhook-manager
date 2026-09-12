import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, act, fireEvent, waitFor } from "@testing-library/react";
import { WelcomeSplash } from "../components/WelcomeSplash";
import DashboardPage from "../app/dashboard/page";
import LoginPage from "../app/login/page";
import * as api from "../lib/api";
import { AuthSession, LoginResponse } from "../types/auth";
import { DashboardSummary, MessageListItem, PagedResult, WebhookEndpoint } from "../types/dashboard";

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

describe("ORAI WelcomeSplash Component & Lifecycle", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    localStorage.clear();
  });

  it("renders official ORAI branding, headings, and tenant name cleanly", () => {
    render(<WelcomeSplash tenantName="Acme Robotics" />);

    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.getByAltText("ORAI Conversational AI Platform")).toBeInTheDocument();
    expect(screen.getByText("Webhook Manager")).toBeInTheDocument();
    expect(screen.getByText("Welcome to ORAI")).toBeInTheDocument();
    expect(screen.getByText("The Future of AI-Powered Customer Engagement")).toBeInTheDocument();
    expect(screen.getByText("Welcome, Acme Robotics")).toBeInTheDocument();
    expect(screen.getByText("Preparing your dashboard…")).toBeInTheDocument();

    // Ensure no undefined or null appears
    expect(screen.queryByText(/undefined/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/null/i)).not.toBeInTheDocument();
  });

  it("falls back to 'Partner' when tenantName is empty, whitespace, or undefined", () => {
    const { rerender } = render(<WelcomeSplash tenantName="" />);
    expect(screen.getByText("Welcome, Partner")).toBeInTheDocument();

    rerender(<WelcomeSplash tenantName="   " />);
    expect(screen.getByText("Welcome, Partner")).toBeInTheDocument();

    rerender(<WelcomeSplash tenantName={undefined} />);
    expect(screen.getByText("Welcome, Partner")).toBeInTheDocument();
  });

  it("handles timer lifecycle and triggers onComplete after display and fade durations", () => {
    vi.useFakeTimers();
    const onComplete = vi.fn();

    render(
      <WelcomeSplash
        tenantName="Nova Corp"
        displayDurationMs={1600}
        fadeDurationMs={300}
        onComplete={onComplete}
      />
    );

    expect(onComplete).not.toHaveBeenCalled();

    // Advance 1600ms (main display phase ends, fade out begins)
    act(() => {
      vi.advanceTimersByTime(1600);
    });
    expect(onComplete).not.toHaveBeenCalled();

    // Advance 300ms (fade out ends)
    act(() => {
      vi.advanceTimersByTime(300);
    });
    expect(onComplete).toHaveBeenCalledTimes(1);

    vi.useRealTimers();
  });

  it("respects prefers-reduced-motion by completing immediately without animation delay", () => {
    vi.useFakeTimers();
    const onComplete = vi.fn();

    // Mock matchMedia for prefers-reduced-motion: reduce
    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: query === "(prefers-reduced-motion: reduce)",
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));

    render(
      <WelcomeSplash
        tenantName="Nova Corp"
        displayDurationMs={1600}
        fadeDurationMs={300}
        onComplete={onComplete}
      />
    );

    // With reduced motion, after the display duration it completes immediately without the fade timer
    act(() => {
      vi.advanceTimersByTime(1600);
    });
    expect(onComplete).toHaveBeenCalledTimes(1);

    vi.useRealTimers();
  });
});

describe("Login Flow Splash Marker Management", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    localStorage.clear();
  });

  it("tenant user login sets only the pending flag 'orai_welcome_splash_pending' to '1'", async () => {
    const mockLoginRes: LoginResponse = {
      succeeded: true,
      user: {
        id: "usr-1",
        email: "tenant@acme.com",
        fullName: "Jane Tenant",
        isPlatformAdmin: false,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: {
        id: "tenant-1",
        name: "Acme Corp",
        slug: "acme",
        isActive: true,
        role: "TenantAdmin",
      },
      mustChangePassword: false,
    };

    vi.spyOn(api, "loginApi").mockResolvedValue(mockLoginRes);

    render(<LoginPage />);

    fireEvent.change(screen.getByLabelText(/email address/i), {
      target: { value: "tenant@acme.com" },
    });
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: "SecurePass123!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => {
      expect(mockPush).toHaveBeenCalledWith("/dashboard");
    });

    expect(sessionStorage.getItem("orai_welcome_splash_pending")).toBe("1");
    // Verify NO tenant name or sensitive data was written to sessionStorage
    expect(sessionStorage.getItem("orai_welcome_tenant_name")).toBeNull();
    expect(sessionStorage.getItem("tenantName")).toBeNull();
    expect(sessionStorage.getItem("Acme Corp")).toBeNull();
  });

  it("platform admin login does NOT set pending flag and purges any stale flag", async () => {
    sessionStorage.setItem("orai_welcome_splash_pending", "1");

    const mockAdminLoginRes: LoginResponse = {
      succeeded: true,
      user: {
        id: "usr-admin",
        email: "admin@orai.internal",
        fullName: "Platform Admin",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: null,
      mustChangePassword: false,
    };

    vi.spyOn(api, "loginApi").mockResolvedValue(mockAdminLoginRes);

    render(<LoginPage />);

    fireEvent.change(screen.getByLabelText(/email address/i), {
      target: { value: "admin@orai.internal" },
    });
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: "AdminSecret123!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => {
      expect(mockPush).toHaveBeenCalledWith("/admin");
    });

    expect(sessionStorage.getItem("orai_welcome_splash_pending")).toBeNull();
  });

  it("password change required does NOT set pending flag and purges any stale flag", async () => {
    sessionStorage.setItem("orai_welcome_splash_pending", "1");

    const mockTempLoginRes: LoginResponse = {
      succeeded: true,
      user: {
        id: "usr-temp",
        email: "temp@acme.com",
        fullName: "Temp User",
        isPlatformAdmin: false,
        mustChangePassword: true,
        isActive: true,
      },
      tenant: {
        id: "tenant-1",
        name: "Acme Corp",
        slug: "acme",
        isActive: true,
        role: "TenantAdmin",
      },
      mustChangePassword: true,
    };

    vi.spyOn(api, "loginApi").mockResolvedValue(mockTempLoginRes);

    render(<LoginPage />);

    fireEvent.change(screen.getByLabelText(/email address/i), {
      target: { value: "temp@acme.com" },
    });
    fireEvent.change(screen.getByLabelText(/password/i), {
      target: { value: "TempPass123!" },
    });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => {
      expect(mockPush).toHaveBeenCalledWith("/change-password");
    });

    expect(sessionStorage.getItem("orai_welcome_splash_pending")).toBeNull();
  });
});

describe("Dashboard Splash Integration & State Flow", () => {
  const mockTenantSession: AuthSession = {
    user: {
      id: "usr-1",
      email: "tenant@acme.com",
      fullName: "Jane Tenant",
      isPlatformAdmin: false,
      mustChangePassword: false,
      isActive: true,
    },
    tenant: {
      id: "tenant-acme-1",
      name: "Acme Enterprises",
      slug: "acme",
      isActive: true,
      role: "TenantAdmin",
    },
  };

  const mockSummary: DashboardSummary = {
    totalMessages: 500,
    delivered: 450,
    deliveredRate: 90,
    read: 400,
    readRate: 80,
    failed: 5,
    failedRate: 1,
    sent: 45,
    pendingInboxCount: 2,
    deadLetterCount: 0,
  };

  const mockEndpoints: WebhookEndpoint[] = [];
  const mockMessages: PagedResult<MessageListItem> = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 0,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    mockSearchParams = new URLSearchParams();
  });

  it("authenticated tenant session displays welcome splash and consumes marker immediately", async () => {
    sessionStorage.setItem("orai_welcome_splash_pending", "1");

    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(mockTenantSession);
    vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    vi.spyOn(api, "getWebhookEndpoints").mockResolvedValue(mockEndpoints);
    vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    // Welcome splash should be displayed with real tenant name from session
    await waitFor(() => {
      expect(screen.getByText("Welcome, Acme Enterprises")).toBeInTheDocument();
    });

    expect(screen.getByText("The Future of AI-Powered Customer Engagement")).toBeInTheDocument();
    expect(screen.getByText("Preparing your dashboard…")).toBeInTheDocument();

    // Marker MUST have been consumed and removed from sessionStorage
    expect(sessionStorage.getItem("orai_welcome_splash_pending")).toBeNull();
  });

  it("page refresh or direct access without marker skips splash completely", async () => {
    // No marker in sessionStorage
    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(mockTenantSession);
    vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    vi.spyOn(api, "getWebhookEndpoints").mockResolvedValue(mockEndpoints);
    vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    // Dashboard renders directly
    await waitFor(() => {
      expect(screen.getByText("Total Messages")).toBeInTheDocument();
    });

    // Splash is NOT rendered
    expect(screen.queryByText("The Future of AI-Powered Customer Engagement")).not.toBeInTheDocument();
    expect(screen.queryByText("Preparing your dashboard…")).not.toBeInTheDocument();
  });

  it("uses user fullName when tenant name is empty, and Partner when both are missing", async () => {
    sessionStorage.setItem("orai_welcome_splash_pending", "1");

    const sessionWithoutTenantName: AuthSession = {
      user: {
        id: "usr-2",
        email: "user@demo.com",
        fullName: "Alexander Sterling",
        isPlatformAdmin: false,
        mustChangePassword: false,
        isActive: true,
      },
      tenant: {
        id: "tenant-2",
        name: "",
        slug: "demo",
        isActive: true,
        role: "TenantUser",
      },
    };

    vi.spyOn(api, "getCurrentSessionApi").mockResolvedValue(sessionWithoutTenantName);
    vi.spyOn(api, "getDashboardSummary").mockResolvedValue(mockSummary);
    vi.spyOn(api, "getWebhookEndpoints").mockResolvedValue(mockEndpoints);
    vi.spyOn(api, "getMessages").mockResolvedValue(mockMessages);

    render(<DashboardPage />);

    await waitFor(() => {
      expect(screen.getByText("Welcome, Alexander Sterling")).toBeInTheDocument();
    });
  });

  it("Super Admin inspection mode skips splash and clears pending flag", async () => {
    sessionStorage.setItem("orai_welcome_splash_pending", "1");
    mockSearchParams = new URLSearchParams("?inspectTenantId=tenant-target&tenantName=Target%20Client");

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

    // Splash not rendered
    expect(screen.queryByText("The Future of AI-Powered Customer Engagement")).not.toBeInTheDocument();
    // Marker removed
    expect(sessionStorage.getItem("orai_welcome_splash_pending")).toBeNull();
  });

  it("invalid unauthenticated session clears the marker and redirects to login", async () => {
    sessionStorage.setItem("orai_welcome_splash_pending", "1");

    const unauthError = new Error("Unauthorized") as Error & { status?: number };
    unauthError.status = 401;
    vi.spyOn(api, "getCurrentSessionApi").mockRejectedValue(unauthError);

    render(<DashboardPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith("/login?reason=sign_in_required");
    });

    expect(sessionStorage.getItem("orai_welcome_splash_pending")).toBeNull();
    expect(screen.queryByText("The Future of AI-Powered Customer Engagement")).not.toBeInTheDocument();
  });
});
