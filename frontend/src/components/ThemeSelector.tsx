"use client";

import React from "react";
import { ThemeMode, useTheme } from "./ThemeProvider";

interface ThemeSelectorProps {
  variant?: "header" | "floating" | "compact";
  className?: string;
}

export function ThemeSelector({ variant = "header", className = "" }: ThemeSelectorProps) {
  const { themeMode, setThemeMode, mounted } = useTheme();

  const options: { mode: ThemeMode; label: string; icon: React.ReactNode }[] = [
    {
      mode: "light",
      label: "Light theme",
      icon: (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2} aria-hidden="true">
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
        </svg>
      ),
    },
    {
      mode: "dark",
      label: "Dark theme",
      icon: (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2} aria-hidden="true">
          <path strokeLinecap="round" strokeLinejoin="round" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
        </svg>
      ),
    },
    {
      mode: "system",
      label: "System theme",
      icon: (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2} aria-hidden="true">
          <path strokeLinecap="round" strokeLinejoin="round" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
        </svg>
      ),
    },
  ];

  return (
    <div
      role="group"
      aria-label="Theme selector"
      className={`inline-flex items-center p-0.5 rounded-xl border transition-colors ${
        variant === "header"
          ? "bg-slate-100/90 dark:bg-slate-800/90 border-slate-200/80 dark:border-slate-700/80 shadow-2xs"
          : "bg-slate-100/90 dark:bg-slate-800/90 border-slate-200 dark:border-slate-700 shadow-sm"
      } ${className}`}
    >
      {options.map((opt) => {
        // Hydration safety: before mounting, all buttons render with aria-pressed="false"
        // matching the deterministic server-rendered state and preventing SSR mismatch.
        const isActive = mounted ? themeMode === opt.mode : false;
        return (
          <button
            key={opt.mode}
            type="button"
            onClick={() => setThemeMode(opt.mode)}
            aria-label={opt.label}
            aria-pressed={isActive}
            title={opt.label}
            className={`p-1.5 rounded-lg text-xs font-medium transition-all duration-150 flex items-center justify-center cursor-pointer ${
              isActive
                ? "bg-white dark:bg-purple-600 text-purple-700 dark:text-white shadow-xs font-semibold"
                : "text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200"
            } focus-visible:ring-2 focus-visible:ring-purple-500 focus-visible:outline-none`}
          >
            {opt.icon}
            <span className="sr-only">{opt.label}</span>
          </button>
        );
      })}
    </div>
  );
}
