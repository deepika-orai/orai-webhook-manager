"use client";

import React, { Suspense, useCallback, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import {
  DashboardSummary,
  MessageFilterState,
  MessageListItem,
  PagedResult,
  WebhookEndpoint,
} from "../../types/dashboard";
import { AuthSession } from "../../types/auth";
import {
  getCurrentSessionApi,
  getDashboardSummary,
  getMessages,
  getWebhookEndpoints,
  logoutApi,
} from "../../lib/api";
import { Header } from "../../components/Header";
import { MetricCard } from "../../components/MetricCard";
import { StatusDistribution } from "../../components/StatusDistribution";
import { EndpointsList } from "../../components/EndpointsList";
import { MessagesTable } from "../../components/MessagesTable";
import { MessageDetailModal } from "../../components/MessageDetailModal";
import { ErrorState } from "../../components/EmptyAndErrorStates";

function DashboardContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const inspectTenantId = searchParams.get("inspectTenantId") || undefined;
  const inspectTenantName = searchParams.get("tenantName") || undefined;

  const [session, setSession] = useState<AuthSession | null>(null);
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [endpoints, setEndpoints] = useState<WebhookEndpoint[]>([]);
  const [messagesData, setMessagesData] = useState<PagedResult<MessageListItem> | null>(null);
  const [selectedMessage, setSelectedMessage] = useState<MessageListItem | null>(null);

  const [loadingSummary, setLoadingSummary] = useState(true);
  const [loadingEndpoints, setLoadingEndpoints] = useState(true);
  const [loadingMessages, setLoadingMessages] = useState(true);

  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [messagesError, setMessagesError] = useState<string | null>(null);

  const [autoRefresh, setAutoRefresh] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const [filters, setFilters] = useState<MessageFilterState>({
    page: 1,
    pageSize: 20,
    status: "ALL",
    search: "",
    dateFrom: "",
    dateTo: "",
  });

  // Verify auth session
  useEffect(() => {
    async function checkAuth() {
      try {
        const sess = await getCurrentSessionApi();
        setSession(sess);
      } catch {
        // If not in demo mode and unauthenticated, redirect to /login
        const isDemo = !!process.env.NEXT_PUBLIC_DEMO_TENANT_ID;
        if (!isDemo && !inspectTenantId) {
          router.push("/login");
        }
      }
    }
    checkAuth();
  }, [router, inspectTenantId]);

  const loadSummaryAndEndpoints = useCallback(async () => {
    setLoadingSummary(true);
    setLoadingEndpoints(true);
    setSummaryError(null);

    try {
      const [summaryRes, endpointsRes] = await Promise.all([
        getDashboardSummary(inspectTenantId),
        getWebhookEndpoints(inspectTenantId),
      ]);
      setSummary(summaryRes);
      setEndpoints(endpointsRes);
      setLastUpdated(new Date());
    } catch (err) {
      setSummaryError(err instanceof Error ? err.message : "Failed to load summary");
    } finally {
      setLoadingSummary(false);
      setLoadingEndpoints(false);
    }
  }, [inspectTenantId]);

  const loadMessages = useCallback(
    async (currentFilters: MessageFilterState) => {
      setLoadingMessages(true);
      setMessagesError(null);

      try {
        const res = await getMessages(currentFilters, inspectTenantId);
        setMessagesData(res);
        setLastUpdated(new Date());
      } catch (err) {
        setMessagesError(err instanceof Error ? err.message : "Failed to load messages");
      } finally {
        setLoadingMessages(false);
      }
    },
    [inspectTenantId]
  );

  // Initial load
  useEffect(() => {
    let ignore = false;
    async function init() {
      if (ignore) return;
      await loadSummaryAndEndpoints();
      await loadMessages(filters);
    }
    init();
    return () => {
      ignore = true;
    };
  }, [loadSummaryAndEndpoints, loadMessages, filters]);

  // Auto-refresh timer
  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(() => {
      loadSummaryAndEndpoints();
      loadMessages(filters);
    }, 15000);

    return () => clearInterval(interval);
  }, [autoRefresh, filters, loadSummaryAndEndpoints, loadMessages]);

  const handleRefreshAll = () => {
    loadSummaryAndEndpoints();
    loadMessages(filters);
  };

  const handleFilterChange = (newFilters: Partial<MessageFilterState>) => {
    const updated = { ...filters, ...newFilters };
    setFilters(updated);
    loadMessages(updated);
  };

  const handleLogout = async () => {
    await logoutApi();
    router.push("/login");
  };

  const activeTenantName =
    inspectTenantName || session?.tenant?.name || process.env.NEXT_PUBLIC_DEMO_TENANT_NAME;

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900 flex flex-col font-sans">
      <Header
        onRefreshAll={handleRefreshAll}
        loading={loadingSummary || loadingMessages || loadingEndpoints}
        autoRefresh={autoRefresh}
        onToggleAutoRefresh={() => setAutoRefresh(!autoRefresh)}
        lastUpdated={lastUpdated}
        tenantName={activeTenantName}
        userEmail={session?.user?.email}
        isPlatformAdmin={session?.user?.isPlatformAdmin}
        inspectionMode={!!inspectTenantId}
        onLogout={handleLogout}
      />

      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6 space-y-6">
        {/* Error state if summary failed to load */}
        {summaryError && !summary && (
          <ErrorState
            error={summaryError}
            onRetry={loadSummaryAndEndpoints}
          />
        )}

        {/* Section 1: KPI Metrics Overview */}
        <section aria-labelledby="kpi-overview-heading">
          <h2 id="kpi-overview-heading" className="sr-only">KPI Overview</h2>
          <div className="grid grid-cols-2 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3 sm:gap-4">
            <MetricCard
              title="Total Messages"
              value={summary?.totalMessages ?? 0}
              iconType="total"
              subtitle="All tracked messages"
            />
            <MetricCard
              title="Delivered"
              value={summary?.delivered ?? 0}
              rate={summary?.deliveredRate}
              rateLabel="delivery rate"
              iconType="delivered"
              subtitle="Confirmed on device"
            />
            <MetricCard
              title="Read"
              value={summary?.read ?? 0}
              rate={summary?.readRate}
              rateLabel="read rate"
              iconType="read"
              subtitle="Opened by user"
            />
            <MetricCard
              title="Failed"
              value={summary?.failed ?? 0}
              rate={summary?.failedRate}
              rateLabel="failure rate"
              iconType="failed"
              subtitle="Delivery errors"
            />
            <MetricCard
              title="Pending Inbox"
              value={summary?.pendingInboxCount ?? 0}
              iconType="inbox"
              subtitle="Awaiting ingestion"
            />
            <MetricCard
              title="Dead Letters"
              value={summary?.deadLetterCount ?? 0}
              iconType="deadletter"
              subtitle="Exhausted retries"
            />
          </div>
        </section>

        {/* Section 2: Distribution & Webhook Ingestion Endpoints */}
        <section className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <div className="lg:col-span-2">
            {summary ? (
              <StatusDistribution summary={summary} />
            ) : (
              <div className="bg-white rounded-xl border border-slate-200/80 p-5 shadow-xs text-center text-slate-400 py-12">
                Loading status distribution...
              </div>
            )}
          </div>
          <div>
            <EndpointsList endpoints={endpoints} />
          </div>
        </section>

        {/* Section 3: Detailed Messages Observability Table */}
        <section aria-labelledby="messages-table-heading" className="space-y-4">
          <h2 id="messages-table-heading" className="sr-only">Messages Feed</h2>
          {messagesError && (
            <div className="p-3.5 rounded-xl bg-rose-50 border border-rose-200 text-rose-700 text-xs flex items-center gap-2">
              <span>⚠️ {messagesError}</span>
            </div>
          )}
          <MessagesTable
            data={messagesData}
            filters={filters}
            onFilterChange={handleFilterChange}
            onSelectMessage={(msg) => setSelectedMessage(msg)}
            loading={loadingMessages}
            onRefresh={handleRefreshAll}
          />
        </section>
      </main>

      {/* Message Events Timeline & Detail Modal */}
      {selectedMessage && (
        <MessageDetailModal
          message={selectedMessage}
          onClose={() => setSelectedMessage(null)}
          customTenantHeader={inspectTenantId}
        />
      )}
    </div>
  );
}

export default function DashboardPage() {
  return (
    <Suspense fallback={<div className="min-h-screen bg-slate-50 flex items-center justify-center text-slate-500">Loading dashboard...</div>}>
      <DashboardContent />
    </Suspense>
  );
}
