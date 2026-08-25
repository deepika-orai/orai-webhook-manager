import React from "react";
import { DashboardSummary } from "../types/dashboard";

interface StatusDistributionProps {
  summary: DashboardSummary;
}

export function StatusDistribution({ summary }: StatusDistributionProps) {
  const total = summary.totalMessages || 0;

  const sentPct = total > 0 ? (summary.sent / total) * 100 : 0;
  const deliveredPct = total > 0 ? (summary.delivered / total) * 100 : 0;
  const readPct = total > 0 ? (summary.read / total) * 100 : 0;
  const failedPct = total > 0 ? (summary.failed / total) * 100 : 0;

  return (
    <div className="bg-white rounded-xl border border-slate-200/80 p-5 shadow-xs">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 mb-4">
        <div>
          <h2 className="text-base font-semibold text-slate-900">Message Status Distribution</h2>
          <p className="text-xs text-slate-500">Real-time status progression breakdown across active messages</p>
        </div>
        <div className="text-xs text-slate-500 font-medium">
          Total Logged: <span className="font-semibold text-slate-800">{total.toLocaleString()}</span>
        </div>
      </div>

      {/* Distribution Progress Bar */}
      <div className="h-4 w-full bg-slate-100 rounded-full overflow-hidden flex gap-0.5 p-0.5 border border-slate-200/60 shadow-inner">
        {total === 0 ? (
          <div className="w-full h-full bg-slate-200 rounded-full flex items-center justify-center text-[10px] text-slate-500 font-medium">
            No message events recorded yet
          </div>
        ) : (
          <>
            {sentPct > 0 && (
              <div
                style={{ width: `${sentPct}%` }}
                className="bg-blue-500 hover:bg-blue-600 transition-all rounded-sm relative group cursor-pointer"
                title={`Sent: ${summary.sent.toLocaleString()} (${sentPct.toFixed(1)}%)`}
              />
            )}
            {deliveredPct > 0 && (
              <div
                style={{ width: `${deliveredPct}%` }}
                className="bg-emerald-500 hover:bg-emerald-600 transition-all rounded-sm relative group cursor-pointer"
                title={`Delivered: ${summary.delivered.toLocaleString()} (${deliveredPct.toFixed(1)}%)`}
              />
            )}
            {readPct > 0 && (
              <div
                style={{ width: `${readPct}%` }}
                className="bg-sky-400 hover:bg-sky-500 transition-all rounded-sm relative group cursor-pointer"
                title={`Read: ${summary.read.toLocaleString()} (${readPct.toFixed(1)}%)`}
              />
            )}
            {failedPct > 0 && (
              <div
                style={{ width: `${failedPct}%` }}
                className="bg-rose-500 hover:bg-rose-600 transition-all rounded-sm relative group cursor-pointer"
                title={`Failed: ${summary.failed.toLocaleString()} (${failedPct.toFixed(1)}%)`}
              />
            )}
          </>
        )}
      </div>

      {/* Legend & Stats Grid */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mt-4 pt-3 border-t border-slate-100">
        <div className="flex items-center gap-2.5 p-2 rounded-lg bg-slate-50/70 border border-slate-100">
          <span className="w-3 h-3 rounded-full bg-blue-500 shrink-0" />
          <div>
            <div className="text-xs text-slate-500 font-medium">Sent</div>
            <div className="text-sm font-bold text-slate-800">
              {summary.sent.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 font-normal">({sentPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2.5 p-2 rounded-lg bg-slate-50/70 border border-slate-100">
          <span className="w-3 h-3 rounded-full bg-emerald-500 shrink-0" />
          <div>
            <div className="text-xs text-slate-500 font-medium">Delivered</div>
            <div className="text-sm font-bold text-slate-800">
              {summary.delivered.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 font-normal">({deliveredPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2.5 p-2 rounded-lg bg-slate-50/70 border border-slate-100">
          <span className="w-3 h-3 rounded-full bg-sky-400 shrink-0" />
          <div>
            <div className="text-xs text-slate-500 font-medium">Read</div>
            <div className="text-sm font-bold text-slate-800">
              {summary.read.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 font-normal">({readPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2.5 p-2 rounded-lg bg-slate-50/70 border border-slate-100">
          <span className="w-3 h-3 rounded-full bg-rose-500 shrink-0" />
          <div>
            <div className="text-xs text-slate-500 font-medium">Failed</div>
            <div className="text-sm font-bold text-slate-800">
              {summary.failed.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 font-normal">({failedPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
