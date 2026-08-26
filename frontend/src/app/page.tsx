"use client";

import React, { useEffect } from "react";
import { useRouter } from "next/navigation";
import { getCurrentSessionApi } from "../lib/api";
import { OraiLoadingScene } from "../components/OraiLoadingScene";

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
    <OraiLoadingScene
      title="ORAI Webhook Manager"
      subtitle="Preparing your secure workspace…"
    />
  );
}
