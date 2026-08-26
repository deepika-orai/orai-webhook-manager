"use client";

import React, { createContext, useContext, useEffect, useState, useSyncExternalStore } from "react";

export type ThemeMode = "light" | "dark" | "system";
export type ResolvedTheme = "light" | "dark";

interface ThemeContextType {
  themeMode: ThemeMode;
  resolvedTheme: ResolvedTheme;
  setThemeMode: (mode: ThemeMode) => void;
  // Ergonomic aliases
  theme: ThemeMode;
  setTheme: (mode: ThemeMode) => void;
  mounted: boolean;
}

const STORAGE_KEY = "orai-theme";

const ThemeContext = createContext<ThemeContextType>({
  themeMode: "system",
  resolvedTheme: "light",
  setThemeMode: () => {},
  theme: "system",
  setTheme: () => {},
  mounted: false,
});

export function useTheme() {
  return useContext(ThemeContext);
}

function getSystemTheme(): ResolvedTheme {
  if (typeof window === "undefined" || !window.matchMedia) return "light";
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function getStoredTheme(): ThemeMode {
  if (typeof window === "undefined") return "system";
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "system") {
      return stored;
    }
  } catch {
    // localStorage unavailable, corrupt or throwing SecurityError
  }
  return "system";
}

function applyThemeToDocument(resolved: ResolvedTheme) {
  if (typeof document === "undefined") return;
  const root = document.documentElement;
  root.setAttribute("data-theme", resolved);
  root.style.colorScheme = resolved;
  if (resolved === "dark") {
    root.classList.add("dark");
  } else {
    root.classList.remove("dark");
  }
}

function subscribeMediaQuery(callback: () => void) {
  if (typeof window === "undefined" || !window.matchMedia) return () => {};
  const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
  if (mediaQuery.addEventListener) {
    mediaQuery.addEventListener("change", callback);
    return () => mediaQuery.removeEventListener("change", callback);
  } else if ("addListener" in mediaQuery) {
    const legacyQuery = mediaQuery as unknown as {
      addListener: (cb: () => void) => void;
      removeListener: (cb: () => void) => void;
    };
    legacyQuery.addListener(callback);
    return () => {
      legacyQuery.removeListener(callback);
    };
  }
  return () => {};
}

function getSystemThemeSnapshot(): ResolvedTheme {
  return getSystemTheme();
}

function getSystemThemeServerSnapshot(): ResolvedTheme {
  return "light";
}

const emptySubscribe = () => () => {};

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const isClient = useSyncExternalStore(
    emptySubscribe,
    () => true,
    () => false
  );

  const systemTheme = useSyncExternalStore(
    subscribeMediaQuery,
    getSystemThemeSnapshot,
    getSystemThemeServerSnapshot
  );

  const [explicitTheme, setExplicitTheme] = useState<ThemeMode>("system");

  // Determine active mode: on client, if mode wasn't explicitly changed via state, read stored preference
  const storedMode = isClient ? getStoredTheme() : "system";
  const activeMode = explicitTheme === "system" && isClient ? storedMode : explicitTheme;

  const resolvedTheme: ResolvedTheme =
    activeMode === "system"
      ? isClient
        ? systemTheme
        : "light"
      : activeMode;

  // Apply theme to document on mount and whenever resolvedTheme changes
  useEffect(() => {
    if (!isClient) return;
    applyThemeToDocument(resolvedTheme);
  }, [isClient, resolvedTheme]);

  const setThemeMode = (mode: ThemeMode) => {
    const validMode: ThemeMode = mode === "light" || mode === "dark" || mode === "system" ? mode : "system";
    setExplicitTheme(validMode);

    try {
      window.localStorage.setItem(STORAGE_KEY, validMode);
    } catch {
      // ignore storage errors
    }
  };

  return (
    <ThemeContext.Provider
      value={{
        themeMode: activeMode,
        resolvedTheme,
        setThemeMode,
        theme: activeMode,
        setTheme: setThemeMode,
        mounted: isClient,
      }}
    >
      {children}
    </ThemeContext.Provider>
  );
}
