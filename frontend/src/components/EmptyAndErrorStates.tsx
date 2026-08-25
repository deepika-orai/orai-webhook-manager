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
    <div className="py-12 px-4 text-center flex flex-col items-center justify-center">
      <div className="w-12 h-12 rounded-full bg-slate-100 flex items-center justify-center text-slate-400 mb-3">
        <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
        </svg>
      </div>
      <h3 className="text-sm font-semibold text-slate-900">{title}</h3>
      <p className="mt-1 text-xs text-slate-500 max-w-sm">{message}</p>
      {onResetFilters && (
        <button
          onClick={onResetFilters}
          className="mt-4 px-3 py-1.5 rounded-lg text-xs font-medium text-blue-700 bg-blue-50 hover:bg-blue-100 border border-blue-200 transition-colors cursor-pointer"
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
    <div className="rounded-xl border border-rose-200 bg-rose-50/70 p-6 text-center my-4 shadow-2xs">
      <div className="w-10 h-10 rounded-full bg-rose-100 text-rose-600 flex items-center justify-center mx-auto mb-3">
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
        </svg>
      </div>
      <h3 className="text-sm font-semibold text-rose-900">{title}</h3>
      <p className="mt-1 text-xs text-rose-700 font-mono bg-rose-100/60 py-1 px-3 rounded inline-block max-w-xl truncate">
        {error}
      </p>
      {onRetry && (
        <div className="mt-4">
          <button
            onClick={onRetry}
            className="px-4 py-1.5 rounded-lg text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 shadow-2xs transition-colors cursor-pointer"
          >
            Retry Request
          </button>
        </div>
      )}
    </div>
  );
}
