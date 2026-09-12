import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import LoginPage from "../app/login/page";
import * as api from "../lib/api";
import { LoginResponse } from "../types/auth";

const mockPush = vi.fn();
let mockSearchParams = new URLSearchParams();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: mockPush,
    replace: vi.fn(),
  }),
  useSearchParams: () => mockSearchParams,
}));

describe("Shared Login Page & Password Visibility Toggle", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams = new URLSearchParams();
    window.sessionStorage.clear();
  });

  it("renders password input as hidden type='password' initially with accessible label and Edge suppression class", () => {
    render(<LoginPage />);

    const passwordInput = screen.getByLabelText(/^Password/i);
    expect(passwordInput).toBeInTheDocument();
    expect(passwordInput).toHaveAttribute("type", "password");
    expect(passwordInput).toHaveAttribute("autoComplete", "current-password");
    expect(passwordInput).toHaveClass("password-visibility-input");

    const toggleBtn = screen.getByRole("button", { name: "Show password" });
    expect(toggleBtn).toBeInTheDocument();
    expect(toggleBtn).toHaveAttribute("type", "button");
    expect(toggleBtn).toHaveAttribute("aria-pressed", "false");
    expect(toggleBtn).toHaveAttribute("title", "Show password");
  });

  it("applies the password-visibility-input class to suppress Microsoft Edge native password reveal and clear controls", () => {
    render(<LoginPage />);

    const passwordInput = screen.getByLabelText(/^Password/i);
    expect(passwordInput.className).toContain("password-visibility-input");
  });

  it("toggles password input type to 'text' and label to 'Hide password' upon click", () => {
    render(<LoginPage />);

    const passwordInput = screen.getByLabelText(/^Password/i);
    const toggleBtn = screen.getByRole("button", { name: "Show password" });

    fireEvent.click(toggleBtn);

    expect(passwordInput).toHaveAttribute("type", "text");
    expect(toggleBtn).toHaveAttribute("aria-label", "Hide password");
    expect(toggleBtn).toHaveAttribute("aria-pressed", "true");
    expect(toggleBtn).toHaveAttribute("title", "Hide password");
  });

  it("toggles back to 'password' type on second click", () => {
    render(<LoginPage />);

    const passwordInput = screen.getByLabelText(/^Password/i);
    const toggleBtn = screen.getByRole("button", { name: "Show password" });

    // Show password
    fireEvent.click(toggleBtn);
    expect(passwordInput).toHaveAttribute("type", "text");

    // Hide password
    fireEvent.click(toggleBtn);
    expect(passwordInput).toHaveAttribute("type", "password");
    expect(toggleBtn).toHaveAttribute("aria-label", "Show password");
    expect(toggleBtn).toHaveAttribute("aria-pressed", "false");
  });

  it("preserves typed password value through multiple toggles", () => {
    render(<LoginPage />);

    const passwordInput = screen.getByLabelText(/^Password/i) as HTMLInputElement;
    const toggleBtn = screen.getByRole("button", { name: "Show password" });

    fireEvent.change(passwordInput, { target: { value: "SuperSecretKey99!" } });
    expect(passwordInput.value).toBe("SuperSecretKey99!");

    // Toggle on
    fireEvent.click(toggleBtn);
    expect(passwordInput.value).toBe("SuperSecretKey99!");
    expect(passwordInput).toHaveAttribute("type", "text");

    // Toggle off
    fireEvent.click(toggleBtn);
    expect(passwordInput.value).toBe("SuperSecretKey99!");
    expect(passwordInput).toHaveAttribute("type", "password");
  });

  it("does not submit the form when the toggle button is clicked", () => {
    const loginSpy = vi.spyOn(api, "loginApi").mockResolvedValue({
      token: "jwt",
      user: {
        id: "u-1",
        email: "admin@orai.io",
        fullName: "Platform Admin",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
    } as unknown as LoginResponse);

    render(<LoginPage />);

    const emailInput = screen.getByLabelText(/Email Address/i);
    const passwordInput = screen.getByLabelText(/^Password/i);
    const toggleBtn = screen.getByRole("button", { name: "Show password" });

    fireEvent.change(emailInput, { target: { value: "admin@orai.io" } });
    fireEvent.change(passwordInput, { target: { value: "valid-password" } });

    fireEvent.click(toggleBtn);

    expect(loginSpy).not.toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });

  it("supports native keyboard space/enter activation on the toggle button", () => {
    render(<LoginPage />);

    const passwordInput = screen.getByLabelText(/^Password/i);
    const toggleBtn = screen.getByRole("button", { name: "Show password" });

    toggleBtn.focus();
    expect(document.activeElement).toBe(toggleBtn);

    fireEvent.click(toggleBtn);
    expect(passwordInput).toHaveAttribute("type", "text");

    fireEvent.click(toggleBtn);
    expect(passwordInput).toHaveAttribute("type", "password");
  });

  it("submits Super Admin login and redirects to /admin", async () => {
    const loginSpy = vi.spyOn(api, "loginApi").mockResolvedValue({
      token: "jwt",
      user: {
        id: "admin-1",
        email: "superadmin@orai.io",
        fullName: "Super Admin",
        isPlatformAdmin: true,
        mustChangePassword: false,
        isActive: true,
      },
    } as unknown as LoginResponse);

    render(<LoginPage />);

    const emailInput = screen.getByLabelText(/Email Address/i);
    const passwordInput = screen.getByLabelText(/^Password/i);
    const submitBtn = screen.getByRole("button", { name: /Sign In/i });

    fireEvent.change(emailInput, { target: { value: "superadmin@orai.io" } });
    fireEvent.change(passwordInput, { target: { value: "AdminSecret123!" } });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(loginSpy).toHaveBeenCalledWith("superadmin@orai.io", "AdminSecret123!");
      expect(mockPush).toHaveBeenCalledWith("/admin");
    });
  });

  it("submits Tenant user login, stores splash pending flag, and redirects to /dashboard", async () => {
    const loginSpy = vi.spyOn(api, "loginApi").mockResolvedValue({
      token: "jwt",
      user: {
        id: "tenant-1",
        email: "tenant@acme.com",
        fullName: "Acme User",
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
    } as unknown as LoginResponse);

    render(<LoginPage />);

    const emailInput = screen.getByLabelText(/Email Address/i);
    const passwordInput = screen.getByLabelText(/^Password/i);
    const submitBtn = screen.getByRole("button", { name: /Sign In/i });

    fireEvent.change(emailInput, { target: { value: "tenant@acme.com" } });
    fireEvent.change(passwordInput, { target: { value: "TenantPass123!" } });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(loginSpy).toHaveBeenCalledWith("tenant@acme.com", "TenantPass123!");
      expect(window.sessionStorage.getItem("orai_welcome_splash_pending")).toBe("1");
      expect(mockPush).toHaveBeenCalledWith("/dashboard");
    });
  });

  it("preserves password visibility state and displays error banner on login failure without revealing password", async () => {
    vi.spyOn(api, "loginApi").mockRejectedValue(new Error("Invalid email or password"));

    render(<LoginPage />);

    const emailInput = screen.getByLabelText(/Email Address/i);
    const passwordInput = screen.getByLabelText(/^Password/i);
    const submitBtn = screen.getByRole("button", { name: /Sign In/i });

    fireEvent.change(emailInput, { target: { value: "wrong@domain.com" } });
    fireEvent.change(passwordInput, { target: { value: "WrongPass" } });

    // Ensure it starts hidden
    expect(passwordInput).toHaveAttribute("type", "password");

    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Invalid email or password");
    });

    // Password must remain hidden and not auto-revealed
    expect(passwordInput).toHaveAttribute("type", "password");
  });
});
