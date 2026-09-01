/**
 * Formats WebhookEndpointStatus enum values (numeric or string) to human-readable labels.
 * 0: Active, 1: Suspended, 2: Revoked.
 * Unrecognized values safely return "Unknown".
 */
export function formatWebhookEndpointStatus(status: number | string | null | undefined): string {
  if (status === null || status === undefined) {
    return "Unknown";
  }

  if (typeof status === "number" || /^\d+$/.test(String(status).trim())) {
    const num = typeof status === "number" ? status : parseInt(String(status).trim(), 10);
    switch (num) {
      case 0:
        return "Active";
      case 1:
        return "Suspended";
      case 2:
        return "Revoked";
      default:
        return "Unknown";
    }
  }

  const normalized = String(status).trim().toLowerCase();
  switch (normalized) {
    case "active":
      return "Active";
    case "suspended":
      return "Suspended";
    case "revoked":
      return "Revoked";
    default:
      return "Unknown";
  }
}

/**
 * Formats TenantRole enum values (numeric or string) to human-readable labels.
 * 0: Tenant Admin, 1: Member, 2: Read Only.
 * Unrecognized values safely return "Unknown".
 */
export function formatTenantRole(role: number | string | null | undefined): string {
  if (role === null || role === undefined) {
    return "Unknown";
  }

  if (typeof role === "number" || /^\d+$/.test(String(role).trim())) {
    const num = typeof role === "number" ? role : parseInt(String(role).trim(), 10);
    switch (num) {
      case 0:
        return "Tenant Admin";
      case 1:
        return "Member";
      case 2:
        return "Read Only";
      default:
        return "Unknown";
    }
  }

  const normalized = String(role).trim().toLowerCase().replace(/[_\s-]+/g, "");
  switch (normalized) {
    case "tenantadmin":
      return "Tenant Admin";
    case "member":
      return "Member";
    case "readonly":
      return "Read Only";
    default:
      return "Unknown";
  }
}
