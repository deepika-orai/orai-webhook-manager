"use client";

import React from "react";
import { ProtectedLayout } from "../../components/ProtectedLayout";

export default function AdminLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <ProtectedLayout
      requirePlatformAdmin
      loadingTitle="Super Admin Authorization"
      loadingSubtitle="Verifying cryptographic token & platform privileges..."
    >
      {children}
    </ProtectedLayout>
  );
}
