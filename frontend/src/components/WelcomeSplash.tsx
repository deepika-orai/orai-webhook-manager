"use client";

import React, { useEffect, useState } from "react";
import Image from "next/image";

export interface WelcomeSplashProps {
  tenantName?: string;
  onComplete?: () => void;
  /** Duration in ms for main display before fade-out begins (default 1600ms) */
  displayDurationMs?: number;
  /** Duration in ms for exit fade-out transition (default 300ms) */
  fadeDurationMs?: number;
}

export function WelcomeSplash({
  tenantName,
  onComplete,
  displayDurationMs = 1600,
  fadeDurationMs = 300,
}: WelcomeSplashProps) {
  const [isFadingOut, setIsFadingOut] = useState(false);
  const [prefersReducedMotion, setPrefersReducedMotion] = useState(() => {
    if (typeof window === "undefined" || !window.matchMedia) return false;
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  });

  // Safe tenant display name fallback order: tenant name -> Partner
  const safeTenantName =
    typeof tenantName === "string" && tenantName.trim().length > 0
      ? tenantName.trim()
      : "Partner";

  useEffect(() => {
    if (typeof window === "undefined" || !window.matchMedia) return;
    const mediaQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    const handler = (e: MediaQueryListEvent) => setPrefersReducedMotion(e.matches);
    mediaQuery.addEventListener("change", handler);
    return () => mediaQuery.removeEventListener("change", handler);
  }, []);

  useEffect(() => {
    // Main display timer -> trigger fade-out
    const fadeTimer = setTimeout(() => {
      if (prefersReducedMotion) {
        // Complete immediately for reduced motion
        onComplete?.();
      } else {
        setIsFadingOut(true);
      }
    }, displayDurationMs);

    return () => clearTimeout(fadeTimer);
  }, [displayDurationMs, prefersReducedMotion, onComplete]);

  useEffect(() => {
    if (!isFadingOut) return;

    // Fade-out timer -> trigger onComplete callback
    const exitTimer = setTimeout(() => {
      onComplete?.();
    }, fadeDurationMs);

    return () => clearTimeout(exitTimer);
  }, [isFadingOut, fadeDurationMs, onComplete]);

  return (
    <div
      role="status"
      aria-live="polite"
      aria-label="Welcome splash screen"
      className={`fixed inset-0 z-50 flex flex-col items-center justify-center p-6 select-none bg-gradient-to-b from-[#F5F3FF] via-[#FAF8FF] to-white dark:from-[#0B0F19] dark:via-[#111827] dark:to-[#0B0F19] text-slate-900 dark:text-slate-100 transition-opacity duration-300 ease-out ${
        isFadingOut ? "opacity-0 pointer-events-none" : "opacity-100"
      }`}
      style={{
        transitionDuration: prefersReducedMotion ? "0ms" : `${fadeDurationMs}ms`,
      }}
    >
      {/* Decorative ambient background glows strictly behind content */}
      <div
        aria-hidden="true"
        className="fixed inset-0 bg-grid-pattern dark:bg-grid-pattern-dark opacity-25 pointer-events-none -z-10"
      />
      <div
        aria-hidden="true"
        className="fixed top-1/4 left-1/2 -translate-x-1/2 w-96 h-96 bg-purple-500/15 dark:bg-purple-600/15 rounded-full blur-3xl pointer-events-none -z-10 animate-ambient-drift"
      />
      <div
        aria-hidden="true"
        className="fixed bottom-1/4 right-1/4 w-80 h-80 bg-cyan-500/10 dark:cyan-600/10 rounded-full blur-3xl pointer-events-none -z-10 animate-ambient-drift"
      />

      {/* Center Welcome Card / Layout */}
      <div className="relative flex flex-col items-center max-w-lg w-full text-center px-4 my-auto animate-card-enter">
        {/* Official ORAI Logo on a refined, matching surface card */}
        <div className="relative mb-4 flex justify-center items-center px-4 py-3 sm:px-6 sm:py-3.5 rounded-2xl bg-[#F8F6FF] dark:bg-slate-900/90 border border-purple-100/80 dark:border-slate-800 shadow-sm backdrop-blur-sm">
          <Image
            src="/branding/orai-logo.png"
            alt="ORAI Conversational AI Platform"
            width={300}
            height={150}
            priority
            className="w-[190px] sm:w-[260px] h-auto object-contain select-none pointer-events-none rounded-lg"
          />
        </div>

        {/* Product Sub-badge */}
        <div className="inline-flex items-center gap-2 mb-2.5">
          <span className="px-3 py-0.5 text-xs font-semibold rounded-full bg-purple-500/15 dark:bg-purple-500/20 text-purple-700 dark:text-purple-300 border border-purple-500/30">
            Webhook Manager
          </span>
        </div>

        {/* Welcome Title */}
        <h1 className="text-xl sm:text-2xl font-bold text-purple-950 dark:text-purple-100 tracking-tight mb-1.5">
          Welcome to ORAI
        </h1>

        {/* Tagline */}
        <p className="text-xs sm:text-sm font-medium text-slate-600 dark:text-slate-300 max-w-sm sm:max-w-md leading-relaxed mb-4 sm:mb-5">
          The Future of AI-Powered Customer Engagement
        </p>

        {/* Tenant Personalized Badge (with safe truncation for long tenant names) */}
        <div className="max-w-[320px] sm:max-w-md px-4 py-2 rounded-2xl bg-white/90 dark:bg-slate-900/90 border border-purple-100 dark:border-slate-800 backdrop-blur-md shadow-sm mb-5 flex items-center justify-center gap-2">
          <span className="w-2 h-2 rounded-full bg-emerald-500 shrink-0 animate-pulse-subtle" aria-hidden="true" />
          <span
            className="text-xs font-semibold text-purple-900 dark:text-purple-300 truncate"
            title={`Welcome, ${safeTenantName}`}
          >
            Welcome, {safeTenantName}
          </span>
        </div>

        {/* Status indicator */}
        <div className="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
          <svg
            className="w-3.5 h-3.5 animate-spin text-purple-600 dark:text-purple-400 shrink-0"
            fill="none"
            viewBox="0 0 24 24"
            aria-hidden="true"
          >
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
          </svg>
          <span>Preparing your dashboard…</span>
        </div>
      </div>
    </div>
  );
}
