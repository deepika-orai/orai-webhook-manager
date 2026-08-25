"use client";

import React, { useState } from "react";
import { MessageFilterState, MessageListItem, PagedResult } from "../types/dashboard";
import { StatusBadge } from "./StatusBadge";
import { EmptyState } from "./EmptyAndErrorStates";

interface MessagesTableProps {
  data: PagedResult<MessageListItem> | null;
  filters: MessageFilterState;
  onFilterChange: (newFilters: Partial<MessageFilterState>) => void;
  onSelectMessage: (message: MessageListItem) => void;
  loading: boolean;
  onRefresh: () => void;
}

export function MessagesTable({
  data,
  filters,
  onFilterChange,
  onSelectMessage,
  loading,
  onRefresh,
}: MessagesTableProps) {
  const [searchInput, setSearchInput] = useState(filters.search);

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
    <div className="bg-white rounded-xl border border-slate-200/80 shadow-xs overflow-hidden">
      {/* Table Header & Controls Bar */}
      <div className="p-4 sm:p-5 border-b border-slate-200/80 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-slate-900">WhatsApp Messages</h2>
            <p className="text-xs text-slate-500">
              Real-time message state transitions & immutable delivery audit logs
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={onRefresh}
              disabled={loading}
              className="p-2 rounded-lg text-slate-600 hover:bg-slate-100 border border-slate-200 transition-colors disabled:opacity-50 cursor-pointer"
              title="Refresh messages table"
            >
              <svg
                className={`w-4 h-4 ${loading ? "animate-spin text-blue-600" : ""}`}
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

        {/* Filter Controls Row */}
        <div className="flex flex-col md:flex-row items-stretch md:items-center justify-between gap-3 pt-2">
          {/* Status Tabs */}
          <div className="flex items-center gap-1 overflow-x-auto pb-1 sm:pb-0 scrollbar-none">
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
                  className={`px-3 py-1.5 rounded-lg text-xs font-medium whitespace-nowrap transition-all cursor-pointer ${
                    active
                      ? "bg-slate-900 text-white shadow-2xs"
                      : "bg-slate-50 text-slate-600 hover:bg-slate-100 hover:text-slate-900 border border-slate-200/60"
                  }`}
                >
                  {tab.label}
                </button>
              );
            })}
          </div>

          {/* Search Form & Date Range */}
          <div className="flex flex-wrap items-center gap-2">
            <form onSubmit={handleSearchSubmit} className="relative flex-1 sm:w-64">
              <input
                type="text"
                placeholder="Search WAMID or Phone..."
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                className="w-full pl-8 pr-3 py-1.5 text-xs bg-slate-50 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-colors"
              />
              <svg
                className="w-4 h-4 text-slate-400 absolute left-2.5 top-2 pointer-events-none"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
            </form>

            {(filters.search || filters.status !== "ALL" || filters.dateFrom || filters.dateTo) && (
              <button
                onClick={handleResetFilters}
                className="px-2.5 py-1.5 text-xs font-medium text-slate-500 hover:text-slate-800 bg-slate-100 hover:bg-slate-200 rounded-lg transition-colors cursor-pointer"
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
            <tr className="bg-slate-50/80 border-b border-slate-200/80 text-slate-500 font-semibold uppercase tracking-wider">
              <th className="py-3 px-4">WAMID</th>
              <th className="py-3 px-4">Recipient</th>
              <th className="py-3 px-4">Endpoint</th>
              <th className="py-3 px-4">Status</th>
              <th className="py-3 px-4">Broadcast / Template</th>
              <th className="py-3 px-4">Last Status Time</th>
              <th className="py-3 px-4 text-right">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {loading ? (
              // Loading Skeleton
              [...Array(5)].map((_, i) => (
                <tr key={i} className="animate-pulse">
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 rounded w-32" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 rounded w-24" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 rounded w-20" /></td>
                  <td className="py-3.5 px-4"><div className="h-5 bg-slate-100 rounded-full w-16" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 rounded w-24" /></td>
                  <td className="py-3.5 px-4"><div className="h-4 bg-slate-100 rounded w-20" /></td>
                  <td className="py-3.5 px-4 text-right"><div className="h-7 bg-slate-100 rounded w-16 ml-auto" /></td>
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
                  className="hover:bg-slate-50/70 transition-colors group cursor-pointer"
                  onClick={() => onSelectMessage(item)}
                >
                  {/* WAMID */}
                  <td className="py-3.5 px-4">
                    <div className="font-mono text-slate-800 font-medium truncate max-w-[180px]" title={item.wamid}>
                      {item.wamid}
                    </div>
                  </td>

                  {/* Recipient */}
                  <td className="py-3.5 px-4">
                    <div className="font-mono font-medium text-slate-700">
                      {item.recipientPhone || "—"}
                    </div>
                  </td>

                  {/* Endpoint */}
                  <td className="py-3.5 px-4">
                    <span className="text-slate-600 font-medium truncate max-w-[130px] block">
                      {item.endpointName}
                    </span>
                  </td>

                  {/* Status */}
                  <td className="py-3.5 px-4">
                    <div className="flex flex-col gap-1 items-start">
                      <StatusBadge status={item.currentStatus} size="sm" />
                      {item.activeErrorCode && (
                        <span className="text-[10px] text-rose-600 font-mono" title={item.activeErrorTitle || undefined}>
                          Error: {item.activeErrorCode}
                        </span>
                      )}
                    </div>
                  </td>

                  {/* Broadcast / Template */}
                  <td className="py-3.5 px-4">
                    {item.templateName || item.broadcastName ? (
                      <span className="inline-flex items-center px-2 py-0.5 rounded text-[11px] font-medium bg-slate-100 text-slate-700 max-w-[150px] truncate" title={item.templateName || item.broadcastName || ""}>
                        {item.templateName || item.broadcastName}
                      </span>
                    ) : (
                      <span className="text-slate-400">—</span>
                    )}
                  </td>

                  {/* Last Status Time */}
                  <td className="py-3.5 px-4 text-slate-500 font-mono text-[11px]">
                    {formatDate(item.lastStatusTimestamp || item.createdAt)}
                  </td>

                  {/* Action */}
                  <td className="py-3.5 px-4 text-right" onClick={(e) => e.stopPropagation()}>
                    <button
                      onClick={() => onSelectMessage(item)}
                      className="px-2.5 py-1 text-xs font-semibold rounded-md text-blue-700 bg-blue-50 hover:bg-blue-100 border border-blue-200 transition-colors shadow-2xs cursor-pointer"
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
      <div className="p-4 bg-slate-50/70 border-t border-slate-200/80 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs text-slate-600">
        <div className="flex items-center gap-2">
          <span>
            Showing <span className="font-semibold text-slate-800">{startRecord}</span> to{" "}
            <span className="font-semibold text-slate-800">{endRecord}</span> of{" "}
            <span className="font-semibold text-slate-800">{totalCount.toLocaleString()}</span> messages
          </span>

          <div className="flex items-center gap-1.5 ml-3">
            <span className="text-slate-400">Rows:</span>
            <select
              value={pageSize}
              onChange={(e) => onFilterChange({ pageSize: Number(e.target.value), page: 1 })}
              className="bg-white border border-slate-200 rounded px-1.5 py-0.5 text-xs text-slate-700 focus:outline-none focus:ring-1 focus:ring-blue-500 cursor-pointer"
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
            className="px-2.5 py-1 rounded bg-white border border-slate-200 text-slate-700 hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer"
          >
            Previous
          </button>

          <span className="px-2 font-medium text-slate-700">
            Page {currentPage} of {totalPages}
          </span>

          <button
            onClick={() => onFilterChange({ page: Math.min(totalPages, currentPage + 1) })}
            disabled={currentPage >= totalPages || loading}
            className="px-2.5 py-1 rounded bg-white border border-slate-200 text-slate-700 hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
