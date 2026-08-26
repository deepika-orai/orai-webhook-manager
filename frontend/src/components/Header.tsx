"use client";

import React from "react";
import { ThemeSelector } from "./ThemeSelector";

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
    <header className="bg-white/95 dark:bg-slate-900/90 border-b border-purple-100/70 dark:border-slate-800/80 sticky top-0 z-30 shadow-2xs backdrop-blur-md transition-colors duration-150">
      {inspectionMode && (
        <div
          role="alert"
          className="bg-amber-500 text-slate-950 px-4 py-1.5 text-xs font-semibold flex items-center justify-between shadow-xs"
        >
          <div className="flex items-center gap-2">
            <span aria-hidden="true">🛡️</span>
            <span>Platform Admin Inspection Mode: Inspecting Tenant [{tenantName || "Client Tenant"}]</span>
          </div>
          <a
            href="/admin"
            className="px-2.5 py-0.5 rounded bg-slate-950 text-white text-[11px] font-bold hover:bg-slate-800 transition-colors"
          >
            ← Return to Super Admin
          </a>
        </div>
      )}

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16 gap-3 sm:gap-4">
          {/* Logo & Brand */}
          <div className="flex items-center gap-3 shrink-0">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-tr from-purple-600 via-indigo-600 to-purple-500 flex items-center justify-center text-white font-bold shadow-md shadow-purple-600/20 border border-purple-400/30">
              <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="font-bold text-slate-900 dark:text-white tracking-tight text-base sm:text-lg">
                  ORAI
                </span>
                <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-purple-50 dark:bg-purple-950/50 text-purple-700 dark:text-purple-300 border border-purple-200 dark:border-purple-800/60">
                  Webhook Manager
                </span>
              </div>
              <p className="text-[11px] text-slate-500 dark:text-slate-400 hidden sm:block">
                WhatsApp Status Events & Ingestion Observability
              </p>
            </div>
          </div>

          {/* Right Actions: Tenant Context & Theme & Refresh & Sign Out */}
          <div className="flex items-center gap-2 sm:gap-2.5">
            {/* Tenant badge */}
            <div className="hidden lg:flex items-center gap-2 px-3 py-1 rounded-xl bg-purple-50/60 dark:bg-slate-800/80 border border-purple-100 dark:border-slate-700/80 text-xs">
              <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse-subtle" />
              <span className="text-slate-500 dark:text-slate-400 font-medium">Tenant:</span>
              <span
                className="font-mono text-purple-900 dark:text-purple-300 font-semibold truncate max-w-[140px]"
                title={tenantName || envTenantId || "Active Context"}
              >
                {tenantName || (envTenantId ? `${envTenantId.substring(0, 8)}...` : "Active")}
              </span>
            </div>

            {/* Theme Selector */}
            <ThemeSelector variant="header" />

            {/* Auto Refresh Toggle */}
            <button
              onClick={onToggleAutoRefresh}
              className={`hidden sm:inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-xl text-xs font-medium border transition-colors cursor-pointer ${
                autoRefresh
                  ? "bg-emerald-50 dark:bg-emerald-950/40 text-emerald-700 dark:text-emerald-300 border-emerald-200 dark:border-emerald-800/60"
                  : "bg-slate-50 dark:bg-slate-800/80 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:bg-slate-100 dark:hover:bg-slate-700"
              }`}
              title="Auto refresh every 15 seconds"
            >
              <span
                className={`w-1.5 h-1.5 rounded-full ${
                  autoRefresh ? "bg-emerald-500 animate-pulse-subtle" : "bg-slate-400"
                }`}
              />
              <span className="hidden md:inline">Auto Refresh: </span>
              {autoRefresh ? "ON" : "OFF"}
            </button>

            {/* Manual Refresh Button */}
            <button
              onClick={onRefreshAll}
              disabled={loading}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold text-white bg-gradient-to-r from-purple-600 to-indigo-600 hover:from-purple-500 hover:to-indigo-500 shadow-sm shadow-purple-600/20 transition-all disabled:opacity-60 cursor-pointer"
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
              <span className="hidden xs:inline">Refresh</span>
            </button>

            {/* Platform Admin link or Sign Out */}
            {isPlatformAdmin && !inspectionMode && (
              <a
                href="/admin"
                className="px-3 py-1.5 rounded-xl text-xs font-semibold bg-purple-50 dark:bg-purple-950/40 text-purple-700 dark:text-purple-300 border border-purple-200 dark:border-purple-800/60 hover:bg-purple-100 dark:hover:bg-purple-900/40 transition-colors hidden sm:block"
              >
                Super Admin
              </a>
            )}

            {onLogout && (
              <button
                onClick={onLogout}
                className="px-3 py-1.5 rounded-xl text-xs font-medium text-slate-600 dark:text-slate-300 bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors cursor-pointer"
                title={`Signed in as ${userEmail || "User"}`}
              >
                Sign Out
              </button>
            )}
          </div>
        </div>
      </div>

      {lastUpdated && (
        <div className="bg-slate-50/60 dark:bg-slate-950/40 border-t border-purple-50 dark:border-slate-800/60 px-4 py-1 text-[11px] text-slate-400 dark:text-slate-500 text-right pr-6 sm:pr-8">
          Last synced: {lastUpdated.toLocaleTimeString()}
        </div>
      )}
    </header>
  );
}
