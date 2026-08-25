import React from "react";

interface StatusBadgeProps {
  status?: string | null;
  size?: "sm" | "md";
}

export function StatusBadge({ status, size = "md" }: StatusBadgeProps) {
  const normStatus = (status || "unknown").toLowerCase().trim();

  let styles = "bg-slate-100 text-slate-700 border-slate-200";
  let dotStyle = "bg-slate-400";
  let label = status || "Unknown";

  switch (normStatus) {
    case "sent":
      styles = "bg-blue-50 text-blue-700 border-blue-200/80";
      dotStyle = "bg-blue-500 animate-pulse";
      label = "Sent";
      break;
    case "delivered":
      styles = "bg-emerald-50 text-emerald-700 border-emerald-200/80";
      dotStyle = "bg-emerald-500";
      label = "Delivered";
      break;
    case "read":
      styles = "bg-sky-50 text-sky-700 border-sky-200/80";
      dotStyle = "bg-sky-500";
      label = "Read";
      break;
    case "failed":
      styles = "bg-rose-50 text-rose-700 border-rose-200/80";
      dotStyle = "bg-rose-500";
      label = "Failed";
      break;
    case "active":
      styles = "bg-emerald-50 text-emerald-700 border-emerald-200/80";
      dotStyle = "bg-emerald-500";
      label = "Active";
      break;
    case "suspended":
      styles = "bg-amber-50 text-amber-700 border-amber-200/80";
      dotStyle = "bg-amber-500";
      label = "Suspended";
      break;
    case "revoked":
      styles = "bg-rose-50 text-rose-700 border-rose-200/80";
      dotStyle = "bg-rose-500";
      label = "Revoked";
      break;
    case "pending":
      styles = "bg-amber-50 text-amber-700 border-amber-200/80";
      dotStyle = "bg-amber-500";
      label = "Pending";
      break;
    case "processing":
      styles = "bg-indigo-50 text-indigo-700 border-indigo-200/80";
      dotStyle = "bg-indigo-500 animate-spin";
      label = "Processing";
      break;
    case "deadletter":
      styles = "bg-purple-50 text-purple-700 border-purple-200/80";
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
