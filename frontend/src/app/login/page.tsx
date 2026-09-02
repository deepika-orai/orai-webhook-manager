"use client";

import React, { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { loginApi } from "../../lib/api";
import { ThemeSelector } from "../../components/ThemeSelector";

function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const reason = searchParams?.get("reason");
  const isSessionExpired = reason === "session_expired";
  const isSignInRequired = reason === "sign_in_required" || reason === "unauthenticated";

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      setError("Please provide both email and password.");
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const res = await loginApi(email, password);

      if (res.mustChangePassword) {
        router.push("/change-password");
      } else if (res.user.isPlatformAdmin) {
        router.push("/admin");
      } else {
        router.push("/dashboard");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Invalid email or password");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-[#F8F9FD] dark:bg-[#0B0F19] text-slate-900 dark:text-slate-100 flex flex-col justify-center items-center p-4 sm:p-6 relative overflow-hidden transition-colors duration-150">
      {/* Top Controls: Theme Selector */}
      <div className="absolute top-4 right-4 sm:top-6 sm:right-6 z-20">
        <ThemeSelector variant="compact" />
      </div>

      {/* Decorative ambient background blur strictly behind content */}
      <div
        aria-hidden="true"
        className="fixed inset-0 bg-grid-pattern dark:bg-grid-pattern-dark opacity-30 pointer-events-none -z-10"
      />
      <div
        aria-hidden="true"
        className="fixed top-1/4 left-1/2 -translate-x-1/2 w-[500px] h-[500px] bg-gradient-to-br from-purple-600/15 via-indigo-600/10 to-cyan-500/10 rounded-full blur-3xl pointer-events-none animate-ambient-drift -z-10"
      />
      <div
        aria-hidden="true"
        className="fixed -bottom-20 -left-20 w-80 h-80 bg-purple-600/10 rounded-full blur-3xl pointer-events-none -z-10"
      />
      <div
        aria-hidden="true"
        className="fixed -top-20 -right-20 w-80 h-80 bg-cyan-600/10 rounded-full blur-3xl pointer-events-none -z-10"
      />

      {/* Main Login Card */}
      <div className="w-full max-w-md bg-white/95 dark:bg-slate-900/80 border border-purple-100/80 dark:border-slate-800/90 backdrop-blur-2xl rounded-3xl p-8 sm:p-9 shadow-xl dark:shadow-2xl relative z-10 animate-card-enter">
        {/* Logo / Header */}
        <div className="flex flex-col items-center text-center mb-8">
          <div className="relative mb-3.5">
            <div className="w-13 h-13 rounded-2xl bg-gradient-to-tr from-purple-600 via-indigo-600 to-cyan-500 flex items-center justify-center shadow-lg shadow-purple-600/30 border border-purple-500/30">
              <svg
                className="w-7 h-7 text-white"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                strokeWidth={2.2}
                aria-hidden="true"
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
            </div>
            {/* Subtle glow badge */}
            <div
              aria-hidden="true"
              className="absolute -inset-1 rounded-2xl bg-gradient-to-r from-purple-600 to-cyan-500 opacity-20 blur-sm pointer-events-none"
            />
          </div>

          <div className="inline-flex items-center gap-2 mb-1.5">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">ORAI</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold rounded-full bg-purple-500/15 dark:bg-purple-500/20 text-purple-700 dark:text-purple-300 border border-purple-500/30">
              Webhook Manager
            </span>
          </div>
          <p className="text-xs text-slate-500 dark:text-slate-400 max-w-xs leading-relaxed">
            Enterprise WhatsApp status telemetry & real-time webhook observability
          </p>
        </div>

        {/* Neutral sign-in required info banner for direct protected access */}
        {isSignInRequired && !isSessionExpired && !error && (
          <div
            role="status"
            className="mb-6 p-4 rounded-xl bg-purple-500/10 border border-purple-500/30 text-purple-700 dark:text-purple-300 text-xs flex items-center gap-3 animate-card-enter"
          >
            <svg
              className="w-5 h-5 shrink-0 text-purple-600 dark:text-purple-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2}
              aria-hidden="true"
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            <span>Please sign in to continue.</span>
          </div>
        )}

        {/* Session expired info banner */}
        {isSessionExpired && !error && (
          <div
            role="status"
            className="mb-6 p-4 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-700 dark:text-amber-400 text-xs flex items-center gap-3 animate-card-enter"
          >
            <svg
              className="w-5 h-5 shrink-0 text-amber-500 dark:text-amber-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2}
              aria-hidden="true"
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            <span>Your session expired. Please sign in again.</span>
          </div>
        )}

        {/* Error alert */}
        {error && (
          <div
            role="alert"
            className="mb-6 p-4 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-600 dark:text-rose-400 text-xs flex items-center gap-3 animate-card-enter"
          >
            <svg
              className="w-5 h-5 flex-shrink-0 text-rose-500 dark:text-rose-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              strokeWidth={2}
              aria-hidden="true"
            >
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label
              htmlFor="email-input"
              className="block text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider mb-2"
            >
              Email Address
            </label>
            <div className="relative">
              <input
                id="email-input"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="user@orai.io"
                className="w-full pl-10 pr-4 py-3 rounded-xl bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-700/80 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-purple-500 focus:ring-2 focus:ring-purple-500/30 transition-all"
              />
              <svg
                aria-hidden="true"
                className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3.5 top-3.5 pointer-events-none"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M16 12a4 4 0 10-8 0 4 4 0 008 0zm0 0v1.5a2.5 2.5 0 005 0V12a9 9 0 10-9 9m4.5-1.206a8.959 8.959 0 01-4.5 1.207" />
              </svg>
            </div>
          </div>

          <div>
            <label
              htmlFor="password-input"
              className="block text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider mb-2"
            >
              Password
            </label>
            <div className="relative">
              <input
                id="password-input"
                type="password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••••••"
                className="w-full pl-10 pr-4 py-3 rounded-xl bg-slate-50 dark:bg-slate-950/70 border border-slate-200 dark:border-slate-700/80 text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-500 text-sm focus:outline-none focus:border-purple-500 focus:ring-2 focus:ring-purple-500/30 transition-all"
              />
              <svg
                aria-hidden="true"
                className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3.5 top-3.5 pointer-events-none"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                strokeWidth={2}
              >
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
              </svg>
            </div>
          </div>

          <div className="pt-2">
            <button
              type="submit"
              disabled={loading}
              className="w-full py-3.5 px-4 rounded-xl bg-gradient-to-r from-purple-600 via-indigo-600 to-purple-600 hover:from-purple-500 hover:via-indigo-500 hover:to-purple-500 text-white font-semibold text-sm shadow-lg shadow-purple-600/30 transition-all duration-200 flex items-center justify-center gap-2 disabled:opacity-60 disabled:cursor-not-allowed cursor-pointer"
            >
              {loading ? (
                <>
                  <svg className="animate-spin w-4 h-4 text-white" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                  </svg>
                  <span>Authenticating...</span>
                </>
              ) : (
                <>
                  <span>Sign In</span>
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M14 5l7 7m0 0l-7 7m7-7H3" />
                  </svg>
                </>
              )}
            </button>
          </div>
        </form>

        {/* Security and Trust Footer */}
        <div className="mt-8 pt-6 border-t border-slate-200 dark:border-slate-800/80 text-center space-y-2">
          <div className="flex items-center justify-center gap-4 text-[11px] text-slate-500 dark:text-slate-400">
            <span className="flex items-center gap-1.5 font-medium">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
              Tenant-isolated access
            </span>
            <span className="flex items-center gap-1.5 font-medium">
              <span className="w-1.5 h-1.5 rounded-full bg-purple-500" />
              Secure session
            </span>
          </div>
          <p className="text-[11px] text-slate-400 dark:text-slate-500">
            Protected by encrypted session authentication and tenant boundary isolation.
          </p>
        </div>
      </div>
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<div className="min-h-screen bg-[#F8F9FD] dark:bg-[#0B0F19]" />}>
      <LoginForm />
    </Suspense>
  );
}
