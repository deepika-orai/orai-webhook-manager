import React from "react";
import { WebhookEndpoint } from "../types/dashboard";
import { StatusBadge } from "./StatusBadge";

interface EndpointsListProps {
  endpoints: WebhookEndpoint[];
  loading?: boolean;
}

export function EndpointsList({ endpoints, loading }: EndpointsListProps) {
  const formatTimestamp = (dateStr?: string | null) => {
    if (!dateStr) return "Never received";
    try {
      const d = new Date(dateStr);
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
    <div className="bg-white rounded-xl border border-slate-200/80 p-5 shadow-xs">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-base font-semibold text-slate-900">Registered Webhook Endpoints</h2>
          <p className="text-xs text-slate-500">Active ingestion lines connected to this tenant</p>
        </div>
        <span className="text-xs font-semibold px-2.5 py-1 rounded-full bg-slate-100 text-slate-700 border border-slate-200">
          {endpoints.length} {endpoints.length === 1 ? "Endpoint" : "Endpoints"}
        </span>
      </div>

      {loading ? (
        <div className="space-y-3 py-2">
          {[1, 2].map((i) => (
            <div key={i} className="h-14 bg-slate-50 rounded-lg animate-pulse border border-slate-100" />
          ))}
        </div>
      ) : endpoints.length === 0 ? (
        <div className="py-6 text-center text-slate-500 text-xs bg-slate-50/50 rounded-lg border border-dashed border-slate-200">
          No webhook endpoints provisioned for this tenant.
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
          {endpoints.map((ep) => (
            <div
              key={ep.id}
              className="p-3.5 rounded-lg border border-slate-200/90 bg-slate-50/40 hover:bg-white hover:border-slate-300 transition-all flex flex-col justify-between gap-3 shadow-2xs"
            >
              <div className="flex items-start justify-between gap-2">
                <div>
                  <h3 className="text-sm font-semibold text-slate-900 truncate" title={ep.name}>
                    {ep.name}
                  </h3>
                  <div className="flex items-center gap-1.5 mt-1 font-mono text-[11px] text-slate-500">
                    <span className="text-slate-400">Prefix:</span>
                    <span className="bg-slate-200/70 text-slate-700 px-1.5 py-0.5 rounded text-[10px] font-medium">
                      {ep.keyPrefix}***
                    </span>
                  </div>
                </div>
                <StatusBadge status={ep.status} size="sm" />
              </div>

              <div className="pt-2 border-t border-slate-200/60 flex items-center justify-between text-[11px] text-slate-500">
                <span>Last payload:</span>
                <span className="font-medium text-slate-700">{formatTimestamp(ep.lastReceivedAt)}</span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
