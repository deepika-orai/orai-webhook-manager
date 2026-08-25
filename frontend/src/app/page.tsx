"use client";

import React, { useEffect } from "react";
import { useRouter } from "next/navigation";
import { getCurrentSessionApi } from "../lib/api";

export default function RootPage() {
  const router = useRouter();

  useEffect(() => {
    async function checkAuthAndRoute() {
      try {
        const session = await getCurrentSessionApi();
        if (session?.user?.mustChangePassword) {
          router.replace("/change-password");
        } else if (session?.user?.isPlatformAdmin) {
          router.replace("/admin");
        } else if (session?.tenant) {
          router.replace("/dashboard");
        } else {
          router.replace("/login");
        }
      } catch {
        const isDemo = !!process.env.NEXT_PUBLIC_DEMO_TENANT_ID;
        if (isDemo && process.env.NODE_ENV === "development") {
          router.replace("/dashboard");
        } else {
          router.replace("/login");
        }
      }
    }

    checkAuthAndRoute();
  }, [router]);

  return (
    <div className="min-h-screen bg-[#0B0F19] flex items-center justify-center text-slate-400">
      <div className="flex flex-col items-center gap-3">
        <svg className="animate-spin w-8 h-8 text-indigo-500" fill="none" viewBox="0 0 24 24">
          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
        </svg>
        <span className="text-xs font-medium tracking-wide">Loading ORAI Webhook Manager...</span>
      </div>
    </div>
  );
}
