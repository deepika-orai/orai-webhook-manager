import React from "react";

interface EmptyStateProps {
  title?: string;
  message?: string;
  onResetFilters?: () => void;
}

export function EmptyState({
  title = "No messages found",
  message = "Try changing your search terms or filters to find what you are looking for.",
  onResetFilters,
}: EmptyStateProps) {
  return (
    <div className="py-14 px-4 text-center flex flex-col items-center justify-center">
      <div className="w-14 h-14 rounded-2xl bg-purple-50 dark:bg-purple-950/50 border border-purple-100 dark:border-purple-800/60 flex items-center justify-center text-purple-600 dark:text-purple-400 mb-3.5 shadow-xs">
        <svg className="w-7 h-7" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.75}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
        </svg>
      </div>
      <h3 className="text-sm font-bold text-slate-900 dark:text-white">{title}</h3>
      <p className="mt-1 text-xs text-slate-500 dark:text-slate-400 max-w-sm leading-relaxed">{message}</p>
      {onResetFilters && (
        <button
          onClick={onResetFilters}
          className="mt-4 px-3.5 py-1.5 rounded-xl text-xs font-semibold text-purple-700 dark:text-purple-300 bg-purple-50 dark:bg-purple-950/50 hover:bg-purple-100 dark:hover:bg-purple-900/50 border border-purple-200 dark:border-purple-800/60 transition-colors cursor-pointer"
        >
          Reset all filters
        </button>
      )}
    </div>
  );
}

interface ErrorStateProps {
  title?: string;
  error: string;
  onRetry?: () => void;
}

export function ErrorState({
  title = "Failed to load dashboard data",
  error,
  onRetry,
}: ErrorStateProps) {
  return (
    <div className="rounded-2xl border border-rose-200 dark:border-rose-800/60 bg-rose-50/70 dark:bg-rose-950/30 p-6 text-center my-4 shadow-xs">
      <div className="w-11 h-11 rounded-xl bg-rose-100 dark:bg-rose-900/50 text-rose-600 dark:text-rose-300 flex items-center justify-center mx-auto mb-3 border border-rose-200 dark:border-rose-800/60">
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
        </svg>
      </div>
      <h3 className="text-sm font-bold text-rose-900 dark:text-rose-200">{title}</h3>
      <p className="mt-1 text-xs text-rose-700 dark:text-rose-300 font-mono bg-white/80 dark:bg-slate-950/70 py-1.5 px-3 rounded-lg inline-block max-w-xl truncate border border-rose-200/60 dark:border-rose-800/60">
        {error}
      </p>
      {onRetry && (
        <div className="mt-4">
          <button
            onClick={onRetry}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 shadow-sm transition-colors cursor-pointer"
          >
            Retry Request
          </button>
        </div>
      )}
    </div>
  );
}
