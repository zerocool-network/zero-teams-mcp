"use client";

import { useState, useEffect, useCallback } from "react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import {
  LineChart,
  Line,
  AreaChart,
  Area,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from "recharts";
import { RefreshCw, Download, ExternalLink } from "lucide-react";
import { Button } from "@/components/ui/button";

interface MetricDataPoint {
  timestamp: number;
  [key: string]: number | string;
}

interface MetricsConfig {
  timeRange: "5m" | "15m" | "1h" | "6h" | "24h";
  refreshInterval: number;
}

const METRIC_LABELS: Record<string, string> = {
  process_cpu_seconds_total: "CPU (seconds)",
  process_memory_bytes: "Memory (bytes)",
  process_virtual_memory_bytes: "Virtual Memory (bytes)",
  dotnet_gc_heap_size_bytes: "GC Heap Size (bytes)",
  dotnet_gc_gen0_collections_total: "Gen 0 Collections",
  dotnet_gc_gen1_collections_total: "Gen 1 Collections",
  dotnet_gc_gen2_collections_total: "Gen 2 Collections",
  dotnet_thread_count_total: "Thread Count",
};

const METRIC_GROUPS: Record<string, string[]> = {
  System: [
    "process_cpu_seconds_total",
    "process_memory_bytes",
    "process_virtual_memory_bytes",
  ],
  GC: [
    "dotnet_gc_heap_size_bytes",
    "dotnet_gc_gen0_collections_total",
    "dotnet_gc_gen1_collections_total",
    "dotnet_gc_gen2_collections_total",
  ],
  Runtime: [
    "dotnet_thread_count_total",
  ],
};

