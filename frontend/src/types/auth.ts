export interface User {
  id: string;
  email: string;
  fullName: string;
  isPlatformAdmin: boolean;
  mustChangePassword: boolean;
  isActive: boolean;
}

export interface Tenant {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  role: string;
}

export interface AuthSession {
  user: User;
  tenant: Tenant | null;
}

export interface LoginResponse {
  succeeded: boolean;
  user: User;
  tenant: Tenant | null;
  mustChangePassword: boolean;
}

export interface PlatformSummary {
  totalTenants: number;
  activeTenants: number;
  suspendedTenants: number;
  totalMessages: number;
  failedMessages: number;
  pendingInbox: number;
  deadLetterInbox: number;
}

export interface AdminTenantListItem {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  endpointsCount: number;
  messagesCount: number;
  adminEmail: string | null;
  adminFullName: string | null;
}

export interface AdminTenantUser {
  userId: string;
  email: string;
  fullName: string;
  role: string;
  isActive: boolean;
  mustChangePassword: boolean;
  createdAt: string;
}

export interface AdminTenantEndpoint {
  endpointId: string;
  name: string;
  keyPrefix: string;
  status: string;
  lastReceivedAt: string | null;
  revokedAt: string | null;
  createdAt: string;
}

export interface AdminTenantSummary {
  id: string;
  name: string;
  slug: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  users: AdminTenantUser[];
  endpoints: AdminTenantEndpoint[];
  totalMessages: number;
  failedMessages: number;
}

export interface CreateTenantRequest {
  name: string;
  slug: string;
  adminEmail: string;
  adminFullName: string;
}

export interface CreateTenantResponse {
  tenantId: string;
  name: string;
  slug: string;
  adminUserId: string;
  adminEmail: string;
  tempPassword: string;
  webhookEndpointId: string;
  webhookEndpointName: string;
  webhookUrl: string;
  webhookPlainKey: string;
  webhookKeyPrefix: string;
}

export interface ResetPasswordResponse {
  userId: string;
  email: string;
  tempPassword: string;
}

export interface RotateKeyResponse {
  endpointId: string;
  plainKey: string;
  keyPrefix: string;
}
