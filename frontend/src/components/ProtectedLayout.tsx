"use client";

import React, { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { getCurrentSessionApi } from "../lib/api";
import { AuthSession } from "../types/auth";
import { OraiLoadingScene } from "./OraiLoadingScene";

export interface ProtectedLayoutProps {
  children: React.ReactNode;
  requirePlatformAdmin?: boolean;
  loadingTitle?: string;
  loadingSubtitle?: string;
}

function ProtectedLayoutInternal({
  children,
  requirePlatformAdmin = false,
  loadingTitle = "ORAI Webhook Manager",
  loadingSubtitle = "Verifying session authentication...",
}: ProtectedLayoutProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const inspectTenantId = searchParams?.get("inspectTenantId") || undefined;

  const [isAuthorized, setIsAuthorized] = useState(false);
  const [authChecking, setAuthChecking] = useState(true);

  useEffect(() => {
    let ignore = false;

    async function checkAuth() {
      try {
        const session: AuthSession = await getCurrentSessionApi();
        if (ignore) return;

        if (!session || !session.user) {
          const isDemo = !!process.env.NEXT_PUBLIC_DEMO_TENANT_ID;
          if (isDemo && process.env.NODE_ENV === "development") {
            setIsAuthorized(true);
            setAuthChecking(false);
            return;
          }
          router.replace("/login");
          return;
        }

        if (session.user.mustChangePassword) {
          router.replace("/change-password");
          return;
        }

        if (requirePlatformAdmin && !session.user.isPlatformAdmin) {
          router.replace("/dashboard");
          return;
        }

        // Platform admin navigating to dashboard without inspecting a tenant should go to /admin
        if (!requirePlatformAdmin && session.user.isPlatformAdmin && !inspectTenantId && !session.tenant) {
          router.replace("/admin");
          return;
        }

        setIsAuthorized(true);
        setAuthChecking(false);
      } catch {
        if (ignore) return;
        const isDemo = !!process.env.NEXT_PUBLIC_DEMO_TENANT_ID;
        if (isDemo && process.env.NODE_ENV === "development") {
          setIsAuthorized(true);
          setAuthChecking(false);
        } else {
          router.replace("/login");
        }
      }
    }

    checkAuth();

    return () => {
      ignore = true;
    };
  }, [router, requirePlatformAdmin, inspectTenantId]);

  if (authChecking || !isAuthorized) {
    return (
      <OraiLoadingScene
        title={loadingTitle}
        subtitle={loadingSubtitle}
      />
    );
  }

  return <>{children}</>;
}

export function ProtectedLayout(props: ProtectedLayoutProps) {
  return (
    <Suspense
      fallback={
        <OraiLoadingScene
          title={props.loadingTitle || "ORAI Webhook Manager"}
          subtitle={props.loadingSubtitle || "Verifying session authentication..."}
        />
      }
    >
      <ProtectedLayoutInternal {...props} />
    </Suspense>
  );
}
