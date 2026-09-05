"use client";

import { Sidebar } from "@/components/sidebar";
import { Dashboard } from "@/components/dashboard";
import { AddTenantDialog } from "@/components/add-tenant-dialog";

export default function Home() {
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="flex items-center justify-between mb-8">
          <div />
          <AddTenantDialog />
        </div>
        <Dashboard />
      </main>
    </div>
  );
}
