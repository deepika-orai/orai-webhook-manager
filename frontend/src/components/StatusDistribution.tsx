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
    <div className="bg-white dark:bg-slate-900/80 rounded-2xl border border-slate-200/80 dark:border-slate-800/90 p-5 sm:p-6 shadow-xs">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 mb-4">
        <div>
          <h2 className="text-base font-bold text-slate-900 dark:text-white tracking-tight flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-purple-500" />
            Message Status Distribution
          </h2>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Real-time status progression breakdown across active messages</p>
        </div>
        <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">
          Total Tracked: <span className="font-bold text-slate-900 dark:text-white font-mono">{total.toLocaleString()}</span>
        </div>
      </div>

      {/* Distribution Progress Bar */}
      <div className="h-4 w-full bg-slate-100/90 dark:bg-slate-950/80 rounded-full overflow-hidden flex gap-0.5 p-0.5 border border-slate-200/60 dark:border-slate-800/80 shadow-inner">
        {total === 0 ? (
          <div className="w-full h-full bg-slate-200/60 dark:bg-slate-800/60 rounded-full flex items-center justify-center text-[10px] text-slate-500 dark:text-slate-400 font-medium">
            No message events recorded yet
          </div>
        ) : (
          <>
            {sentPct > 0 && (
              <div
                style={{ width: `${sentPct}%` }}
                className="bg-gradient-to-r from-purple-500 to-indigo-500 hover:brightness-110 transition-all rounded-full relative group cursor-pointer"
                title={`Sent: ${summary.sent.toLocaleString()} (${sentPct.toFixed(1)}%)`}
              />
            )}
            {deliveredPct > 0 && (
              <div
                style={{ width: `${deliveredPct}%` }}
                className="bg-emerald-500 hover:bg-emerald-600 transition-all rounded-full relative group cursor-pointer"
                title={`Delivered: ${summary.delivered.toLocaleString()} (${deliveredPct.toFixed(1)}%)`}
              />
            )}
            {readPct > 0 && (
              <div
                style={{ width: `${readPct}%` }}
                className="bg-cyan-500 hover:bg-cyan-600 transition-all rounded-full relative group cursor-pointer"
                title={`Read: ${summary.read.toLocaleString()} (${readPct.toFixed(1)}%)`}
              />
            )}
            {failedPct > 0 && (
              <div
                style={{ width: `${failedPct}%` }}
                className="bg-rose-500 hover:bg-rose-600 transition-all rounded-full relative group cursor-pointer"
                title={`Failed: ${summary.failed.toLocaleString()} (${failedPct.toFixed(1)}%)`}
              />
            )}
          </>
        )}
      </div>

      {/* Legend & Stats Grid */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mt-4 pt-3 border-t border-slate-100 dark:border-slate-800/80">
        <div className="flex items-center gap-2.5 p-2.5 rounded-xl bg-purple-50/50 dark:bg-purple-950/30 border border-purple-100/70 dark:border-purple-800/50">
          <span className="w-3 h-3 rounded-full bg-purple-500 shrink-0 shadow-xs" />
          <div>
            <div className="text-xs text-purple-900 dark:text-purple-300 font-medium">Sent</div>
            <div className="text-sm font-bold text-slate-800 dark:text-slate-200 font-mono">
              {summary.sent.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 dark:text-slate-500 font-normal font-sans">({sentPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2.5 p-2.5 rounded-xl bg-emerald-50/50 dark:bg-emerald-950/30 border border-emerald-100/70 dark:border-emerald-800/50">
          <span className="w-3 h-3 rounded-full bg-emerald-500 shrink-0 shadow-xs" />
          <div>
            <div className="text-xs text-emerald-900 dark:text-emerald-300 font-medium">Delivered</div>
            <div className="text-sm font-bold text-slate-800 dark:text-slate-200 font-mono">
              {summary.delivered.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 dark:text-slate-500 font-normal font-sans">({deliveredPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2.5 p-2.5 rounded-xl bg-cyan-50/50 dark:bg-cyan-950/30 border border-cyan-100/70 dark:border-cyan-800/50">
          <span className="w-3 h-3 rounded-full bg-cyan-500 shrink-0 shadow-xs" />
          <div>
            <div className="text-xs text-cyan-900 dark:text-cyan-300 font-medium">Read</div>
            <div className="text-sm font-bold text-slate-800 dark:text-slate-200 font-mono">
              {summary.read.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 dark:text-slate-500 font-normal font-sans">({readPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2.5 p-2.5 rounded-xl bg-rose-50/50 dark:bg-rose-950/30 border border-rose-100/70 dark:border-rose-800/50">
          <span className="w-3 h-3 rounded-full bg-rose-500 shrink-0 shadow-xs" />
          <div>
            <div className="text-xs text-rose-900 dark:text-rose-300 font-medium">Failed</div>
            <div className="text-sm font-bold text-slate-800 dark:text-slate-200 font-mono">
              {summary.failed.toLocaleString()}{" "}
              <span className="text-xs text-slate-400 dark:text-slate-500 font-normal font-sans">({failedPct.toFixed(1)}%)</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
