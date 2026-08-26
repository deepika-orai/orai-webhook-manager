import React from "react";

interface MetricCardProps {
  title: string;
  value: number | string;
  rate?: number | null;
  rateLabel?: string;
  subtitle?: string;
  iconType: "total" | "sent" | "delivered" | "read" | "failed" | "inbox" | "deadletter";
}

export function MetricCard({
  title,
  value,
  rate,
  rateLabel = "rate",
  subtitle,
  iconType,
}: MetricCardProps) {
  const getIcon = () => {
    switch (iconType) {
      case "total":
        return (
          <svg className="w-5 h-5 text-purple-600 dark:text-purple-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
          </svg>
        );
      case "sent":
        return (
          <svg className="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
          </svg>
        );
      case "delivered":
        return (
          <svg className="w-5 h-5 text-emerald-600 dark:text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
          </svg>
        );
      case "read":
        return (
          <svg className="w-5 h-5 text-cyan-600 dark:text-cyan-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        );
      case "failed":
        return (
          <svg className="w-5 h-5 text-rose-600 dark:text-rose-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        );
      case "inbox":
        return (
          <svg className="w-5 h-5 text-amber-600 dark:text-amber-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4" />
          </svg>
        );
      case "deadletter":
        return (
          <svg className="w-5 h-5 text-purple-600 dark:text-purple-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
          </svg>
        );
    }
  };

  const getBgIcon = () => {
    switch (iconType) {
      case "total": return "bg-purple-50 dark:bg-purple-950/50 border border-purple-100 dark:border-purple-800/50";
      case "sent": return "bg-blue-50 dark:bg-blue-950/50 border border-blue-100 dark:border-blue-800/50";
      case "delivered": return "bg-emerald-50 dark:bg-emerald-950/50 border border-emerald-100 dark:border-emerald-800/50";
      case "read": return "bg-cyan-50 dark:bg-cyan-950/50 border border-cyan-100 dark:border-cyan-800/50";
      case "failed": return "bg-rose-50 dark:bg-rose-950/50 border border-rose-100 dark:border-rose-800/50";
      case "inbox": return "bg-amber-50 dark:bg-amber-950/50 border border-amber-100 dark:border-amber-800/50";
      case "deadletter": return "bg-purple-50 dark:bg-purple-950/50 border border-purple-100 dark:border-purple-800/50";
    }
  };

  return (
    <div className="bg-white dark:bg-slate-900/80 rounded-2xl border border-slate-200/80 dark:border-slate-800/90 hover:border-purple-200/90 dark:hover:border-purple-500/40 p-4 sm:p-5 shadow-xs hover:shadow-md dark:hover:shadow-purple-950/20 transition-all duration-200 flex flex-col justify-between group">
      <div className="flex items-center justify-between gap-2">
        <p className="text-[11px] sm:text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 whitespace-nowrap min-w-0">
          {title}
        </p>
        <div className={`w-9 h-9 rounded-xl ${getBgIcon()} flex items-center justify-center shrink-0 transition-transform group-hover:scale-105`}>
          {getIcon()}
        </div>
      </div>

      <div className="mt-3 flex items-baseline justify-between gap-2 flex-wrap">
        <div className="text-2xl sm:text-3xl font-bold tracking-tight text-slate-900 dark:text-white">
          {typeof value === "number" ? value.toLocaleString() : value}
        </div>
        {rate !== undefined && rate !== null && (
          <span
            className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold ${
              iconType === "failed"
                ? rate > 5
                  ? "bg-rose-100 dark:bg-rose-950/50 text-rose-800 dark:text-rose-300 border border-rose-200 dark:border-rose-800/60"
                  : "bg-emerald-100 dark:bg-emerald-950/50 text-emerald-800 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800/60"
                : "bg-purple-100 dark:bg-purple-950/50 text-purple-800 dark:text-purple-300 border border-purple-200 dark:border-purple-800/60"
            }`}
          >
            {rate.toFixed(1)}% {rateLabel}
          </span>
        )}
      </div>

      {subtitle && (
        <p className="mt-1 text-xs text-slate-400 dark:text-slate-500 truncate">{subtitle}</p>
      )}
    </div>
  );
}
