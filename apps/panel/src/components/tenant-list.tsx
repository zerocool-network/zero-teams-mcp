"use client";

import { useState, useEffect, useCallback } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Trash2, Shield, ShieldCheck, ShieldAlert, RefreshCw } from "lucide-react";
import { api } from "@/lib/api";
import type { TenantConfig, ConsentRecord } from "@/lib/types";

export function TenantList() {
  const [tenants, setTenants] = useState<TenantConfig[]>([]);
  const [consents, setConsents] = useState<Map<string, ConsentRecord>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const tenantData = await api.listTenants();
      setTenants(tenantData);

      const consentMap = new Map<string, ConsentRecord>();
      await Promise.allSettled(
        tenantData.map(async (t) => {
          try {
            const consent = await api.getConsent(t.tenantId);
            consentMap.set(t.tenantId, consent);
          } catch {}
        })
      );
      setConsents(consentMap);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Error desconocido");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleDelete = async (id: string) => {
    if (!confirm("Eliminar este tenant?")) return;
    try {
      await api.deleteTenant(id);
      fetchData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error al eliminar");
    }
  };

  const handleConsent = async (id: string) => {
    try {
      const result = await api.requestConsent(id);
      const record: ConsentRecord = {
        tenantId: id,
        status: "Pending",
        requestedAt: new Date().toISOString(),
        consentUrl: result.consentUrl,
      };
      setConsents((prev) => new Map(prev).set(id, record));
      if (result.consentUrl) {
        window.open(result.consentUrl, "_blank");
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error al solicitar consent");
    }
  };

  const consentBadge = (status: string) => {
    switch (status) {
      case "Granted":
        return <Badge variant="outline" className="border-green-500 text-green-400"><ShieldCheck className="h-3 w-3 mr-1" />Granted</Badge>;
      case "Pending":
        return <Badge variant="outline" className="border-yellow-500 text-yellow-400"><Shield className="h-3 w-3 mr-1" />Pending</Badge>;
      case "Revoked":
        return <Badge variant="outline" className="border-red-500 text-red-400"><ShieldAlert className="h-3 w-3 mr-1" />Revoked</Badge>;
      case "Failed":
        return <Badge variant="destructive"><ShieldAlert className="h-3 w-3 mr-1" />Failed</Badge>;
      default:
        return <Badge variant="secondary">{status}</Badge>;
    }
  };

  if (loading && tenants.length === 0) {
    return (
      <Card className="glass">
        <CardHeader>
          <CardTitle className="font-display">Tenants</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="space-y-2">
            {[1, 2, 3].map((i) => (
              <div key={i} className="h-12 rounded bg-muted animate-pulse" />
            ))}
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="glass animate-fade-in">
      <CardHeader className="flex flex-row items-center justify-between">
        <div>
          <CardTitle className="font-display">Tenants</CardTitle>
          <CardDescription className="font-mono">{tenants.length} registrado(s)</CardDescription>
        </div>
        <Button variant="ghost" size="icon" onClick={fetchData} disabled={loading}>
          <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
        </Button>
      </CardHeader>
      <CardContent>
        {error && (
          <p className="text-destructive font-mono text-sm mb-4">{error}</p>
        )}
        {tenants.length === 0 ? (
          <p className="text-muted-foreground text-center py-8 font-mono">No hay tenants registrados</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Nombre</TableHead>
                <TableHead>Tenant ID</TableHead>
                <TableHead>Client ID</TableHead>
                <TableHead>Consent</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {tenants.map((tenant) => {
                const consent = consents.get(tenant.tenantId);
                return (
                  <TableRow key={tenant.tenantId}>
                    <TableCell className="font-medium">{tenant.displayName}</TableCell>
                    <TableCell className="text-xs">{tenant.tenantId}</TableCell>
                    <TableCell className="text-xs">{tenant.clientId}</TableCell>
                    <TableCell>
                      {consent ? consentBadge(consent.status) : (
                        <Badge variant="secondary">Sin datos</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex items-center justify-end gap-2">
                        {(!consent || consent.status !== "Granted") && (
                          <Button variant="ghost" size="sm" onClick={() => handleConsent(tenant.tenantId)}>
                            <Shield className="h-4 w-4 mr-1" />Consent
                          </Button>
                        )}
                        <Button variant="ghost" size="icon" onClick={() => handleDelete(tenant.tenantId)}>
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
