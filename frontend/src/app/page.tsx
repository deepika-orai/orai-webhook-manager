"use client";

import React, { useCallback, useEffect, useState } from "react";
import {
  DashboardSummary,
  MessageFilterState,
  MessageListItem,
  PagedResult,
  WebhookEndpoint,
} from "../types/dashboard";
import {
  getDashboardSummary,
  getMessages,
  getWebhookEndpoints,
} from "../lib/api";
import { Header } from "../components/Header";
import { MetricCard } from "../components/MetricCard";
import { StatusDistribution } from "../components/StatusDistribution";
import { EndpointsList } from "../components/EndpointsList";
import { MessagesTable } from "../components/MessagesTable";
import { MessageDetailModal } from "../components/MessageDetailModal";
import { ErrorState } from "../components/EmptyAndErrorStates";

export default function DashboardPage() {
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

  const loadSummaryAndEndpoints = useCallback(async () => {
    setLoadingSummary(true);
    setLoadingEndpoints(true);
    setSummaryError(null);

    try {
      const [summaryRes, endpointsRes] = await Promise.all([
        getDashboardSummary(),
        getWebhookEndpoints(),
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
  }, []);

  const loadMessages = useCallback(async (currentFilters: MessageFilterState) => {
    setLoadingMessages(true);
    setMessagesError(null);

    try {
      const res = await getMessages(currentFilters);
      setMessagesData(res);
      setLastUpdated(new Date());
    } catch (err) {
      setMessagesError(err instanceof Error ? err.message : "Failed to load messages");
    } finally {
      setLoadingMessages(false);
    }
  }, []);

  // Initial load
  useEffect(() => {
    let isMounted = true;

    async function initialFetch() {
      try {
        setLoadingSummary(true);
        setLoadingEndpoints(true);
        setSummaryError(null);
        const [summaryRes, endpointsRes] = await Promise.all([
          getDashboardSummary(),
          getWebhookEndpoints(),
        ]);
        if (isMounted) {
          setSummary(summaryRes);
          setEndpoints(endpointsRes);
          setLastUpdated(new Date());
        }
      } catch (err) {
        if (isMounted) {
          setSummaryError(err instanceof Error ? err.message : "Failed to load summary");
        }
      } finally {
        if (isMounted) {
          setLoadingSummary(false);
          setLoadingEndpoints(false);
        }
      }
    }

    initialFetch();

    return () => {
      isMounted = false;
    };
  }, []);

  // Messages load on filter change
  useEffect(() => {
    let isMounted = true;

    async function fetchMessagesData() {
      try {
        setLoadingMessages(true);
        setMessagesError(null);
        const res = await getMessages(filters);
        if (isMounted) {
          setMessagesData(res);
          setLastUpdated(new Date());
        }
      } catch (err) {
        if (isMounted) {
          setMessagesError(err instanceof Error ? err.message : "Failed to load messages");
        }
      } finally {
        if (isMounted) {
          setLoadingMessages(false);
        }
      }
    }

    fetchMessagesData();

    return () => {
      isMounted = false;
    };
  }, [filters]);

  // Auto refresh effect
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
    setFilters((prev) => ({ ...prev, ...newFilters }));
  };

  const isGlobalLoading = loadingSummary || loadingEndpoints || loadingMessages;

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900 flex flex-col font-sans">
      <Header
        onRefreshAll={handleRefreshAll}
        loading={isGlobalLoading}
        autoRefresh={autoRefresh}
        onToggleAutoRefresh={() => setAutoRefresh(!autoRefresh)}
        lastUpdated={lastUpdated}
      />

      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6 space-y-6">
        {/* Global Summary Error State */}
        {summaryError && (
          <ErrorState
            title="Failed to connect to ORAI Backend API"
            error={summaryError}
            onRetry={handleRefreshAll}
          />
        )}

        {/* Top Summary Metric Cards */}
        <section>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <MetricCard
              title="Total Messages"
              value={summary?.totalMessages ?? 0}
              subtitle="All tracked WhatsApp messages"
              iconType="total"
            />
            <MetricCard
              title="Delivery Confirmed"
              value={summary ? summary.delivered + summary.read : 0}
              rate={summary?.deliveredRate}
              rateLabel="delivery"
              subtitle="Confirmed WhatsApp delivery"
              iconType="delivered"
            />
            <MetricCard
              title="Read Messages"
              value={summary?.read ?? 0}
              rate={summary?.readRate}
              rateLabel="read rate"
              subtitle="Opened by recipient"
              iconType="read"
            />
            <MetricCard
              title="Failed Messages"
              value={summary?.failed ?? 0}
              rate={summary?.failedRate}
              rateLabel="failed"
              subtitle="Delivery errors & rejections"
              iconType="failed"
            />
          </div>

          {/* Secondary Ingestion Health Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mt-4">
            <MetricCard
              title="Sent (In Flight)"
              value={summary?.sent ?? 0}
              subtitle="Dispatched, awaiting recipient delivery"
              iconType="sent"
            />
            <MetricCard
              title="Pending Inbox Queue"
              value={summary?.pendingInboxCount ?? 0}
              subtitle="Awaiting background worker processing"
              iconType="inbox"
            />
            <MetricCard
              title="Dead Letter Ingestion"
              value={summary?.deadLetterCount ?? 0}
              subtitle="Unrecoverable ingestion payloads"
              iconType="deadletter"
            />
          </div>
        </section>

        {/* Visual Distribution Breakdown */}
        {summary && <StatusDistribution summary={summary} />}

        {/* Registered Endpoints Section */}
        <EndpointsList endpoints={endpoints} loading={loadingEndpoints} />

        {/* Messages Table Error & Content */}
        {messagesError ? (
          <ErrorState
            title="Failed to load message list"
            error={messagesError}
            onRetry={() => loadMessages(filters)}
          />
        ) : (
          <MessagesTable
            data={messagesData}
            filters={filters}
            onFilterChange={handleFilterChange}
            onSelectMessage={(msg) => setSelectedMessage(msg)}
            loading={loadingMessages}
            onRefresh={() => loadMessages(filters)}
          />
        )}
      </main>

      {/* Message Event Detail Modal */}
      {selectedMessage && (
        <MessageDetailModal
          message={selectedMessage}
          onClose={() => setSelectedMessage(null)}
        />
      )}
    </div>
  );
}
