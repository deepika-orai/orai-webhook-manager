"use client";

import React, { useCallback, useEffect, useState } from "react";
import { MessageListItem, MessageStatusEvent } from "../types/dashboard";
import { getMessageEvents } from "../lib/api";
import { StatusBadge } from "./StatusBadge";

interface MessageDetailModalProps {
  message: MessageListItem | null;
  onClose: () => void;
  customTenantHeader?: string;
}

export function MessageDetailModal({ message, onClose, customTenantHeader }: MessageDetailModalProps) {
  const [events, setEvents] = useState<MessageStatusEvent[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  const fetchEvents = useCallback(async () => {
    if (!message) return;
    setLoading(true);
    setError(null);
    try {
      const data = await getMessageEvents(message.id, customTenantHeader);
      setEvents(data);
    } catch (err: unknown) {
      let errMsg = "Failed to load events";
      if (err && typeof err === "object") {
        const errObj = err as { status?: number; message?: string };
        if (errObj.status === 401) {
          errMsg = "Session expired or unauthenticated. Please log in again.";
        } else if (errObj.status === 403) {
          errMsg = "Access denied. You do not have permission to view message events for this tenant.";
        } else if (errObj.status === 404) {
          errMsg = "Message not found or does not belong to your tenant.";
        } else if (errObj.message) {
          errMsg = errObj.message;
        }
      }
      setError(errMsg);
    } finally {
      setLoading(false);
    }
  }, [message, customTenantHeader]);

  useEffect(() => {
    if (!message) return;
    let ignore = false;
    async function load() {
      if (ignore) return;
      await fetchEvents();
    }
    load();
    return () => {
      ignore = true;
    };
  }, [message, fetchEvents]);

  if (!message) return null;

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopied(label);
    setTimeout(() => setCopied(null), 2000);
  };

  const formatDate = (dateStr?: string | null) => {
    if (!dateStr) return "N/A";
    try {
      const d = new Date(dateStr);
      return d.toLocaleString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      });
    } catch {
      return dateStr;
    }
  };

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto bg-slate-900/40 backdrop-blur-xs flex items-center justify-center p-4 sm:p-6 animate-in fade-in duration-200">
      <div
        className="relative w-full max-w-4xl bg-white rounded-2xl shadow-2xl border border-slate-200 overflow-hidden flex flex-col max-h-[90vh]"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="px-6 py-4 bg-slate-50 border-b border-slate-200 flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-blue-100/80 text-blue-700 flex items-center justify-center font-bold">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z" />
              </svg>
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-bold text-slate-900">WhatsApp Message Details</h2>
                <StatusBadge status={message.currentStatus} />
              </div>
              <p className="text-xs font-mono text-slate-500 truncate max-w-md" title={message.wamid}>
                {message.wamid}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => copyToClipboard(message.wamid, "wamid")}
              className="px-2.5 py-1.5 rounded-lg text-xs font-medium text-slate-700 bg-white hover:bg-slate-100 border border-slate-200 transition-colors flex items-center gap-1.5 cursor-pointer"
            >
              {copied === "wamid" ? (
                <>
                  <span className="text-emerald-600 font-semibold">✓ Copied</span>
                </>
              ) : (
                <>
                  <svg className="w-3.5 h-3.5 text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                  </svg>
                  Copy WAMID
                </>
              )}
            </button>
            <button
              onClick={onClose}
              className="p-1.5 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors cursor-pointer"
              title="Close modal"
            >
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>

        {/* Modal Body */}
        <div className="p-6 overflow-y-auto space-y-6">
          {/* Metadata Grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
            <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
              <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider block">Recipient</span>
              <span className="text-sm font-semibold text-slate-800 font-mono">
                {message.recipientPhone || "N/A"}
              </span>
            </div>

            <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
              <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider block">Sender Display</span>
              <span className="text-sm font-semibold text-slate-800 font-mono">
                {message.displayPhoneNumber || message.phoneNumberId || "N/A"}
              </span>
            </div>

            <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
              <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider block">Endpoint Line</span>
              <span className="text-sm font-semibold text-slate-800 truncate block">
                {message.endpointName || "Default Line"}
              </span>
            </div>

            <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
              <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider block">Conversation</span>
              <span className="text-xs font-medium text-slate-700 truncate block font-mono">
                {message.conversationId || "N/A"}
              </span>
              {message.conversationOriginType && (
                <span className="text-[10px] text-slate-500 block mt-0.5">
                  Origin: {message.conversationOriginType}
                </span>
              )}
            </div>

            <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
              <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider block">Pricing Category</span>
              <span className="text-xs font-semibold text-slate-700 capitalize">
                {message.pricingCategory || "Standard"} {message.pricingModel ? `(${message.pricingModel})` : ""}
              </span>
              <span className="text-[10px] text-slate-500 block mt-0.5">
                Billable: {message.pricingBillable ? "Yes" : "No"}
              </span>
            </div>

            <div className="p-3 bg-slate-50 rounded-xl border border-slate-100">
              <span className="text-[11px] font-medium text-slate-400 uppercase tracking-wider block">Template / Campaign</span>
              <span className="text-xs font-semibold text-slate-700 truncate block">
                {message.templateName || message.broadcastName || "Direct / None"}
              </span>
              {message.broadcastId && (
                <span className="text-[10px] text-slate-400 block font-mono">
                  ID: {message.broadcastId}
                </span>
              )}
            </div>
          </div>

          {/* Failure Diagnostics / Historical Failure Event */}
          {Boolean(message.activeErrorCode || message.activeErrorMessage || message.lastFailureCode || message.lastFailureReason) && (
            (() => {
              const isFailed = message.currentStatus?.toLowerCase() === "failed";
              const currentStatusLabel = message.currentStatus
                ? message.currentStatus.charAt(0).toUpperCase() + message.currentStatus.slice(1).toLowerCase()
                : "Confirmed";

              if (isFailed) {
                return (
                  <div className="p-4 rounded-xl border border-rose-200 bg-rose-50/70 space-y-2">
                    <div className="flex items-center gap-2 text-rose-800 font-semibold text-xs">
                      <svg className="w-4 h-4 text-rose-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                      <span>Failure Diagnostics ({message.activeErrorCode || message.lastFailureCode || "Error"})</span>
                    </div>
                    <p className="text-xs font-semibold text-rose-900">
                      {message.activeErrorTitle || message.lastFailureReason || "WhatsApp Delivery Failure"}
                    </p>
                    {message.activeErrorMessage && (
                      <p className="text-xs text-rose-700 font-mono bg-white/80 p-2.5 rounded border border-rose-200/80">
                        {message.activeErrorMessage}
                      </p>
                    )}
                    {message.activeErrorDetails && (
                      <p className="text-[11px] text-rose-600">
                        {message.activeErrorDetails}
                      </p>
                    )}
                  </div>
                );
              }

              return (
                <div className="p-4 rounded-xl border border-amber-200 bg-amber-50/80 space-y-2">
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2 text-amber-900 font-semibold text-xs">
                      <svg className="w-4 h-4 text-amber-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                      </svg>
                      <span>Historical Failure Event ({message.lastFailureCode || message.activeErrorCode || "Audit"})</span>
                    </div>
                    <span className="text-[10px] font-medium px-2 py-0.5 rounded-full bg-amber-100 text-amber-800 border border-amber-200">
                      Audit History
                    </span>
                  </div>
                  <p className="text-xs text-amber-950">
                    Recorded in audit history; current status remains <span className="font-semibold text-amber-900">{currentStatusLabel}</span>.
                  </p>
                  {(message.lastFailureReason || message.activeErrorTitle) && (
                    <p className="text-xs text-amber-900 font-medium">
                      Event Detail: {message.lastFailureReason || message.activeErrorTitle}
                    </p>
                  )}
                  {message.activeErrorMessage && (
                    <p className="text-xs text-slate-700 font-mono bg-white/90 p-2.5 rounded border border-amber-200/60">
                      {message.activeErrorMessage}
                    </p>
                  )}
                  {message.activeErrorDetails && (
                    <p className="text-[11px] text-amber-800">
                      {message.activeErrorDetails}
                    </p>
                  )}
                  {message.lastFailureTimestamp && (
                    <p className="text-[10px] text-amber-700 font-mono">
                      Timestamp: {formatDate(message.lastFailureTimestamp)}
                    </p>
                  )}
                </div>
              );
            })()
          )}

          {/* Chronological Event History */}
          <div>
            <div className="flex items-center justify-between mb-3">
              <h3 className="text-sm font-bold text-slate-900 flex items-center gap-2">
                <svg className="w-4 h-4 text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                Immutable Status History
              </h3>
              <span className="text-xs text-slate-400">Chronological Event Timeline</span>
            </div>

            {loading ? (
              <div className="py-8 text-center text-xs text-slate-500 flex items-center justify-center gap-2">
                <div className="w-4 h-4 border-2 border-blue-600 border-t-transparent rounded-full animate-spin" />
                Loading status event history...
              </div>
            ) : error ? (
              <div className="p-3.5 bg-rose-50 text-rose-700 rounded-xl text-xs border border-rose-200 flex items-center justify-between gap-3">
                <div className="flex items-center gap-2">
                  <svg className="w-4 h-4 text-rose-500 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  <span>{error}</span>
                </div>
                <button
                  onClick={fetchEvents}
                  className="px-2.5 py-1 text-xs font-semibold rounded-md bg-rose-100 text-rose-800 hover:bg-rose-200 transition-colors shrink-0 cursor-pointer"
                >
                  Retry
                </button>
              </div>
            ) : events.length === 0 ? (
              <div className="py-6 text-center text-slate-400 text-xs bg-slate-50 rounded-xl border border-dashed border-slate-200">
                No recorded status events for this message.
              </div>
            ) : (
              <div className="relative pl-6 space-y-6 before:absolute before:left-2.5 before:top-2 before:bottom-2 before:w-0.5 before:bg-slate-200">
                {events.map((evt) => (
                  <div key={evt.id} className="relative group">
                    {/* Dot */}
                    <div className="absolute -left-6 top-1 w-3.5 h-3.5 rounded-full border-2 border-white bg-blue-600 shadow-xs" />

                    <div className="bg-slate-50 p-3.5 rounded-xl border border-slate-200/80 hover:bg-white hover:border-slate-300 transition-all">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="flex items-center gap-2">
                          <StatusBadge status={evt.status} size="sm" />
                          {evt.errorCode && (
                            <span className="text-[11px] font-mono font-medium px-2 py-0.5 rounded bg-rose-100 text-rose-800 border border-rose-200">
                              Code: {evt.errorCode}
                            </span>
                          )}
                        </div>
                        <div className="text-xs text-slate-500 font-mono">
                          {formatDate(evt.statusTimestamp)}
                        </div>
                      </div>

                      {evt.errorTitle && (
                        <p className="mt-2 text-xs font-semibold text-slate-800">
                          {evt.errorTitle}
                        </p>
                      )}

                      {evt.errorMessage && (
                        <p className="mt-1 text-xs text-slate-600 font-mono bg-white p-2 rounded border border-slate-200">
                          {evt.errorMessage}
                        </p>
                      )}

                      {evt.errorDetails && (
                        <p className="mt-1 text-[11px] text-slate-500">
                          {evt.errorDetails}
                        </p>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="px-6 py-3.5 bg-slate-50 border-t border-slate-200 flex justify-between items-center text-xs text-slate-500">
          <span>First Ingested: {formatDate(message.createdAt)}</span>
          <button
            onClick={onClose}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-slate-700 bg-white hover:bg-slate-100 border border-slate-300 transition-colors shadow-2xs cursor-pointer"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
