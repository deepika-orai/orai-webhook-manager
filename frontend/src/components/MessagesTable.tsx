"use client";

import React, { useState } from "react";
import { MessageFilterState, MessageListItem, PagedResult } from "../types/dashboard";
import { exportStatusLogsCsvApi } from "../lib/api";
import { StatusBadge } from "./StatusBadge";
import { EmptyState } from "./EmptyAndErrorStates";

interface MessagesTableProps {
  data: PagedResult<MessageListItem> | null;
  filters: MessageFilterState;
  onFilterChange: (newFilters: Partial<MessageFilterState>) => void;
  onSelectMessage: (message: MessageListItem) => void;
  loading: boolean;
  onRefresh: () => void;
  customTenantHeader?: string;
}

export function MessagesTable({
  data,
  filters,
  onFilterChange,
  onSelectMessage,
  loading,
  onRefresh,
  customTenantHeader,
}: MessagesTableProps) {
  const [searchInput, setSearchInput] = useState(filters.search);
  const [exportPeriod, setExportPeriod] = useState<"7d" | "30d" | "90d" | "custom">("7d");
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const handleExportCsv = async () => {
    setExportError(null);
    let exportDateFrom: string | undefined;
    let exportDateTo: string | undefined;

    const now = new Date();
    if (exportPeriod === "7d") {
      exportDateTo = now.toISOString();
      exportDateFrom = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString();
    } else if (exportPeriod === "30d") {
      exportDateTo = now.toISOString();
      exportDateFrom = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString();
    } else if (exportPeriod === "90d") {
      exportDateTo = now.toISOString();
      exportDateFrom = new Date(now.getTime() - 90 * 24 * 60 * 60 * 1000).toISOString();
    } else if (exportPeriod === "custom") {
      if (!filters.dateFrom || !filters.dateTo) {
        setExportError("Please select both a Start Date and End Date in the dashboard filters for custom date range export.");
        return;
      }
      if (new Date(filters.dateFrom) > new Date(filters.dateTo)) {
        setExportError("Start Date cannot be after End Date.");
        return;
      }
      exportDateFrom = filters.dateFrom;
      exportDateTo = filters.dateTo;
    }

    setExporting(true);
    try {
      const exportFilters: Partial<MessageFilterState> = {
        status: filters.status,
        search: filters.search,
        dateFrom: exportDateFrom,
        dateTo: exportDateTo,
      };
      const { blob, filename } = await exportStatusLogsCsvApi(exportFilters, customTenantHeader);
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (err) {
      setExportError(err instanceof Error ? err.message : "Failed to export CSV");
    } finally {
      setExporting(false);
    }
  };

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onFilterChange({ search: searchInput, page: 1 });
  };

  const handleStatusClick = (status: string) => {
    onFilterChange({ status, page: 1 });
  };

  const handleResetFilters = () => {
    setSearchInput("");
    onFilterChange({
      search: "",
      status: "ALL",
      dateFrom: "",
      dateTo: "",
      page: 1,
    });
  };

  const formatDate = (dateStr?: string | null) => {
    if (!dateStr) return "N/A";
    try {
      const d = new Date(dateStr);
      return d.toLocaleString(undefined, {
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
    } catch {
      return dateStr;
    }
  };

  const totalCount = data?.totalCount || 0;
  const totalPages = data?.totalPages || 1;
  const currentPage = filters.page;
  const pageSize = filters.pageSize;

  const startRecord = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const endRecord = Math.min(currentPage * pageSize, totalCount);

  return (
    <div className="bg-white dark:bg-slate-900/80 rounded-2xl border border-slate-200/80 dark:border-slate-800/90 shadow-xs overflow-hidden">
      {/* Table Header & Controls Bar */}
      <div className="p-5 sm:p-6 border-b border-slate-200/80 dark:border-slate-800/80 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h2 className="text-base font-bold text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-purple-600" />
              WhatsApp Messages
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              Real-time message state transitions & immutable delivery audit logs
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {/* Export Period Selector */}
            <div className="flex items-center gap-1.5 bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-700 rounded-xl px-2.5 py-1">
              <label htmlFor="export-period" className="text-[11px] font-semibold text-slate-500 dark:text-slate-400 whitespace-nowrap">
                Export:
              </label>
              <select
                id="export-period"
                aria-label="Export Period"
                value={exportPeriod}
                onChange={(e) => {
                  setExportPeriod(e.target.value as "7d" | "30d" | "90d" | "custom");
                  setExportError(null);
                }}
                disabled={exporting || loading}
                className="bg-transparent text-xs font-medium text-slate-700 dark:text-slate-200 focus:outline-none cursor-pointer"
              >
                <option value="7d" className="bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-100">Last 7 Days</option>
                <option value="30d" className="bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-100">Last 30 Days</option>
                <option value="90d" className="bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-100">Last 90 Days</option>
                <option value="custom" className="bg-white dark:bg-slate-900 text-slate-900 dark:text-slate-100">Custom Date Range</option>
              </select>
            </div>

            <button
              onClick={handleExportCsv}
              disabled={exporting || loading || (exportPeriod === "custom" && (!filters.dateFrom || !filters.dateTo))}
              className="px-3 py-2 rounded-xl text-xs font-semibold text-slate-700 dark:text-slate-200 hover:text-purple-700 dark:hover:text-purple-300 hover:bg-purple-50 dark:hover:bg-purple-950/40 border border-slate-200 dark:border-slate-700 transition-colors disabled:opacity-50 cursor-pointer flex items-center gap-1.5 shadow-xs"
              title="Download status logs as CSV"
            >
              {exporting ? (
                <>
                  <svg className="w-3.5 h-3.5 animate-spin text-purple-600 dark:text-purple-400" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                  </svg>
                  <span>Exporting...</span>
                </>
              ) : (
                <>
                  <svg className="w-3.5 h-3.5 text-slate-500 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  <span>Download CSV</span>
                </>
              )}
            </button>
            <button
              onClick={onRefresh}
              disabled={loading}
              className="p-2 rounded-xl text-slate-600 dark:text-slate-400 hover:text-purple-700 dark:hover:text-purple-300 hover:bg-purple-50 dark:hover:bg-purple-950/40 border border-slate-200 dark:border-slate-700 transition-colors disabled:opacity-50 cursor-pointer"
              title="Refresh messages table"
            >
              <svg
                className={`w-4 h-4 ${loading ? "animate-spin text-purple-600" : ""}`}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
            </button>
          </div>
        </div>

        {exportError && (
          <div role="alert" className="p-3 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800/60 text-rose-700 dark:text-rose-300 text-xs flex items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <span aria-hidden="true">⚠️</span>
              <span>{exportError}</span>
            </div>
            <button onClick={() => setExportError(null)} className="text-rose-500 hover:text-rose-700 dark:hover:text-rose-200 font-bold text-xs p-1 cursor-pointer">
              ✕
            </button>
          </div>
        )}

        {/* Filter Controls Row */}
        <div className="flex flex-col md:flex-row items-stretch md:items-center justify-between gap-3 pt-1">
          {/* Status Tabs */}
          <div className="flex items-center gap-1.5 overflow-x-auto pb-1 sm:pb-0 scrollbar-none">
            {[
              { label: "All Messages", value: "ALL" },
              { label: "Sent", value: "sent" },
              { label: "Delivered", value: "delivered" },
              { label: "Read", value: "read" },
              { label: "Failed", value: "failed" },
            ].map((tab) => {
              const active = filters.status === tab.value;
              return (
                <button
                  key={tab.value}
                  onClick={() => handleStatusClick(tab.value)}
                  className={`px-3 py-1.5 rounded-xl text-xs font-semibold whitespace-nowrap transition-all cursor-pointer ${
                    active
                      ? "bg-gradient-to-r from-purple-600 to-indigo-600 text-white shadow-sm shadow-purple-600/20"
                      : "bg-slate-50 dark:bg-slate-800/80 text-slate-600 dark:text-slate-300 hover:bg-purple-50 dark:hover:bg-purple-950/40 hover:text-purple-900 dark:hover:text-purple-200 border border-slate-200/80 dark:border-slate-700"
                  }`}
                >
                  {tab.label}
                </button>
              );
            })}
          </div>

          {/* Search Form & Date Range */}
          <div className="flex flex-wrap items-center gap-2">
            <form onSubmit={handleSearchSubmit} className="relative flex-1 sm:w-56">
              <input
                type="text"
                placeholder="Search WAMID or Phone..."
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                className="w-full pl-8 pr-3 py-2 text-xs bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-purple-500/20 focus:border-purple-500 transition-colors text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500"
              />
              <svg
                className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-2.5 top-2.5 pointer-events-none"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
            </form>

            {/* Date Range Inputs */}
            <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400">
              <input
                id="filter-date-from"
                aria-label="Filter from date"
                type="date"
                value={filters.dateFrom ? filters.dateFrom.split("T")[0] : ""}
                onChange={(e) => {
                  const val = e.target.value ? new Date(e.target.value).toISOString() : "";
                  onFilterChange({ dateFrom: val, page: 1 });
                }}
                className="px-2.5 py-1.5 text-xs bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-1 focus:ring-purple-500"
                placeholder="From date"
                title="Filter from date"
              />
              <span className="text-slate-400 dark:text-slate-600">to</span>
              <input
                id="filter-date-to"
                aria-label="Filter to date"
                type="date"
                value={filters.dateTo ? filters.dateTo.split("T")[0] : ""}
                onChange={(e) => {
                  const val = e.target.value ? new Date(new Date(e.target.value).setUTCHours(23, 59, 59, 999)).toISOString() : "";
                  onFilterChange({ dateTo: val, page: 1 });
                }}
                className="px-2.5 py-1.5 text-xs bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-1 focus:ring-purple-500"
                placeholder="To date"
                title="Filter to date"
              />
            </div>

            {(filters.search || filters.status !== "ALL" || filters.dateFrom || filters.dateTo) && (
              <button
                onClick={handleResetFilters}
                className="px-3 py-2 text-xs font-semibold text-slate-600 dark:text-slate-300 hover:text-purple-700 dark:hover:text-purple-300 bg-slate-100 dark:bg-slate-800 hover:bg-purple-50 dark:hover:bg-purple-950/40 rounded-xl transition-colors cursor-pointer"
                title="Reset filters"
              >
                Clear
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Messages Table */}
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs border-collapse">
          <thead>
            <tr className="bg-slate-50/80 dark:bg-slate-950/80 border-b border-slate-200/80 dark:border-slate-800/80 text-slate-500 dark:text-slate-400 font-semibold uppercase tracking-wider">
              <th className="py-3.5 px-4">WAMID</th>
              <th className="py-3.5 px-4">Recipient</th>
              <th className="py-3.5 px-4">Endpoint</th>
              <th className="py-3.5 px-4">Status</th>
              <th className="py-3.5 px-4">Broadcast / Template</th>
              <th className="py-3.5 px-4">Last Status Time</th>
              <th className="py-3.5 px-4 text-right">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-800/60">
            {loading ? (
              // Loading Skeleton
              [...Array(5)].map((_, i) => (
                <tr key={i} className="animate-pulse">
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 dark:bg-slate-800 rounded w-32" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 dark:bg-slate-800 rounded w-24" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 dark:bg-slate-800 rounded w-20" /></td>
                  <td className="py-3.5 px-4"><div className="h-5 bg-slate-100 dark:bg-slate-800 rounded-full w-16" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 dark:bg-slate-800 rounded w-24" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 dark:bg-slate-800 rounded w-20" /></td>
                  <td className="py-3.5 px-4 text-right"><div className="h-7 bg-slate-100 dark:bg-slate-800 rounded w-16 ml-auto" /></td>
                </tr>
              ))
            ) : !data || data.items.length === 0 ? (
              <tr>
                <td colSpan={7}>
                  <EmptyState onResetFilters={handleResetFilters} />
                </td>
              </tr>
            ) : (
              data.items.map((item) => (
                <tr
                  key={item.id}
                  className="hover:bg-purple-50/40 dark:hover:bg-slate-800/50 transition-colors group cursor-pointer"
                  onClick={() => onSelectMessage(item)}
                >
                  {/* WAMID */}
                  <td className="py-3.5 px-4">
                    <div className="font-mono text-slate-800 dark:text-slate-200 font-medium truncate max-w-[180px] group-hover:text-purple-950 dark:group-hover:text-purple-300" title={item.wamid}>
                      {item.wamid}
                    </div>
                  </td>

                  {/* Recipient */}
                  <td className="py-3.5 px-4">
                    <div className="font-mono font-medium text-slate-700 dark:text-slate-300">
                      {item.recipientPhone || "—"}
                    </div>
                  </td>

                  {/* Endpoint */}
                  <td className="py-3.5 px-4">
                    <span className="text-slate-600 dark:text-slate-400 font-medium truncate max-w-[130px] block">
                      {item.endpointName}
                    </span>
                  </td>

                  {/* Status */}
                  <td className="py-3.5 px-4">
                    <div className="flex flex-col gap-1 items-start">
                      <StatusBadge status={item.currentStatus} size="sm" />
                      {item.activeErrorCode && (
                        <span className="text-[10px] text-rose-600 dark:text-rose-400 font-mono" title={item.activeErrorTitle || undefined}>
                          Error: {item.activeErrorCode}
                        </span>
                      )}
                    </div>
                  </td>

                  {/* Broadcast / Template */}
                  <td className="py-3.5 px-4">
                    {item.templateName || item.broadcastName ? (
                      <span className="inline-flex items-center px-2 py-0.5 rounded-md text-[11px] font-medium bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border border-purple-100 dark:border-purple-800/60 max-w-[150px] truncate" title={item.templateName || item.broadcastName || ""}>
                        {item.templateName || item.broadcastName}
                      </span>
                    ) : (
                      <span className="text-slate-400 dark:text-slate-500">—</span>
                    )}
                  </td>

                  {/* Last Status Time */}
                  <td className="py-3.5 px-4 text-slate-500 dark:text-slate-400 font-mono text-[11px]">
                    {formatDate(item.lastStatusTimestamp || item.createdAt)}
                  </td>

                  {/* Action */}
                  <td className="py-3.5 px-4 text-right" onClick={(e) => e.stopPropagation()}>
                    <button
                      onClick={() => onSelectMessage(item)}
                      className="px-2.5 py-1 text-xs font-semibold rounded-lg text-purple-700 dark:text-purple-300 bg-purple-50 dark:bg-purple-950/50 hover:bg-purple-100 dark:hover:bg-purple-900/50 border border-purple-200 dark:border-purple-800/60 transition-colors shadow-2xs cursor-pointer"
                    >
                      History
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="p-4 bg-slate-50/70 dark:bg-slate-950/60 border-t border-slate-200/80 dark:border-slate-800/80 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs text-slate-600 dark:text-slate-400">
        <div className="flex items-center gap-2">
          <span>
            Showing <span className="font-semibold text-slate-800 dark:text-slate-200 font-mono">{startRecord}</span> to{" "}
            <span className="font-semibold text-slate-800 dark:text-slate-200 font-mono">{endRecord}</span> of{" "}
            <span className="font-semibold text-slate-800 dark:text-slate-200 font-mono">{totalCount.toLocaleString()}</span> messages
          </span>

          <div className="flex items-center gap-1.5 ml-3">
            <span className="text-slate-400 dark:text-slate-500">Rows:</span>
            <select
              value={pageSize}
              onChange={(e) => onFilterChange({ pageSize: Number(e.target.value), page: 1 })}
              className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-lg px-2 py-1 text-xs text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-1 focus:ring-purple-500 cursor-pointer"
            >
              <option value={10}>10</option>
              <option value={20}>20</option>
              <option value={50}>50</option>
            </select>
          </div>
        </div>

        {/* Page Nav */}
        <div className="flex items-center gap-1.5">
          <button
            onClick={() => onFilterChange({ page: Math.max(1, currentPage - 1) })}
            disabled={currentPage <= 1 || loading}
            className="px-3 py-1 rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer font-medium"
          >
            Previous
          </button>

          <span className="px-2 font-medium text-slate-700 dark:text-slate-300">
            Page {currentPage} of {totalPages}
          </span>

          <button
            onClick={() => onFilterChange({ page: Math.min(totalPages, currentPage + 1) })}
            disabled={currentPage >= totalPages || loading}
            className="px-3 py-1 rounded-lg bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer font-medium"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
