"use client";

import { Sidebar } from "@/components/sidebar";
import { TenantList } from "@/components/tenant-list";
import { AddTenantDialog } from "@/components/add-tenant-dialog";

export default function TenantsPage() {
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="font-display text-3xl font-bold tracking-tight text-glow-neon">
              Tenants
            </h1>
            <p className="text-muted-foreground font-mono text-sm mt-1">
              Gestionar suscripciones de Microsoft Teams
            </p>
          </div>
          <AddTenantDialog />
        </div>
        <TenantList />
      </main>
    </div>
  );
}
