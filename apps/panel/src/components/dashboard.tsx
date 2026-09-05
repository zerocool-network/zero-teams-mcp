"use client";

import { useState, useEffect, useCallback } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Server, Users, MessageSquare, Activity, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import type { HealthResponse, TenantConfig, ConsentRecord } from "@/lib/types";

interface StatCard {
  title: string;
  value: string | number;
  icon: React.ElementType;
  color: string;
  description?: string;
}

export function Dashboard() {
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [tenants, setTenants] = useState<TenantConfig[]>([]);
  const [consents, setConsents] = useState<ConsentRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null);

  const fetchData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [healthData, tenantData] = await Promise.all([
        api.health(),
        api.listTenants(),
      ]);
      setHealth(healthData);
      setTenants(tenantData);

      // Fetch consents for all tenants
      const consentResults = await Promise.allSettled(
        tenantData.map((t) => api.getConsent(t.tenantId))
      );
      const validConsents = consentResults
        .filter((r): r is PromiseFulfilledResult<ConsentRecord> => r.status === "fulfilled")
        .map((r) => r.value);
      setConsents(validConsents);

      setLastRefresh(new Date());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error desconocido");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 30000);
    return () => clearInterval(interval);
  }, [fetchData]);

  const stats: StatCard[] = [
    {
      title: "Tenants activos",
      value: tenants.length,
      icon: Server,
      color: "text-neon-cyan",
      description: health?.tenantCount !== undefined ? `ControlPlane: ${health.tenantCount}` : undefined,
    },
    {
      title: "Consents",
      value: consents.filter((c) => c.status === "Granted").length,
      icon: Users,
      color: "text-neon-magenta",
      description: `${consents.length} total`,
    },
    {
      title: "Health",
      value: health?.status === "Healthy" ? "OK" : health?.status ?? "—",
      icon: Activity,
      color: health?.status === "Healthy" ? "text-green-400" : "text-yellow-400",
      description: health?.service,
    },
    {
      title: "Último refresh",
      value: lastRefresh ? lastRefresh.toLocaleTimeString("es-ES") : "—",
      icon: MessageSquare,
      color: "text-muted-foreground",
      description: "Auto-cada 30s",
    },
  ];

  return (
    <div className="space-y-8 animate-fade-in">
      <div className="flex items-center justify-between">
        <div className="space-y-2">
          <h1 className="font-display text-4xl font-bold tracking-tight text-glow-neon">
            ZeroTeams MCP
          </h1>
          <p className="text-muted-foreground font-mono text-sm">
            Panel de control multi-tenant — Microsoft Teams MCP Server
          </p>
        </div>
        <Button
          variant="neon"
          size="sm"
          onClick={fetchData}
          disabled={loading}
        >
          <RefreshCw className={`h-4 w-4 mr-2 ${loading ? "animate-spin" : ""}`} />
          Actualizar
        </Button>
      </div>

      {error && (
        <Card className="glass border-destructive/50">
          <CardContent className="pt-6">
            <p className="text-destructive font-mono text-sm">{error}</p>
          </CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {stats.map((stat) => (
          <Card key={stat.title} className="glass animate-glow-pulse">
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">
                {stat.title}
              </CardTitle>
              <stat.icon className={`h-4 w-4 ${stat.color}`} />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-display font-bold">{stat.value}</div>
              {stat.description && (
                <p className="text-xs text-muted-foreground font-mono mt-1">{stat.description}</p>
              )}
            </CardContent>
          </Card>
        ))}
      </div>

      <Card className="glass">
        <CardHeader>
          <CardTitle className="font-display">Estado del servicio</CardTitle>
          <CardDescription className="font-mono">ControlPlane REST API</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center gap-3">
            <Badge
              variant="outline"
              className={health?.status === "Healthy"
                ? "border-green-500 text-green-400"
                : "border-yellow-500 text-yellow-400"
              }
            >
              {health?.status === "Healthy" ? "En línea" : "Desconocido"}
            </Badge>
            <span className="text-sm text-muted-foreground font-mono">
              http://localhost:5100/health
            </span>
            {health?.timestamp && (
              <span className="text-xs text-muted-foreground font-mono ml-auto">
                {new Date(health.timestamp).toLocaleString("es-ES")}
              </span>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
