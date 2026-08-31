"use client";

import React from "react";
import { ProtectedLayout } from "../../components/ProtectedLayout";

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <ProtectedLayout
      loadingTitle="ORAI Webhook Manager"
      loadingSubtitle="Verifying session authentication..."
    >
      {children}
    </ProtectedLayout>
  );
}
