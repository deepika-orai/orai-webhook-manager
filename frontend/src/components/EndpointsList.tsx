import React from "react";
import { WebhookEndpoint } from "../types/dashboard";
import { StatusBadge } from "./StatusBadge";

interface EndpointsListProps {
  endpoints: WebhookEndpoint[];
  loading?: boolean;
}

export function EndpointsList({ endpoints, loading }: EndpointsListProps) {
  const formatTimestamp = (dateStr?: string | null) => {
    if (!dateStr) return "Never";
    try {
      const d = new Date(dateStr);
      if (isNaN(d.getTime())) return "Never";
      return d.toLocaleString(undefined, {
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
    <div className="bg-white dark:bg-slate-900/80 rounded-2xl border border-slate-200/80 dark:border-slate-800/90 p-5 sm:p-6 shadow-xs flex flex-col justify-between h-full">
      <div>
        <div className="flex items-center justify-between mb-4">
          <div>
            <h2 className="text-base font-bold text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-cyan-500" />
              Registered Webhook Endpoints
            </h2>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Active ingestion lines connected to this tenant</p>
          </div>
          <span className="text-xs font-semibold px-2.5 py-1 rounded-full bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border border-purple-100 dark:border-purple-800/60">
            {endpoints.length} {endpoints.length === 1 ? "Endpoint" : "Endpoints"}
          </span>
        </div>

        {loading ? (
          <div className="space-y-3 py-2">
            {[1, 2].map((i) => (
              <div key={i} className="h-16 bg-slate-50 dark:bg-slate-800/50 rounded-xl animate-pulse border border-slate-100 dark:border-slate-800" />
            ))}
          </div>
        ) : endpoints.length === 0 ? (
          <div className="py-8 text-center text-slate-500 dark:text-slate-400 text-xs bg-slate-50/60 dark:bg-slate-950/40 rounded-xl border border-dashed border-slate-200 dark:border-slate-800">
            No webhook endpoints provisioned for this tenant.
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-3">
            {endpoints.map((ep) => (
              <div
                key={ep.id}
                className="p-3.5 rounded-xl border border-slate-200/80 dark:border-slate-800/90 bg-slate-50/40 dark:bg-slate-950/40 hover:bg-white dark:hover:bg-slate-800/60 hover:border-purple-200 dark:hover:border-purple-500/40 hover:shadow-xs transition-all flex flex-col justify-between gap-2.5"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0 flex-1">
                    <h3 className="text-sm font-semibold text-slate-900 dark:text-white truncate" title={ep.name}>
                      {ep.name}
                    </h3>
                    <div className="flex items-center gap-1.5 mt-1 font-mono text-[11px] text-slate-500 dark:text-slate-400">
                      <span className="text-slate-400 dark:text-slate-500">Prefix:</span>
                      <span className="bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border border-purple-100 dark:border-purple-800/60 px-1.5 py-0.5 rounded text-[10px] font-semibold">
                        {ep.keyPrefix}***
                      </span>
                    </div>
                  </div>
                  <StatusBadge status={ep.status} size="sm" />
                </div>

                <div className="pt-2 border-t border-slate-200/60 dark:border-slate-800/60 flex items-center justify-between gap-2 text-[11px] text-slate-500 dark:text-slate-400">
                  <span className="shrink-0 text-slate-500 dark:text-slate-400">Last received:</span>
                  <span className="font-medium text-slate-700 dark:text-slate-300 font-mono whitespace-nowrap">
                    {formatTimestamp(ep.lastReceivedAt)}
                  </span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
