"use client";

import { useCallback, useEffect, useState } from "react";
import { Sidebar } from "@/components/sidebar";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Check, Copy, Eye, EyeOff, KeyRound, Loader2 } from "lucide-react";

const CONTROLPLANE_URL = process.env.NEXT_PUBLIC_CONTROLPLANE_URL ?? "http://localhost:5100";

interface Connection {
  email: string;
  panelIsAdmin: boolean;
  apiKey: string | null;
  keyPrefix: string;
  keyCreated: boolean;
  createdAt: string;
  serverUrl: string;
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button
      variant="outline"
      size="sm"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(text);
          setCopied(true);
          setTimeout(() => setCopied(false), 1500);
        } catch {
          // clipboard bloqueado por el navegador
        }
      }}
    >
      {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
      {copied ? "Copiado" : "Copiar"}
    </Button>
  );
}

function Snippet({ title, description, code }: { title: string; description: string; code: string }) {
  return (
    <Card className="border-border/50 bg-card/50 backdrop-blur-xl">
      <CardHeader className="pb-3">
        <div className="flex items-center justify-between gap-3">
          <div>
            <CardTitle className="font-display text-base">{title}</CardTitle>
            <CardDescription className="text-xs">{description}</CardDescription>
          </div>
          <CopyButton text={code} />
        </div>
      </CardHeader>
      <CardContent>
        <pre className="overflow-x-auto rounded-lg border border-border/50 bg-muted/30 p-4 text-xs font-mono">
          {code}
        </pre>
      </CardContent>
    </Card>
  );
}

export default function ConnectionPage() {
  const [conn, setConn] = useState<Connection | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showKey, setShowKey] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch(`${CONTROLPLANE_URL}/api/session/connection`, {
        credentials: "same-origin",
        cache: "no-store",
      });
      if (res.status === 401) {
        window.location.href = `${process.env.NEXT_PUBLIC_BASE_PATH ?? ""}/login`;
        return;
      }
      if (!res.ok) throw new Error(`API ${res.status}`);
      setConn(await res.json());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error desconocido");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const key = conn?.apiKey ?? "";
  const claudeSnippet = JSON.stringify(
    { mcpServers: { teams: { url: conn?.serverUrl ?? "", headers: { "X-Api-Key": key } } } },
    null,
    2
  );
  const opencodeSnippet = JSON.stringify(
    {
      $schema: "https://opencode.ai/config.json",
      mcp: {
        teams: {
          type: "remote",
          url: conn?.serverUrl ?? "",
          enabled: true,
          headers: { "X-Api-Key": key },
        },
      },
    },
    null,
    2
  );
  const curlSnippet = `curl -X POST ${conn?.serverUrl ?? ""} \\
  -H "X-Api-Key: ${key}" \\
  -H "Content-Type: application/json" \\
  -H "Accept: application/json, text/event-stream" \\
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'`;

  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <main className="flex-1 ml-64 p-8">
        <div className="mb-8">
          <h1 className="font-display text-2xl font-bold">Mi conexión</h1>
          <p className="text-sm text-muted-foreground">
            Tu API key y la configuración para usar el servidor MCP de Teams desde Claude, OpenCode u otro cliente.
          </p>
        </div>

        {loading ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando…
          </div>
        ) : error ? (
          <Card className="border-destructive/40 bg-destructive/5">
            <CardContent className="pt-6 text-sm text-destructive">No se pudo cargar la conexión: {error}</CardContent>
          </Card>
        ) : conn ? (
          <div className="space-y-6">
            <Card className="border-border/50 bg-card/50 backdrop-blur-xl">
              <CardHeader>
                <CardTitle className="font-display text-base flex items-center gap-2">
                  <KeyRound className="h-4 w-4 text-neon-cyan" /> API key
                </CardTitle>
                <CardDescription>
                  {conn.email}
                  {conn.panelIsAdmin ? " · admin" : ""} · {conn.keyPrefix}…
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {conn.apiKey ? (
                  <div className="flex items-center gap-2">
                    <code className="flex-1 overflow-x-auto rounded-lg border border-border/50 bg-muted/30 px-3 py-2 text-xs font-mono">
                      {showKey ? conn.apiKey : `${conn.apiKey.slice(0, 10)}${"•".repeat(24)}`}
                    </code>
                    <Button variant="outline" size="sm" onClick={() => setShowKey((v) => !v)}>
                      {showKey ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                      {showKey ? "Ocultar" : "Mostrar"}
                    </Button>
                    <CopyButton text={conn.apiKey} />
                  </div>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    Tu clave es anterior a esta función y no se puede mostrar. Pide al operador que la regenere.
                  </p>
                )}
                <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                  <Badge variant="secondary">{conn.panelIsAdmin ? "Acceso admin del panel" : "Cliente (lectura)"}</Badge>
                  {conn.keyCreated ? <Badge variant="secondary">Creada ahora</Badge> : null}
                  <span className="font-mono">{new Date(conn.createdAt).toLocaleString()}</span>
                </div>
                <p className="text-xs text-muted-foreground">
                  Con esta key puedes leer tus chats, canales, miembros y calendario. Las operaciones de escritura
                  requieren una key de administrador (la emite el operador por CLI).
                </p>
              </CardContent>
            </Card>

            <Snippet
              title="Claude Desktop"
              description="Pega este bloque en claude_desktop_config.json y reinicia Claude."
              code={claudeSnippet}
            />
            <Snippet
              title="OpenCode"
              description="Pega este bloque en tu opencode.json."
              code={opencodeSnippet}
            />
            <Snippet
              title="cURL"
              description="Prueba rápida de conectividad con la API key."
              code={curlSnippet}
            />
          </div>
        ) : null}
      </main>
    </div>
  );
}
