import { describe, it, expect } from "vitest";
import { formatWebhookEndpointStatus, formatTenantRole } from "../lib/enumFormatters";

describe("Enum Formatters Utility", () => {
  describe("formatWebhookEndpointStatus", () => {
    it("correctly maps numeric enum values to strings", () => {
      expect(formatWebhookEndpointStatus(0)).toBe("Active");
      expect(formatWebhookEndpointStatus(1)).toBe("Suspended");
      expect(formatWebhookEndpointStatus(2)).toBe("Revoked");
    });

    it("correctly maps string numeric values to strings", () => {
      expect(formatWebhookEndpointStatus("0")).toBe("Active");
      expect(formatWebhookEndpointStatus("1")).toBe("Suspended");
      expect(formatWebhookEndpointStatus("2")).toBe("Revoked");
    });

    it("correctly handles named string values regardless of case", () => {
      expect(formatWebhookEndpointStatus("Active")).toBe("Active");
      expect(formatWebhookEndpointStatus("active")).toBe("Active");
      expect(formatWebhookEndpointStatus("Suspended")).toBe("Suspended");
      expect(formatWebhookEndpointStatus("suspended")).toBe("Suspended");
      expect(formatWebhookEndpointStatus("Revoked")).toBe("Revoked");
      expect(formatWebhookEndpointStatus("revoked")).toBe("Revoked");
    });

    it("safely falls back to Unknown for unrecognized, null, or undefined values", () => {
      expect(formatWebhookEndpointStatus(99)).toBe("Unknown");
      expect(formatWebhookEndpointStatus("99")).toBe("Unknown");
      expect(formatWebhookEndpointStatus("random_status")).toBe("Unknown");
      expect(formatWebhookEndpointStatus(null)).toBe("Unknown");
      expect(formatWebhookEndpointStatus(undefined)).toBe("Unknown");
      expect(formatWebhookEndpointStatus("")).toBe("Unknown");
    });
  });

  describe("formatTenantRole", () => {
    it("correctly maps numeric enum values to role titles", () => {
      expect(formatTenantRole(0)).toBe("Tenant Admin");
      expect(formatTenantRole(1)).toBe("Member");
      expect(formatTenantRole(2)).toBe("Read Only");
    });

    it("correctly maps string numeric values to role titles", () => {
      expect(formatTenantRole("0")).toBe("Tenant Admin");
      expect(formatTenantRole("1")).toBe("Member");
      expect(formatTenantRole("2")).toBe("Read Only");
    });

    it("correctly handles named string variants regardless of case and separators", () => {
      expect(formatTenantRole("TenantAdmin")).toBe("Tenant Admin");
      expect(formatTenantRole("tenantadmin")).toBe("Tenant Admin");
      expect(formatTenantRole("tenant_admin")).toBe("Tenant Admin");
      expect(formatTenantRole("Tenant Admin")).toBe("Tenant Admin");
      expect(formatTenantRole("Member")).toBe("Member");
      expect(formatTenantRole("member")).toBe("Member");
      expect(formatTenantRole("ReadOnly")).toBe("Read Only");
      expect(formatTenantRole("readonly")).toBe("Read Only");
      expect(formatTenantRole("read_only")).toBe("Read Only");
      expect(formatTenantRole("Read Only")).toBe("Read Only");
    });

    it("safely falls back to Unknown for unrecognized, null, or undefined values", () => {
      expect(formatTenantRole(99)).toBe("Unknown");
      expect(formatTenantRole("99")).toBe("Unknown");
      expect(formatTenantRole("SuperUser")).toBe("Unknown");
      expect(formatTenantRole(null)).toBe("Unknown");
      expect(formatTenantRole(undefined)).toBe("Unknown");
      expect(formatTenantRole("")).toBe("Unknown");
    });
  });
});
