"use client";

import React from "react";

interface HeaderProps {
  onRefreshAll: () => void;
  loading: boolean;
  autoRefresh: boolean;
  onToggleAutoRefresh: () => void;
  lastUpdated: Date | null;
  tenantName?: string;
  userEmail?: string;
  isPlatformAdmin?: boolean;
  inspectionMode?: boolean;
  onLogout?: () => void;
}

export function Header({
  onRefreshAll,
  loading,
  autoRefresh,
  onToggleAutoRefresh,
  lastUpdated,
  tenantName,
  userEmail,
  isPlatformAdmin,
  inspectionMode,
  onLogout,
}: HeaderProps) {
  const envTenantId = process.env.NEXT_PUBLIC_DEMO_TENANT_ID;

  return (
    <header className="bg-white border-b border-slate-200/80 sticky top-0 z-30 shadow-2xs backdrop-blur-md bg-white/95">
      {inspectionMode && (
        <div className="bg-amber-500 text-slate-950 px-4 py-1.5 text-xs font-semibold flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span>🛡️ Platform Admin Inspection Mode: Inspecting Tenant [{tenantName || "Client Tenant"}]</span>
          </div>
          <a
            href="/admin"
            className="px-2.5 py-0.5 rounded bg-slate-950 text-white text-[11px] font-bold hover:bg-slate-800"
          >
            ← Return to Super Admin
          </a>
        </div>
      )}

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16 gap-4">
          {/* Logo & Brand */}
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-tr from-blue-600 to-indigo-600 flex items-center justify-center text-white font-bold shadow-md shadow-blue-500/20">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="font-bold text-slate-900 tracking-tight text-base sm:text-lg">
                  ORAI
                </span>
                <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-blue-50 text-blue-700 border border-blue-200/80">
                  Webhook Manager
                </span>
              </div>
              <p className="text-[11px] text-slate-500 hidden sm:block">
                WhatsApp Status Events & Ingestion Observability
              </p>
            </div>
          </div>

          {/* Right Actions: Tenant Context & Refresh & Sign Out */}
          <div className="flex items-center gap-3">
            {/* Tenant badge */}
            <div className="hidden md:flex items-center gap-2 px-3 py-1 rounded-lg bg-slate-50 border border-slate-200/80 text-xs">
              <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
              <span className="text-slate-500 font-medium">Tenant:</span>
              <span className="font-mono text-slate-800 font-semibold" title={tenantName || envTenantId || "Active Context"}>
                {tenantName || (envTenantId ? `${envTenantId.substring(0, 8)}...` : "Active")}
              </span>
            </div>

            {/* Auto Refresh Toggle */}
            <button
              onClick={onToggleAutoRefresh}
              className={`hidden sm:inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium border transition-colors cursor-pointer ${
                autoRefresh
                  ? "bg-emerald-50 text-emerald-700 border-emerald-200"
                  : "bg-slate-50 text-slate-600 border-slate-200 hover:bg-slate-100"
              }`}
              title="Auto refresh every 15 seconds"
            >
              <span className={`w-1.5 h-1.5 rounded-full ${autoRefresh ? "bg-emerald-500" : "bg-slate-400"}`} />
              Auto Refresh: {autoRefresh ? "ON" : "OFF"}
            </button>

            {/* Manual Refresh Button */}
            <button
              onClick={onRefreshAll}
              disabled={loading}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 shadow-xs transition-colors disabled:opacity-60 cursor-pointer"
            >
              <svg
                className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`}
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
              <span>Refresh</span>
            </button>

            {/* Platform Admin link or Sign Out */}
            {isPlatformAdmin && !inspectionMode && (
              <a
                href="/admin"
                className="px-3 py-1.5 rounded-lg text-xs font-semibold bg-indigo-50 text-indigo-700 border border-indigo-200 hover:bg-indigo-100 transition-colors"
              >
                Super Admin
              </a>
            )}

            {onLogout && (
              <button
                onClick={onLogout}
                className="px-3 py-1.5 rounded-lg text-xs font-medium text-slate-600 bg-slate-100 hover:bg-slate-200 transition-colors cursor-pointer"
                title={`Signed in as ${userEmail || "User"}`}
              >
                Sign Out
              </button>
            )}
          </div>
        </div>
      </div>
      {lastUpdated && (
        <div className="bg-slate-50/50 border-t border-slate-100 px-4 py-1 text-[11px] text-slate-400 text-right pr-6">
          Last synced: {lastUpdated.toLocaleTimeString()}
        </div>
      )}
    </header>
  );
}