export function MetricsCharts() {
  const [metrics, setMetrics] = useState<MetricDataPoint[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [config, setConfig] = useState<MetricsConfig>({
    timeRange: "15m",
    refreshInterval: 30000,
  });
  const [selectedGroup, setSelectedGroup] = useState<keyof typeof METRIC_GROUPS>("System");
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null);

  const CONTROLPLANE_URL = process.env.NEXT_PUBLIC_CONTROLPLANE_URL ?? "http://localhost:5100";

  const getRangeMs = (range: string): number => {
    switch (range) {
      case "5m": return 5 * 60 * 1000;
      case "15m": return 15 * 60 * 1000;
      case "1h": return 60 * 60 * 1000;
      case "6h": return 6 * 60 * 60 * 1000;
      case "24h": return 24 * 60 * 60 * 1000;
      default: return 15 * 60 * 1000;
    }
  };

  const parseMetrics = useCallback((text: string): MetricDataPoint[] => {
    const lines = text.split("\n");
    const data: Record<number, MetricDataPoint> = {};
    const now = Date.now();
    const rangeMs = getRangeMs(config.timeRange);
    const cutoff = now - rangeMs;

    for (const line of lines) {
      if (line.startsWith("#") || !line.trim()) continue;
      const parts = line.split(" ");
      if (parts.length < 2) continue;
      const metricName = parts[0];
      const value = parseFloat(parts[1]);
      if (isNaN(value)) continue;

      const timestamp = now - Math.random() * rangeMs;
      if (timestamp < cutoff) continue;

      if (!data[timestamp]) {
        data[timestamp] = { timestamp };
      }
      data[timestamp][metricName] = value;
    }

    return Object.values(data)
      .sort((a, b) => a.timestamp - b.timestamp)
      .slice(-200);
  }, [config.timeRange]);

  const fetchMetrics = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await fetch(`${CONTROLPLANE_URL}/metrics`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const text = await res.text();
      const parsed = parseMetrics(text);
      setMetrics(parsed);
      setLastRefresh(new Date());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error desconocido");
    } finally {
      setLoading(false);
    }
  }, [config.timeRange, parseMetrics]);

  useEffect(() => {
    fetchMetrics();
    const interval = setInterval(fetchMetrics, config.refreshInterval);
    return () => clearInterval(interval);
  }, [fetchMetrics, config.refreshInterval]);

  const currentMetrics = (METRIC_GROUPS[selectedGroup] ?? []) as string[];
  const chartData = metrics.map((d) => {
    const time = new Date(d.timestamp).toLocaleTimeString("es-ES", { hour12: false });
    const obj: Record<string, number | string> = { time };
    for (const m of currentMetrics) {
      obj[m] = d[m] ?? 0;
    }
    return obj;
  });

  return (
    <div className="space-y-6 animate-fade-in">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h2 className="font-display text-2xl font-bold">Métricas del Sistema</h2>
          <p className="text-muted-foreground font-mono text-sm">
            Métricas Prometheus desde ControlPlane — Auto-refresh cada {config.refreshInterval / 1000}s
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Select value={config.timeRange} onValueChange={(v) => setConfig({ ...config, timeRange: v as any })}>
            <SelectTrigger className="bg-muted/30 border-border/50 w-36">
              <SelectValue placeholder="Rango" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="5m">5 min</SelectItem>
              <SelectItem value="15m">15 min</SelectItem>
              <SelectItem value="1h">1 hora</SelectItem>
              <SelectItem value="6h">6 horas</SelectItem>
              <SelectItem value="24h">24 horas</SelectItem>
            </SelectContent>
          </Select>
          <Select value={selectedGroup} onValueChange={(v) => setSelectedGroup(v as any)}>
            <SelectTrigger className="bg-muted/30 border-border/50 w-36">
              <SelectValue placeholder="Grupo" />
            </SelectTrigger>
            <SelectContent>
              {Object.keys(METRIC_GROUPS).map((g) => (
                <SelectItem key={g} value={g}>{g}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button variant="outline" size="sm" onClick={fetchMetrics} disabled={loading}>
            <RefreshCw className={`h-4 w-4 mr-2 ${loading ? "animate-spin" : ""}`} />
            Actualizar
          </Button>
          <a href={`${CONTROLPLANE_URL}/metrics`} target="_blank" rel="noopener noreferrer">
            <Button variant="ghost" size="icon">
              <ExternalLink className="h-4 w-4" />
            </Button>
          </a>
        </div>
      </div>

      {error && (
        <Card className="glass border-destructive/50">
          <CardContent className="pt-6">
            <p className="text-destructive font-mono text-sm">{error}</p>
          </CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {currentMetrics.map((metricName) => {
          const metricKey = metricName;
          const label = METRIC_LABELS[metricKey] ?? metricKey;

          if (chartData.length === 0 || chartData.every((d) => d[metricKey] === 0)) {
            return (
              <Card key={metricName} className="glass">
                <CardHeader>
                  <CardTitle className="font-display">{label}</CardTitle>
                  <CardDescription className="font-mono">Sin datos disponibles</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="h-64 flex items-center justify-center">
                    <p className="text-muted-foreground">No hay datos para este rango</p>
                  </div>
                </CardContent>
              </Card>
            );
          }

          const isCounter = metricName.includes("_total");
          const ChartComponent = isCounter ? AreaChart : LineChart;
          const ChartSeries = isCounter ? Area : Line;

          return (
            <Card key={metricName} className="glass">
              <CardHeader>
                <CardTitle className="font-display">{label}</CardTitle>
                <CardDescription className="font-mono">{metricKey}</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="h-64">
                  <ResponsiveContainer width="100%" height="100%">
                    <ChartComponent data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                      <CartesianGrid strokeDasharray="3 3" className="stroke-border/20" />
                      <XAxis
                        dataKey="time"
                        className="text-xs"
                        tick={{ fill: "hsl(var(--muted-foreground))", fontSize: 10 }}
                        tickLine={false}
                        axisLine={false}
                        tickCount={8}
                      />
                      <YAxis
                        className="text-xs"
                        tick={{ fill: "hsl(var(--muted-foreground))", fontSize: 10 }}
                        tickLine={false}
                        axisLine={false}
                        tickFormatter={(value: number | undefined) => value !== undefined ? (value >= 1e6 ? `${(value / 1e6).toFixed(1)}M` : value >= 1e3 ? `${(value / 1e3).toFixed(1)}K` : String(value)) : "-"}
                      />
                      <Tooltip
                        contentStyle={{
                          backgroundColor: "hsl(var(--card))",
                          border: "1px solid hsl(var(--border))",
                          borderRadius: "8px",
                        }}
                        labelStyle={{ color: "hsl(var(--foreground))" }}
                        formatter={(value: any) => value !== undefined ? [typeof value === "number" ? value.toLocaleString() : String(value), ""] : ["-", ""]}
                      />
                      <Legend />
                      <ChartSeries
                        type="monotone"
                        dataKey={metricKey}
                        stroke="hsl(var(--neon-cyan))"
                        fill="hsl(var(--neon-cyan))"
                        fillOpacity={0.1}
                        strokeWidth={2}
                        dot={false}
                        activeDot={{ r: 6, strokeWidth: 2 }}
                      />
                    </ChartComponent>
                  </ResponsiveContainer>
                </div>
              </CardContent>
            </Card>
          );
        })}
      </div>

      {lastRefresh && (
        <p className="text-xs text-muted-foreground font-mono text-right">
          Última actualización: {lastRefresh.toLocaleTimeString("es-ES")}
        </p>
      )}
    </div>
  );
}