"use client";

import React from "react";
import { useTheme } from "./ThemeProvider";

interface OraiLoadingSceneProps {
  title?: string;
  subtitle?: string;
  theme?: "dark" | "light";
  fullScreen?: boolean;
}

export function OraiLoadingScene({
  title = "ORAI Webhook Manager",
  subtitle = "Initializing telemetry & event ingestion pipeline...",
  theme,
  fullScreen = true,
}: OraiLoadingSceneProps) {
  const { resolvedTheme } = useTheme();
  const isDark = theme ? theme === "dark" : resolvedTheme === "dark";

  const nodes = [
    { label: "Received", icon: "webhook" },
    { label: "Queued", icon: "queue" },
    { label: "Processed", icon: "process" },
    { label: "Dashboard", icon: "dashboard" },
  ];

  return (
    <div
      role="status"
      aria-live="polite"
      className={`flex flex-col items-center justify-center p-6 select-none ${
        fullScreen
          ? isDark
            ? "min-h-screen bg-[#0B0F19] text-slate-100"
            : "min-h-screen bg-[#F8F9FD] text-slate-800"
          : isDark
          ? "py-12 text-slate-100"
          : "py-12 text-slate-800"
      }`}
    >
      {/* Decorative ambient background blur */}
      <div className="relative flex flex-col items-center max-w-md w-full text-center">
        <div
          aria-hidden="true"
          className={`absolute -top-12 w-64 h-64 rounded-full blur-3xl pointer-events-none opacity-40 ${
            isDark ? "bg-purple-600/20" : "bg-purple-300/30"
          }`}
        />

        {/* ORAI Logo Emblem */}
        <div className="relative mb-6">
          <div
            className={`w-14 h-14 rounded-2xl flex items-center justify-center shadow-lg transition-transform ${
              isDark
                ? "bg-gradient-to-tr from-purple-600 via-indigo-600 to-cyan-500 shadow-purple-900/30 border border-purple-500/30"
                : "bg-gradient-to-tr from-purple-600 via-indigo-600 to-purple-500 shadow-purple-500/20 border border-purple-200"
            }`}
          >
            <svg
              className="w-7 h-7 text-white animate-pulse"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2.2}
              aria-hidden="true"
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
          </div>
          {/* Subtle spinning outer orbit ring */}
          <div
            aria-hidden="true"
            className="absolute -inset-1.5 rounded-[20px] border border-purple-500/30 border-t-purple-400 animate-spin pointer-events-none"
            style={{ animationDuration: "3s" }}
          />
        </div>

        {/* 4-Stage Pipeline Animation */}
        <div
          aria-hidden="true"
          className={`w-full p-4 rounded-2xl border mb-6 relative backdrop-blur-sm ${
            isDark
              ? "bg-slate-900/80 border-slate-800/90 shadow-xl"
              : "bg-white/90 border-purple-100 shadow-lg shadow-purple-500/5"
          }`}
        >
          {/* Connector beam line */}
          <div className="absolute top-[34px] left-10 right-10 h-0.5 bg-slate-700/40 overflow-hidden">
            <div
              className="h-full w-24 bg-gradient-to-r from-transparent via-purple-400 to-transparent animate-pipeline-beam"
            />
          </div>

          <div className="relative grid grid-cols-4 gap-2 text-center z-10">
            {nodes.map((node, index) => (
              <div key={node.label} className="flex flex-col items-center group">
                <div
                  className={`w-9 h-9 rounded-xl flex items-center justify-center text-xs font-semibold mb-2 transition-all duration-300 ${
                    isDark
                      ? "bg-slate-800/90 text-purple-300 border border-purple-500/30 group-hover:border-purple-400"
                      : "bg-purple-50 text-purple-700 border border-purple-200 group-hover:border-purple-400"
                  } animate-node-pulse`}
                  style={{ animationDelay: `${index * 0.35}s` }}
                >
                  {index === 0 && (
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M19 14l-7 7m0 0l-7-7m7 7V3" />
                    </svg>
                  )}
                  {index === 1 && (
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4" />
                    </svg>
                  )}
                  {index === 2 && (
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M9 3v2m6-2v2M9 19v2m6-2v2M5 9H3m2 6H3m18-6h-2m2 6h-2M7 19h10a2 2 0 002-2V7a2 2 0 00-2-2H7a2 2 0 00-2 2v10a2 2 0 002 2zM9 9h6v6H9V9z" />
                    </svg>
                  )}
                  {index === 3 && (
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
                    </svg>
                  )}
                </div>
                <span
                  className={`text-[11px] font-medium tracking-tight ${
                    isDark ? "text-slate-400" : "text-slate-600"
                  }`}
                >
                  {node.label}
                </span>
              </div>
            ))}
          </div>
        </div>

        {/* Copy & Status text */}
        <h2
          className={`text-base font-bold tracking-tight ${
            isDark ? "text-white" : "text-slate-900"
          }`}
        >
          {title}
        </h2>
        <p
          className={`text-xs mt-1.5 max-w-xs leading-relaxed ${
            isDark ? "text-slate-400" : "text-slate-500"
          }`}
        >
          {subtitle}
        </p>

        <span className="sr-only">Loading, please wait...</span>
      </div>
    </div>
  );
}
