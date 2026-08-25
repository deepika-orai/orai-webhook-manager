"use client";

import React, { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  AdminTenantListItem,
  AdminTenantSummary,
  AuthSession,
  CreateTenantResponse,
  PlatformSummary,
  ResetPasswordResponse,
  RotateKeyResponse,
} from "../../types/auth";
import {
  createTenantApi,
  getAdminTenantsApi,
  getAdminTenantSummaryApi,
  getCurrentSessionApi,
  getPlatformSummaryApi,
  logoutApi,
  resetClientPasswordApi,
  rotateWebhookKeyApi,
  updateTenantStatusApi,
} from "../../lib/api";

export default function SuperAdminPage() {
  const router = useRouter();
  const [session, setSession] = useState<AuthSession | null>(null);
  const [authChecking, setAuthChecking] = useState(true);
  const [isAuthorized, setIsAuthorized] = useState(false);
  const [summary, setSummary] = useState<PlatformSummary | null>(null);
  const [tenants, setTenants] = useState<AdminTenantListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<"ALL" | "ACTIVE" | "SUSPENDED">("ALL");

  const [loadingTenants, setLoadingTenants] = useState(false);
  const [adminApiError, setAdminApiError] = useState<string | null>(null);

  // Modals state
  const [showOnboardModal, setShowOnboardModal] = useState(false);
  const [onboardingData, setOnboardingData] = useState({
    name: "",
    slug: "",
    adminEmail: "",
    adminFullName: "",
  });
  const [onboardLoading, setOnboardLoading] = useState(false);
  const [onboardError, setOnboardError] = useState<string | null>(null);

  // One-time credential modal state
  const [createdCredentials, setCreatedCredentials] = useState<CreateTenantResponse | null>(null);
  const [resetCredentials, setResetCredentials] = useState<ResetPasswordResponse | null>(null);
  const [rotatedKeyData, setRotatedKeyData] = useState<RotateKeyResponse | null>(null);
  const [copyFeedback, setCopyFeedback] = useState<string | null>(null);

  // Action confirmations
  const [confirmSuspendTenant, setConfirmSuspendTenant] = useState<AdminTenantListItem | null>(null);
  const [confirmResetTenant, setConfirmResetTenant] = useState<AdminTenantListItem | null>(null);
  const [inspectSummary, setInspectSummary] = useState<AdminTenantSummary | null>(null);
  const [inspectLoading, setInspectLoading] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);

  const handleInspectTenantSummary = async (tenantId: string) => {
    setInspectLoading(true);
    try {
      const data = await getAdminTenantSummaryApi(tenantId);
      setInspectSummary(data);
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        alert("Action unauthorized: Super admin privileges required.");
        router.replace("/dashboard");
        return;
      }
      alert(err instanceof Error ? err.message : "Failed to load tenant summary");
    } finally {
      setInspectLoading(false);
    }
  };

  const handleRotateWebhookKey = async (endpointId: string) => {
    if (!confirm("Are you sure you want to rotate this webhook key? The old key will immediately stop working.")) {
      return;
    }
    setActionLoading(true);
    try {
      const res = await rotateWebhookKeyApi(endpointId);
      setRotatedKeyData(res);
      if (inspectSummary) {
        handleInspectTenantSummary(inspectSummary.id);
      }
      loadTenants();
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        alert("Action unauthorized: Super admin privileges required.");
        router.replace("/dashboard");
        return;
      }
      alert(err instanceof Error ? err.message : "Failed to rotate webhook key");
    } finally {
      setActionLoading(false);
    }
  };

  const loadSummary = useCallback(async () => {
    try {
      const data = await getPlatformSummaryApi();
      setSummary(data);
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        setAdminApiError("Your session is not authorized for platform administration. Redirecting to dashboard...");
        router.replace("/dashboard");
        return;
      }
      setAdminApiError(err instanceof Error ? err.message : "Failed to load platform summary");
    }
  }, [router]);

  const loadTenants = useCallback(async () => {
    setLoadingTenants(true);
    try {
      const isActiveParam =
        statusFilter === "ACTIVE" ? true : statusFilter === "SUSPENDED" ? false : undefined;
      const res = await getAdminTenantsApi(search, isActiveParam, page, 20);
      setTenants(res.items);
      setTotalCount(res.totalCount);
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        setAdminApiError("Your session is not authorized for platform administration. Redirecting to dashboard...");
        router.replace("/dashboard");
        return;
      }
      setAdminApiError(err instanceof Error ? err.message : "Failed to load clients");
    } finally {
      setLoadingTenants(false);
    }
  }, [search, statusFilter, page, router]);

  // Check auth session before loading admin data
  useEffect(() => {
    let ignore = false;
    async function init() {
      try {
        const sess = await getCurrentSessionApi();
        if (ignore) return;
        if (!sess || !sess.user) {
          router.replace("/login");
          return;
        }
        if (sess.user.mustChangePassword) {
          router.replace("/change-password");
          return;
        }
        if (!sess.user.isPlatformAdmin) {
          // Tenant users navigating directly to /admin must be redirected to /dashboard without calling admin APIs
          router.replace("/dashboard");
          return;
        }
        setSession(sess);
        setIsAuthorized(true);
        setAuthChecking(false);
        await Promise.all([loadSummary(), loadTenants()]);
      } catch {
        if (ignore) return;
        router.replace("/login");
      }
    }
    init();
    return () => {
      ignore = true;
    };
  }, [router, loadSummary, loadTenants]);

  const handleLogout = async () => {
    await logoutApi();
    router.push("/login");
  };

  const handleSlugAutoFill = (name: string) => {
    const slug = name
      .toLowerCase()
      .trim()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "");
    setOnboardingData((prev) => ({ ...prev, name, slug }));
  };

  const handleOnboardSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setOnboardError(null);
    setOnboardLoading(true);

    try {
      const res = await createTenantApi(onboardingData);
      setShowOnboardModal(false);
      setOnboardingData({ name: "", slug: "", adminEmail: "", adminFullName: "" });
      setCreatedCredentials(res);
      loadSummary();
      loadTenants();
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        setOnboardError("Super admin privileges required. Redirecting to dashboard...");
        router.replace("/dashboard");
        return;
      }
      setOnboardError(err instanceof Error ? err.message : "Failed to onboard client");
    } finally {
      setOnboardLoading(false);
    }
  };

  const handleToggleStatus = async () => {
    if (!confirmSuspendTenant) return;
    setActionLoading(true);
    try {
      await updateTenantStatusApi(confirmSuspendTenant.id, !confirmSuspendTenant.isActive);
      setConfirmSuspendTenant(null);
      loadSummary();
      loadTenants();
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        alert("Action unauthorized: Super admin privileges required.");
        router.replace("/dashboard");
        return;
      }
      alert(err instanceof Error ? err.message : "Failed to update status");
    } finally {
      setActionLoading(false);
    }
  };

  const handleResetPassword = async () => {
    if (!confirmResetTenant) return;
    setActionLoading(true);
    try {
      const res = await resetClientPasswordApi(confirmResetTenant.id);
      setConfirmResetTenant(null);
      setResetCredentials(res);
    } catch (err) {
      if ((err as { status?: number })?.status === 403) {
        alert("Action unauthorized: Super admin privileges required.");
        router.replace("/dashboard");
        return;
      }
      alert(err instanceof Error ? err.message : "Failed to reset password");
    } finally {
      setActionLoading(false);
    }
  };

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopyFeedback(label);
    setTimeout(() => setCopyFeedback(null), 2500);
  };

  if (authChecking || !isAuthorized) {
    return (
      <div className="min-h-screen bg-[#0B0F19] flex items-center justify-center text-slate-400">
        <div className="flex flex-col items-center gap-3">
          <svg className="animate-spin w-8 h-8 text-indigo-500" fill="none" viewBox="0 0 24 24">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
          </svg>
          <span className="text-xs font-medium tracking-wide">Verifying platform authorization...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[#0B0F19] text-slate-100 flex flex-col">
      {/* Top Navbar */}
      <header className="border-b border-slate-800/80 bg-slate-900/60 backdrop-blur-md sticky top-0 z-30 px-6 py-4 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-indigo-600 to-cyan-500 flex items-center justify-center shadow-lg shadow-indigo-500/20">
            <svg className="w-6 h-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-lg font-bold text-white tracking-tight">ORAI Super Admin</h1>
              <span className="px-2 py-0.5 text-xs font-semibold rounded-md bg-indigo-500/20 text-indigo-400 border border-indigo-500/30">
                Platform Admin
              </span>
            </div>
            <p className="text-xs text-slate-400">Multi-tenant cluster & client management</p>
          </div>
        </div>

        <div className="flex items-center gap-4">
          <div className="text-right hidden sm:block">
            <p className="text-sm font-medium text-slate-200">{session?.user.fullName || "Super Admin"}</p>
            <p className="text-xs text-slate-400">{session?.user.email}</p>
          </div>
          <button
            onClick={handleLogout}
            className="px-3.5 py-2 rounded-xl text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 hover:border-slate-600 transition-colors flex items-center gap-2"
          >
            <svg className="w-4 h-4 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
            </svg>
            <span>Sign Out</span>
          </button>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto p-6 space-y-8">
        {adminApiError && (
          <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-400 text-sm flex items-center justify-between gap-3">
            <span>{adminApiError}</span>
            <button
              onClick={() => setAdminApiError(null)}
              className="text-xs text-rose-300 hover:text-white underline"
            >
              Dismiss
            </button>
          </div>
        )}
        {/* Platform Overview Cards */}
        <section className="space-y-4">
          <h2 className="text-sm font-semibold uppercase tracking-wider text-slate-400">
            Platform Overview
          </h2>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-7 gap-4">
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Total Tenants</span>
              <span className="text-2xl font-bold text-white mt-1">{summary?.totalTenants ?? "—"}</span>
            </div>
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Active Clients</span>
              <span className="text-2xl font-bold text-emerald-400 mt-1">{summary?.activeTenants ?? "—"}</span>
            </div>
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Suspended</span>
              <span className="text-2xl font-bold text-rose-400 mt-1">{summary?.suspendedTenants ?? "—"}</span>
            </div>
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Total Messages</span>
              <span className="text-2xl font-bold text-indigo-400 mt-1">{summary?.totalMessages?.toLocaleString() ?? "—"}</span>
            </div>
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Failed Messages</span>
              <span className="text-2xl font-bold text-amber-400 mt-1">{summary?.failedMessages?.toLocaleString() ?? "—"}</span>
            </div>
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Pending Inbox</span>
              <span className="text-2xl font-bold text-cyan-400 mt-1">{summary?.pendingInbox?.toLocaleString() ?? "—"}</span>
            </div>
            <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex flex-col">
              <span className="text-xs text-slate-400">Dead Letters</span>
              <span className="text-2xl font-bold text-purple-400 mt-1">{summary?.deadLetterInbox?.toLocaleString() ?? "—"}</span>
            </div>
          </div>
        </section>

        {/* Tenant Management Table & Actions */}
        <section className="space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div>
              <h2 className="text-lg font-bold text-white tracking-tight">Client Organizations</h2>
              <p className="text-xs text-slate-400">Onboard, inspect, configure and manage client tenant instances</p>
            </div>

            <button
              onClick={() => setShowOnboardModal(true)}
              className="px-4 py-2.5 rounded-xl bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs font-semibold shadow-lg shadow-indigo-500/25 transition-all flex items-center justify-center gap-2 self-start sm:self-auto"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
              </svg>
              <span>Onboard New Client</span>
            </button>
          </div>

          {/* Filter Bar */}
          <div className="bg-slate-900/40 border border-slate-800 rounded-2xl p-4 flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-center gap-3 flex-1 min-w-[260px]">
              <div className="relative w-full max-w-md">
                <svg
                  className="w-4 h-4 text-slate-500 absolute left-3.5 top-1/2 -translate-y-1/2"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <input
                  type="text"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search by client name, slug, admin email..."
                  className="w-full pl-10 pr-4 py-2 rounded-xl bg-slate-950/60 border border-slate-800 text-slate-200 placeholder-slate-500 text-xs focus:outline-none focus:border-indigo-500"
                />
              </div>
            </div>

            <div className="flex items-center gap-2">
              <button
                onClick={() => setStatusFilter("ALL")}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${
                  statusFilter === "ALL"
                    ? "bg-indigo-600 text-white"
                    : "bg-slate-800 text-slate-400 hover:text-slate-200"
                }`}
              >
                All
              </button>
              <button
                onClick={() => setStatusFilter("ACTIVE")}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${
                  statusFilter === "ACTIVE"
                    ? "bg-emerald-600 text-white"
                    : "bg-slate-800 text-slate-400 hover:text-slate-200"
                }`}
              >
                Active
              </button>
              <button
                onClick={() => setStatusFilter("SUSPENDED")}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${
                  statusFilter === "SUSPENDED"
                    ? "bg-rose-600 text-white"
                    : "bg-slate-800 text-slate-400 hover:text-slate-200"
                }`}
              >
                Suspended
              </button>
              <button
                onClick={() => {
                  loadSummary();
                  loadTenants();
                }}
                title="Refresh"
                className="p-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-slate-200 border border-slate-700 transition-colors"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                </svg>
              </button>
            </div>
          </div>

          {/* Tenants Table */}
          <div className="bg-slate-900/60 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/70 text-slate-400 border-b border-slate-800 uppercase tracking-wider font-semibold">
                  <tr>
                    <th className="px-6 py-4">Client / Slug</th>
                    <th className="px-6 py-4">Admin Contact</th>
                    <th className="px-6 py-4">Status</th>
                    <th className="px-6 py-4">Endpoints</th>
                    <th className="px-6 py-4">Messages</th>
                    <th className="px-6 py-4">Created</th>
                    <th className="px-6 py-4 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60">
                  {loadingTenants ? (
                    <tr>
                      <td colSpan={7} className="px-6 py-12 text-center text-slate-500">
                        <div className="flex flex-col items-center justify-center gap-2">
                          <svg className="animate-spin w-6 h-6 text-indigo-500" fill="none" viewBox="0 0 24 24">
                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                          </svg>
                          <span>Loading tenants...</span>
                        </div>
                      </td>
                    </tr>
                  ) : tenants.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="px-6 py-12 text-center text-slate-500">
                        No clients found matching your filters.
                      </td>
                    </tr>
                  ) : (
                    tenants.map((t) => (
                      <tr key={t.id} className="hover:bg-slate-800/40 transition-colors">
                        <td className="px-6 py-4">
                          <div className="font-semibold text-slate-100 text-sm">{t.name}</div>
                          <div className="text-slate-400 text-xs font-mono">{t.slug}</div>
                        </td>
                        <td className="px-6 py-4">
                          <div className="text-slate-200 font-medium">{t.adminFullName || "—"}</div>
                          <div className="text-slate-400 text-xs">{t.adminEmail || "—"}</div>
                        </td>
                        <td className="px-6 py-4">
                          {t.isActive ? (
                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                              <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 mr-1.5 animate-pulse" />
                              Active
                            </span>
                          ) : (
                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-rose-500/10 text-rose-400 border border-rose-500/20">
                              <span className="w-1.5 h-1.5 rounded-full bg-rose-400 mr-1.5" />
                              Suspended
                            </span>
                          )}
                        </td>
                        <td className="px-6 py-4">
                          <button
                            onClick={() => handleInspectTenantSummary(t.id)}
                            className="font-mono text-indigo-400 hover:text-indigo-300 underline underline-offset-2 flex items-center gap-1"
                            title="View Endpoints and Users"
                          >
                            <span>{t.endpointsCount ?? 0}</span>
                            <span className="text-[10px] text-slate-500">details</span>
                          </button>
                        </td>
                        <td className="px-6 py-4 font-mono text-slate-300">{(t.messagesCount ?? 0).toLocaleString()}</td>
                        <td className="px-6 py-4 text-slate-400">
                          {t.createdAt ? new Date(t.createdAt).toLocaleDateString() : "—"}
                        </td>
                        <td className="px-6 py-4 text-right">
                          <div className="flex items-center justify-end gap-2">
                            <button
                              onClick={() => router.push(`/dashboard?inspectTenantId=${t.id}&tenantName=${encodeURIComponent(t.name)}`)}
                              className="px-2.5 py-1.5 rounded-lg bg-indigo-600/20 hover:bg-indigo-600/30 text-indigo-300 border border-indigo-500/30 text-xs font-medium transition-colors"
                              title="Inspect Tenant Dashboard"
                            >
                              Inspect
                            </button>
                            <button
                              onClick={() => setConfirmResetTenant(t)}
                              className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-amber-400 border border-slate-700 text-xs font-medium transition-colors"
                              title="Reset Client Admin Password"
                            >
                              Reset Pass
                            </button>
                            <button
                              onClick={() => setConfirmSuspendTenant(t)}
                              className={`px-2.5 py-1.5 rounded-lg border text-xs font-medium transition-colors ${
                                t.isActive
                                  ? "bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 border-rose-500/30"
                                  : "bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-400 border-emerald-500/30"
                              }`}
                            >
                              {t.isActive ? "Suspend" : "Activate"}
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* Pagination footer */}
            <div className="px-6 py-3 border-t border-slate-800 bg-slate-950/40 flex items-center justify-between text-xs text-slate-400">
              <div>
                Showing <span className="font-semibold text-slate-200">{tenants.length}</span> of{" "}
                <span className="font-semibold text-slate-200">{totalCount}</span> clients
              </div>
              <div className="flex items-center gap-2">
                <button
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="px-3 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  Previous
                </button>
                <span className="text-slate-300 font-medium">Page {page}</span>
                <button
                  disabled={tenants.length < 20 || page * 20 >= totalCount}
                  onClick={() => setPage((p) => p + 1)}
                  className="px-3 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  Next
                </button>
              </div>
            </div>
          </div>
        </section>
      </main>

      {/* ----------------- Onboard Client Modal ----------------- */}
      {showOnboardModal && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg p-6 shadow-2xl space-y-6">
            <div className="flex items-center justify-between border-b border-slate-800 pb-4">
              <h3 className="text-lg font-bold text-white">Onboard New Client</h3>
              <button
                onClick={() => setShowOnboardModal(false)}
                className="text-slate-400 hover:text-white"
              >
                ✕
              </button>
            </div>

            {onboardError && (
              <div className="p-3 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-400 text-xs">
                {onboardError}
              </div>
            )}

            <form onSubmit={handleOnboardSubmit} className="space-y-4 text-xs">
              <div>
                <label className="block font-semibold text-slate-300 uppercase mb-1.5">
                  Tenant Organization Name
                </label>
                <input
                  type="text"
                  required
                  value={onboardingData.name}
                  onChange={(e) => handleSlugAutoFill(e.target.value)}
                  placeholder="e.g. Acme Corporation"
                  className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950/60 border border-slate-800 text-slate-100 placeholder-slate-500 focus:outline-none focus:border-indigo-500 text-sm"
                />
              </div>

              <div>
                <label className="block font-semibold text-slate-300 uppercase mb-1.5">
                  Tenant Slug (Unique identifier)
                </label>
                <input
                  type="text"
                  required
                  value={onboardingData.slug}
                  onChange={(e) =>
                    setOnboardingData((prev) => ({ ...prev, slug: e.target.value.toLowerCase() }))
                  }
                  placeholder="e.g. acme-corp"
                  className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950/60 border border-slate-800 text-slate-100 font-mono placeholder-slate-500 focus:outline-none focus:border-indigo-500 text-sm"
                />
              </div>

              <div>
                <label className="block font-semibold text-slate-300 uppercase mb-1.5">
                  Client Admin Email
                </label>
                <input
                  type="email"
                  required
                  value={onboardingData.adminEmail}
                  onChange={(e) =>
                    setOnboardingData((prev) => ({ ...prev, adminEmail: e.target.value }))
                  }
                  placeholder="admin@acme.com"
                  className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950/60 border border-slate-800 text-slate-100 placeholder-slate-500 focus:outline-none focus:border-indigo-500 text-sm"
                />
              </div>

              <div>
                <label className="block font-semibold text-slate-300 uppercase mb-1.5">
                  Client Admin Full Name
                </label>
                <input
                  type="text"
                  required
                  value={onboardingData.adminFullName}
                  onChange={(e) =>
                    setOnboardingData((prev) => ({ ...prev, adminFullName: e.target.value }))
                  }
                  placeholder="John Doe"
                  className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950/60 border border-slate-800 text-slate-100 placeholder-slate-500 focus:outline-none focus:border-indigo-500 text-sm"
                />
              </div>

              <div className="pt-4 flex items-center justify-end gap-3 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setShowOnboardModal(false)}
                  className="px-4 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 font-semibold"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={onboardLoading}
                  className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold flex items-center gap-2 disabled:opacity-50"
                >
                  {onboardLoading ? "Creating..." : "Create Tenant & Credentials"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ----------------- One-Time Created Credentials Modal ----------------- */}
      {createdCredentials && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-md z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-indigo-500/40 rounded-2xl w-full max-w-xl p-6 shadow-2xl space-y-6">
            <div className="flex items-center gap-3 border-b border-slate-800 pb-4">
              <div className="w-10 h-10 rounded-xl bg-emerald-500/20 text-emerald-400 flex items-center justify-center border border-emerald-500/30">
                ✓
              </div>
              <div>
                <h3 className="text-lg font-bold text-white">Client Onboarded Successfully</h3>
                <p className="text-xs text-slate-400">One-time temporary credentials and ingestion key</p>
              </div>
            </div>

            {/* Critical Security Warning */}
            <div className="p-4 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-400 text-xs flex items-start gap-3">
              <span className="text-lg">⚠️</span>
              <div>
                <span className="font-bold block">Important Security Notice:</span>
                These credentials will only be displayed <strong>ONCE</strong>. Please copy and store them in a secure password manager now before closing this window.
              </div>
            </div>

            <div className="space-y-4 text-xs">
              <div>
                <span className="text-slate-400 block mb-1">Tenant Organization:</span>
                <div className="font-semibold text-slate-100 text-sm">
                  {createdCredentials.name} ({createdCredentials.slug})
                </div>
              </div>

              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className="text-slate-400">Client Admin Email:</span>
                  <button
                    onClick={() => copyToClipboard(createdCredentials.adminEmail, "email")}
                    className="text-indigo-400 hover:text-indigo-300 font-medium"
                  >
                    {copyFeedback === "email" ? "Copied!" : "Copy"}
                  </button>
                </div>
                <div className="px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-800 font-mono text-slate-200 text-xs">
                  {createdCredentials.adminEmail}
                </div>
              </div>

              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className="text-slate-400">One-Time Temporary Password:</span>
                  <button
                    onClick={() => copyToClipboard(createdCredentials.tempPassword, "password")}
                    className="text-emerald-400 hover:text-emerald-300 font-medium"
                  >
                    {copyFeedback === "password" ? "Copied!" : "Copy"}
                  </button>
                </div>
                <div className="px-3.5 py-2 rounded-xl bg-slate-950 border border-emerald-500/40 font-mono text-emerald-400 font-bold text-sm select-all">
                  {createdCredentials.tempPassword}
                </div>
              </div>

              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className="text-slate-400">Default Webhook Ingestion URL & Key:</span>
                  <button
                    onClick={() => copyToClipboard(createdCredentials.webhookUrl, "url")}
                    className="text-cyan-400 hover:text-cyan-300 font-medium"
                  >
                    {copyFeedback === "url" ? "Copied!" : "Copy"}
                  </button>
                </div>
                <div className="px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-800 font-mono text-slate-300 text-xs break-all">
                  {createdCredentials.webhookUrl}
                </div>
              </div>
            </div>

            <div className="pt-4 flex justify-end border-t border-slate-800">
              <button
                onClick={() => setCreatedCredentials(null)}
                className="px-6 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs"
              >
                I have securely saved these credentials
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ----------------- Reset Password Modal ----------------- */}
      {confirmResetTenant && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md p-6 shadow-2xl space-y-4">
            <h3 className="text-lg font-bold text-white">Reset Client Admin Password</h3>
            <p className="text-xs text-slate-300">
              This will generate a new one-time temporary password for <strong>{confirmResetTenant.name}</strong> and immediately revoke all active sessions for this client.
            </p>
            <div className="flex justify-end gap-3 pt-4 border-t border-slate-800">
              <button
                onClick={() => setConfirmResetTenant(null)}
                className="px-4 py-2 rounded-xl bg-slate-800 text-slate-300 text-xs font-semibold"
              >
                Cancel
              </button>
              <button
                onClick={handleResetPassword}
                disabled={actionLoading}
                className="px-4 py-2 rounded-xl bg-amber-600 hover:bg-amber-500 text-white text-xs font-semibold"
              >
                {actionLoading ? "Resetting..." : "Generate New Temp Password"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ----------------- Reset Result Modal ----------------- */}
      {resetCredentials && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-md z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-amber-500/40 rounded-2xl w-full max-w-md p-6 shadow-2xl space-y-5">
            <h3 className="text-lg font-bold text-white">Password Reset Successful</h3>
            <p className="text-xs text-amber-400">
              ⚠️ Save this temporary password now. It will not be shown again.
            </p>
            <div className="space-y-3 text-xs">
              <div>
                <span className="text-slate-400">Client Admin Email:</span>
                <div className="font-mono text-slate-200 mt-1">{resetCredentials.email}</div>
              </div>
              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className="text-slate-400">New Temporary Password:</span>
                  <button
                    onClick={() => copyToClipboard(resetCredentials.tempPassword, "resetpwd")}
                    className="text-emerald-400 hover:text-emerald-300 font-medium"
                  >
                    {copyFeedback === "resetpwd" ? "Copied!" : "Copy"}
                  </button>
                </div>
                <div className="px-3.5 py-2 rounded-xl bg-slate-950 border border-emerald-500/40 font-mono text-emerald-400 font-bold text-sm select-all">
                  {resetCredentials.tempPassword}
                </div>
              </div>
            </div>
            <div className="flex justify-end pt-4 border-t border-slate-800">
              <button
                onClick={() => setResetCredentials(null)}
                className="px-5 py-2 rounded-xl bg-indigo-600 text-white font-semibold text-xs"
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ----------------- Confirm Suspend / Activate Modal ----------------- */}
      {confirmSuspendTenant && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md p-6 shadow-2xl space-y-4">
            <h3 className="text-lg font-bold text-white">
              {confirmSuspendTenant.isActive ? "Suspend Client Tenant" : "Activate Client Tenant"}
            </h3>
            <p className="text-xs text-slate-300">
              Are you sure you want to {confirmSuspendTenant.isActive ? "suspend" : "activate"}{" "}
              <strong>{confirmSuspendTenant.name}</strong>?
              {confirmSuspendTenant.isActive &&
                " All tenant users will immediately lose access and their active sessions will be terminated."}
            </p>
            <div className="flex justify-end gap-3 pt-4 border-t border-slate-800">
              <button
                onClick={() => setConfirmSuspendTenant(null)}
                className="px-4 py-2 rounded-xl bg-slate-800 text-slate-300 text-xs font-semibold"
              >
                Cancel
              </button>
              <button
                onClick={handleToggleStatus}
                disabled={actionLoading}
                className={`px-4 py-2 rounded-xl text-white text-xs font-semibold ${
                  confirmSuspendTenant.isActive ? "bg-rose-600 hover:bg-rose-500" : "bg-emerald-600 hover:bg-emerald-500"
                }`}
              >
                {actionLoading
                  ? "Updating..."
                  : confirmSuspendTenant.isActive
                  ? "Yes, Suspend Client"
                  : "Yes, Activate Client"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ----------------- Tenant Summary & Key Rotation Modal ----------------- */}
      {(inspectSummary || inspectLoading) && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl p-6 shadow-2xl space-y-6 max-h-[90vh] overflow-y-auto">
            {inspectLoading || !inspectSummary ? (
              <div className="py-12 text-center text-slate-400">Loading tenant details...</div>
            ) : (
              <>
                <div className="flex items-center justify-between border-b border-slate-800 pb-4">
                  <div>
                    <h3 className="text-lg font-bold text-white">{inspectSummary.name}</h3>
                    <p className="text-xs font-mono text-slate-400">Slug: {inspectSummary.slug}</p>
                  </div>
                  <button
                    onClick={() => setInspectSummary(null)}
                    className="text-slate-400 hover:text-white"
                  >
                    ✕
                  </button>
                </div>

                {/* Webhook Endpoints */}
                <div className="space-y-3">
                  <h4 className="text-xs font-semibold uppercase tracking-wider text-slate-400">
                    Webhook Endpoints ({inspectSummary.endpoints.length})
                  </h4>
                  <div className="space-y-2">
                    {inspectSummary.endpoints.map((ep) => (
                      <div
                        key={ep.endpointId}
                        className="bg-slate-950/60 border border-slate-800 rounded-xl p-3.5 flex items-center justify-between gap-4 text-xs"
                      >
                        <div>
                          <div className="font-semibold text-slate-200">{ep.name}</div>
                          <div className="text-slate-400 font-mono text-[11px] mt-0.5">
                            Prefix: {ep.keyPrefix} • Status: {ep.status}
                          </div>
                        </div>
                        <button
                          onClick={() => handleRotateWebhookKey(ep.endpointId)}
                          disabled={actionLoading}
                          className="px-3 py-1.5 rounded-lg bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/30 font-semibold transition-colors disabled:opacity-50"
                        >
                          Rotate Key
                        </button>
                      </div>
                    ))}
                  </div>
                </div>

                {/* Tenant Users */}
                <div className="space-y-3">
                  <h4 className="text-xs font-semibold uppercase tracking-wider text-slate-400">
                    Client Users ({inspectSummary.users.length})
                  </h4>
                  <div className="divide-y divide-slate-800/60 bg-slate-950/60 border border-slate-800 rounded-xl overflow-hidden">
                    {inspectSummary.users.map((u) => (
                      <div key={u.userId} className="p-3 flex items-center justify-between text-xs">
                        <div>
                          <span className="font-semibold text-slate-200 block">{u.fullName}</span>
                          <span className="text-slate-400">{u.email}</span>
                        </div>
                        <div className="flex items-center gap-2">
                          <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-300 font-mono text-[11px]">
                            {u.role}
                          </span>
                          {u.mustChangePassword && (
                            <span className="px-2 py-0.5 rounded bg-amber-500/10 text-amber-400 border border-amber-500/20 text-[11px]">
                              Must Change Pass
                            </span>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="flex justify-end pt-4 border-t border-slate-800">
                  <button
                    onClick={() => setInspectSummary(null)}
                    className="px-5 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold"
                  >
                    Close
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* ----------------- Key Rotation Result Modal ----------------- */}
      {rotatedKeyData && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-md z-50 flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-cyan-500/40 rounded-2xl w-full max-w-md p-6 shadow-2xl space-y-5">
            <h3 className="text-lg font-bold text-white">Webhook Key Rotated Successfully</h3>
            <p className="text-xs text-amber-400">
              ⚠️ Save this new plain webhook key now. It will not be shown again.
            </p>
            <div className="space-y-3 text-xs">
              <div>
                <span className="text-slate-400">Key Prefix:</span>
                <div className="font-mono text-slate-200 mt-1">{rotatedKeyData.keyPrefix}</div>
              </div>
              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className="text-slate-400">New Plain Webhook Key:</span>
                  <button
                    onClick={() => copyToClipboard(rotatedKeyData.plainKey, "rotkey")}
                    className="text-cyan-400 hover:text-cyan-300 font-medium"
                  >
                    {copyFeedback === "rotkey" ? "Copied!" : "Copy"}
                  </button>
                </div>
                <div className="px-3.5 py-2 rounded-xl bg-slate-950 border border-cyan-500/40 font-mono text-cyan-400 font-bold text-sm select-all break-all">
                  {rotatedKeyData.plainKey}
                </div>
              </div>
            </div>
            <div className="flex justify-end pt-4 border-t border-slate-800">
              <button
                onClick={() => setRotatedKeyData(null)}
                className="px-5 py-2 rounded-xl bg-indigo-600 text-white font-semibold text-xs"
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
