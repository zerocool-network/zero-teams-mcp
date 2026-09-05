"use client";

import { useState, useEffect, useCallback } from "react";
import { Sidebar } from "@/components/sidebar";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { RefreshCw, Server, Database, Cpu, Clock } from "lucide-react";
import { api } from "@/lib/api";
import type { HealthResponse } from "@/lib/types";

export default function HealthPage() {
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchHealth = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setHealth(await api.health());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchHealth(); const i = setInterval(fetchHealth, 15000); return () => clearInterval(i); }, [fetchHealth]);

  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="font-display text-3xl font-bold tracking-tight text-glow-neon">Health</h1>
            <p className="text-muted-foreground font-mono text-sm mt-1">Estado del ControlPlane y servicios</p>
          </div>
          <Button variant="ghost" size="sm" onClick={fetchHealth} disabled={loading}>
            <RefreshCw className={`h-4 w-4 mr-2 ${loading ? "animate-spin" : ""}`} />Actualizar
          </Button>
        </div>

        {error && (
          <Card className="glass border-destructive/50 mb-6">
            <CardContent className="pt-6">
              <p className="text-destructive font-mono text-sm">{error}</p>
            </CardContent>
          </Card>
        )}

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
          <Card className="glass animate-glow-pulse">
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Estado</CardTitle>
              <Server className="h-4 w-4 text-neon-cyan" />
            </CardHeader>
            <CardContent>
              <Badge variant="outline" className={health?.status === "Healthy" ? "border-green-500 text-green-400" : "border-yellow-500 text-yellow-400"}>
                {health?.status ?? "—"}
              </Badge>
            </CardContent>
          </Card>
          <Card className="glass animate-glow-pulse">
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Servicio</CardTitle>
              <Cpu className="h-4 w-4 text-neon-magenta" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-display font-bold">{health?.service ?? "—"}</div>
            </CardContent>
          </Card>
          <Card className="glass animate-glow-pulse">
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Tenants</CardTitle>
              <Database className="h-4 w-4 text-primary" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-display font-bold">{health?.tenantCount ?? 0}</div>
              <p className="text-xs text-muted-foreground font-mono mt-1">{health?.consentCount ?? 0} consents</p>
            </CardContent>
          </Card>
          <Card className="glass animate-glow-pulse">
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">Timestamp</CardTitle>
              <Clock className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-sm font-mono">{health?.timestamp ? new Date(health.timestamp).toLocaleString("es-ES") : "—"}</div>
            </CardContent>
          </Card>
        </div>

        <Card className="glass">
          <CardHeader>
            <CardTitle className="font-display">Endpoints</CardTitle>
            <CardDescription className="font-mono">URLs de servicios</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="flex items-center justify-between py-2 border-b border-border/30">
              <span className="text-sm font-mono text-muted-foreground">ControlPlane</span>
              <span className="text-sm font-mono">http://localhost:5100</span>
            </div>
            <div className="flex items-center justify-between py-2 border-b border-border/30">
              <span className="text-sm font-mono text-muted-foreground">MCP Server</span>
              <span className="text-sm font-mono">{health?.mcpServerUrl ?? "http://localhost:5200"}</span>
            </div>
            <div className="flex items-center justify-between py-2">
              <span className="text-sm font-mono text-muted-foreground">Panel</span>
              <span className="text-sm font-mono">{health?.panelUrl ?? "http://localhost:3000"}</span>
            </div>
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
