"use client";

import { Sidebar } from "@/components/sidebar";
import { MetricsCharts } from "@/components/metrics-charts";

export default function MetricsPage() {
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <MetricsCharts />
      </main>
    </div>
  );
}