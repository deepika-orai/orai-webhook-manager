export interface DashboardSummary {
  totalMessages: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  deliveredRate: number;
  readRate: number;
  failedRate: number;
  pendingInboxCount: number;
  deadLetterCount: number;
}

export interface MessageFilterState {
  page: number;
  pageSize: number;
  status: string;
  search: string;
  dateFrom: string;
  dateTo: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface MessageListItem {
  id: string;
  endpointId: string;
  endpointName: string;
  wamid: string;
  phoneNumberId?: string | null;
  displayPhoneNumber?: string | null;
  recipientPhone?: string | null;
  currentStatus?: string | null;
  statusRank?: number | null;
  lastStatusTimestamp?: string | null;
  conversationId?: string | null;
  conversationOriginType?: string | null;
  conversationExpiresAt?: string | null;
  pricingModel?: string | null;
  pricingCategory?: string | null;
  pricingBillable?: boolean | null;
  activeErrorCode?: string | null;
  activeErrorTitle?: string | null;
  activeErrorMessage?: string | null;
  activeErrorDetails?: string | null;
  lastFailureCode?: string | null;
  lastFailureTimestamp?: string | null;
  lastFailureReason?: string | null;
  bizOpaqueCallbackData?: string | null;
  broadcastId?: string | null;
  broadcastName?: string | null;
  templateName?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface MessageStatusEvent {
  id: string;
  messageId: string;
  wamid: string;
  status: string;
  statusTimestamp: string;
  errorCode?: string | null;
  errorTitle?: string | null;
  errorMessage?: string | null;
  errorDetails?: string | null;
  errorData?: string | null;
  createdAt: string;
}

export interface WebhookEndpoint {
  id: string;
  name: string;
  keyPrefix: string;
  status: string;
  lastReceivedAt?: string | null;
  createdAt: string;
}
