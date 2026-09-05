"use client";

import { useState, useEffect, useCallback } from "react";
import { Sidebar } from "@/components/sidebar";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { RefreshCw, Shield, ShieldCheck, ShieldAlert } from "lucide-react";
import { api } from "@/lib/api";
import type { ConsentRecord, TenantConfig } from "@/lib/types";

export default function ConsentsPage() {
  const [tenants, setTenants] = useState<TenantConfig[]>([]);
  const [consents, setConsents] = useState<ConsentRecord[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const tenantData = await api.listTenants();
      setTenants(tenantData);
      const results = await Promise.allSettled(
        tenantData.map((t) => api.getConsent(t.tenantId))
      );
      setConsents(
        results
          .filter((r): r is PromiseFulfilledResult<ConsentRecord> => r.status === "fulfilled")
          .map((r) => r.value)
      );
    } catch {} finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);

  const handleConsent = async (id: string) => {
    try {
      const result = await api.requestConsent(id);
      setConsents((prev) => {
        const map = new Map(prev.map((c) => [c.tenantId, c]));
        map.set(id, { ...map.get(id), tenantId: id, status: "Pending", requestedAt: new Date().toISOString(), consentUrl: result.consentUrl } as ConsentRecord);
        return Array.from(map.values());
      });
      if (result.consentUrl) window.open(result.consentUrl, "_blank");
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error");
    }
  };

  const consentBadge = (status: string) => {
    switch (status) {
      case "Granted": return <Badge variant="outline" className="border-green-500 text-green-400"><ShieldCheck className="h-3 w-3 mr-1" />Granted</Badge>;
      case "Pending": return <Badge variant="outline" className="border-yellow-500 text-yellow-400"><Shield className="h-3 w-3 mr-1" />Pending</Badge>;
      case "Revoked": return <Badge variant="outline" className="border-red-500 text-red-400"><ShieldAlert className="h-3 w-3 mr-1" />Revoked</Badge>;
      case "Failed": return <Badge variant="destructive"><ShieldAlert className="h-3 w-3 mr-1" />Failed</Badge>;
      default: return <Badge variant="secondary">{status}</Badge>;
    }
  };

  const tenantName = (id: string) => tenants.find((t) => t.tenantId === id)?.displayName ?? id;

  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="font-display text-3xl font-bold tracking-tight text-glow-neon">Consents</h1>
            <p className="text-muted-foreground font-mono text-sm mt-1">Estado de consentimiento administrativo de Entra ID</p>
          </div>
          <Button variant="ghost" size="sm" onClick={fetchData} disabled={loading}>
            <RefreshCw className={`h-4 w-4 mr-2 ${loading ? "animate-spin" : ""}`} />Actualizar
          </Button>
        </div>
        <Card className="glass animate-fade-in">
          <CardContent className="pt-6">
            {consents.length === 0 ? (
              <p className="text-muted-foreground text-center py-8 font-mono">No hay consents registrados</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Tenant</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead>Solicitado</TableHead>
                    <TableHead>Concedido</TableHead>
                    <TableHead>Consent URL</TableHead>
                    <TableHead className="text-right">Acciones</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {consents.map((c) => (
                    <TableRow key={c.tenantId}>
                      <TableCell className="font-medium">{tenantName(c.tenantId)}</TableCell>
                      <TableCell>{consentBadge(c.status)}</TableCell>
                      <TableCell className="text-xs">{new Date(c.requestedAt).toLocaleString("es-ES")}</TableCell>
                      <TableCell className="text-xs">{c.grantedAt ? new Date(c.grantedAt).toLocaleString("es-ES") : "—"}</TableCell>
                      <TableCell className="text-xs max-w-[200px] truncate">
                        {c.consentUrl ? (
                          <a href={c.consentUrl} target="_blank" rel="noopener noreferrer" className="text-cyan-400 hover:underline" title={c.consentUrl}>
                            Abrir consent
                          </a>
                        ) : "—"}
                      </TableCell>
                      <TableCell className="text-right">
                        {c.status !== "Granted" && (
                          <Button variant="ghost" size="sm" onClick={() => handleConsent(c.tenantId)}>
                            <Shield className="h-4 w-4 mr-1" />Re-solicitar
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
