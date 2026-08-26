import React from "react";

interface StatusBadgeProps {
  status?: string | null;
  size?: "sm" | "md";
}

export function StatusBadge({ status, size = "md" }: StatusBadgeProps) {
  const normStatus = (status || "unknown").toLowerCase().trim();

  let styles = "bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border-slate-200 dark:border-slate-700";
  let dotStyle = "bg-slate-400";
  let label = status || "Unknown";

  switch (normStatus) {
    case "sent":
      styles = "bg-blue-50 dark:bg-blue-950/50 text-blue-700 dark:text-blue-300 border-blue-200/80 dark:border-blue-800/60";
      dotStyle = "bg-blue-500 animate-pulse-subtle";
      label = "Sent";
      break;
    case "delivered":
      styles = "bg-emerald-50 dark:bg-emerald-950/50 text-emerald-700 dark:text-emerald-300 border-emerald-200/80 dark:border-emerald-800/60";
      dotStyle = "bg-emerald-500";
      label = "Delivered";
      break;
    case "read":
      styles = "bg-cyan-50 dark:bg-cyan-950/50 text-cyan-700 dark:text-cyan-300 border-cyan-200/80 dark:border-cyan-800/60";
      dotStyle = "bg-cyan-500";
      label = "Read";
      break;
    case "failed":
      styles = "bg-rose-50 dark:bg-rose-950/50 text-rose-700 dark:text-rose-300 border-rose-200/80 dark:border-rose-800/60";
      dotStyle = "bg-rose-500";
      label = "Failed";
      break;
    case "active":
      styles = "bg-emerald-50 dark:bg-emerald-950/50 text-emerald-700 dark:text-emerald-300 border-emerald-200/80 dark:border-emerald-800/60";
      dotStyle = "bg-emerald-500 animate-pulse-subtle";
      label = "Active";
      break;
    case "suspended":
      styles = "bg-amber-50 dark:bg-amber-950/50 text-amber-700 dark:text-amber-300 border-amber-200/80 dark:border-amber-800/60";
      dotStyle = "bg-amber-500";
      label = "Suspended";
      break;
    case "revoked":
      styles = "bg-rose-50 dark:bg-rose-950/50 text-rose-700 dark:text-rose-300 border-rose-200/80 dark:border-rose-800/60";
      dotStyle = "bg-rose-500";
      label = "Revoked";
      break;
    case "pending":
      styles = "bg-amber-50 dark:bg-amber-950/50 text-amber-700 dark:text-amber-300 border-amber-200/80 dark:border-amber-800/60";
      dotStyle = "bg-amber-500 animate-pulse-subtle";
      label = "Pending";
      break;
    case "processing":
      styles = "bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border-purple-200/80 dark:border-purple-800/60";
      dotStyle = "bg-purple-500 animate-spin";
      label = "Processing";
      break;
    case "deadletter":
      styles = "bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border-purple-200/80 dark:border-purple-800/60";
      dotStyle = "bg-purple-500";
      label = "Dead Letter";
      break;
  }

  const sizeClasses = size === "sm" ? "px-2 py-0.5 text-xs" : "px-2.5 py-1 text-xs";

  return (
    <span
      className={`inline-flex items-center gap-1.5 font-medium rounded-full border shadow-2xs transition-colors ${styles} ${sizeClasses}`}
    >
      <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${dotStyle}`} />
      {label}
    </span>
  );
}
