export default function Home() {
  return (
    <main className="min-h-screen bg-slate-50 flex flex-col items-center justify-center p-6 sm:p-12">
      <div className="w-full max-w-xl bg-white border border-slate-200 rounded-xl shadow-sm p-8 space-y-6">
        <div className="space-y-2">
          <div className="inline-flex items-center gap-2 px-2.5 py-1 rounded-full text-xs font-semibold bg-blue-50 text-blue-700 border border-blue-100">
            Phase 1 Foundation
          </div>
          <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-slate-900">
            ORAI Webhook Manager
          </h1>
          <p className="text-sm sm:text-base text-slate-600">
            Secure multi-tenant WhatsApp webhook status monitoring.
          </p>
        </div>

        <div className="pt-4 border-t border-slate-100 flex items-center justify-between text-sm">
          <span className="text-slate-500 font-medium">Backend Connection:</span>
          <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-amber-50 text-amber-700 border border-amber-200">
            Not configured
          </span>
        </div>
      </div>
    </main>
  );
}
