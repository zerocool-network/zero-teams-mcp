"use client";

import { useState, useEffect, useCallback, useMemo } from "react";
import { useAuth } from "@/components/auth-provider";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Play, Loader2, Copy, Check, Search, ChevronLeft, ChevronRight, Zap, Users, Settings, ChevronDown, ChevronUp } from "lucide-react";

const MCP_URL = process.env.NEXT_PUBLIC_MCP_URL ?? "http://localhost:5200";

interface McpToolParam {
  type: string;
  description?: string;
  default?: unknown;
  items?: { type: string };
}

interface McpTool {
  name: string;
  description?: string;
  inputSchema?: {
    type: string;
    properties?: Record<string, McpToolParam>;
    required?: string[];
  };
}

interface ToolResult {
  success: boolean;
  data?: unknown;
  error?: string;
  duration?: number;
}

const READ_TOOLS = new Set([
  "teams_list_teams", "teams_list_channels", "teams_list_chats",
  "teams_get_chat_messages", "teams_get_chat_members", "teams_get_channel_members",
  "teams_list_meetings", "teams_get_meeting", "teams_get_meeting_attendance",
  "teams_list_calendar_events", "teams_get_calendar_event",
]);

const WRITE_TOOLS = new Set([
  "teams_create_channel", "teams_create_chat", "teams_send_message",
  "teams_send_channel_message", "teams_add_chat_member", "teams_remove_chat_member",
  "teams_create_meeting", "teams_delete_meeting",
  "teams_create_calendar_event", "teams_delete_calendar_event",
]);

const HIDDEN_TOOLS = new Set([
  "admin_health", "admin_list_tenants", "admin_get_tenant",
  "admin_create_tenant", "admin_delete_tenant", "admin_get_consent",
]);

const TOOL_DESCRIPTIONS: Record<string, string> = {
  teams_list_teams: "Lista todos los teams del tenant",
  teams_list_channels: "Lista canales de un team",
  teams_create_channel: "Crea un canal en un team",
  teams_list_chats: "Lista chats de un usuario",
  teams_create_chat: "Crea un chat (oneOnOne o group)",
  teams_send_message: "Envía mensaje a un chat",
  teams_send_channel_message: "Envía mensaje a un canal",
  teams_get_chat_messages: "Obtiene mensajes de un chat",
  teams_get_chat_members: "Lista miembros de un chat",
  teams_add_chat_member: "Agrega miembro a un chat",
  teams_remove_chat_member: "Elimina miembro de un chat",
  teams_get_channel_members: "Lista miembros de un canal",
  admin_health: "Health check del ControlPlane",
  admin_list_tenants: "Lista todos los tenants registrados",
  admin_get_tenant: "Obtiene detalles de un tenant",
  admin_create_tenant: "Registra un nuevo tenant",
  admin_delete_tenant: "Elimina un tenant",
  admin_get_consent: "Obtiene estado de consent de un tenant",
  teams_list_calendar_events: "Lista eventos del calendario",
  teams_get_calendar_event: "Obtiene un evento específico",
  teams_create_calendar_event: "Crea un evento en el calendario",
  teams_delete_calendar_event: "Elimina un evento del calendario",
  teams_list_meetings: "Lista reuniones online de un usuario",
  teams_get_meeting: "Obtiene detalles de una reunión",
  teams_create_meeting: "Crea una reunión online",
  teams_delete_meeting: "Elimina una reunión online",
  teams_get_meeting_attendance: "Obtiene asistencia de una reunión",
};

const TOOL_PRESETS: Record<string, string> = {
  teams_list_teams: "{}",
  admin_health: "{}",
  admin_list_tenants: "{}",
  admin_get_tenant: '{ "tenantId": "" }',
  admin_create_tenant: '{ "tenantId": "", "clientId": "", "clientSecret": "", "displayName": "" }',
  admin_delete_tenant: '{ "tenantId": "" }',
  admin_get_consent: '{ "tenantId": "" }',
  teams_list_channels: '{ "teamId": "" }',
  teams_create_channel: '{ "teamId": "", "displayName": "" }',
  teams_list_chats: '{ "userId": "", "top": 5 }',
  teams_create_chat: '{ "chatType": "oneOnOne", "memberIds": "" }',
  teams_send_message: '{ "chatId": "", "content": "" }',
  teams_send_channel_message: '{ "teamId": "", "channelId": "", "content": "" }',
  teams_get_chat_messages: '{ "chatId": "", "top": 10 }',
  teams_get_chat_members: '{ "chatId": "" }',
  teams_add_chat_member: '{ "chatId": "", "userId": "" }',
  teams_remove_chat_member: '{ "chatId": "", "memberId": "" }',
  teams_get_channel_members: '{ "teamId": "", "channelId": "" }',
  teams_list_calendar_events: '{ "userId": "", "startDateTime": "", "endDateTime": "", "top": 10 }',
  teams_get_calendar_event: '{ "userId": "", "eventId": "" }',
  teams_create_calendar_event: '{ "userId": "", "subject": "", "startDateTime": "", "endDateTime": "" }',
  teams_delete_calendar_event: '{ "userId": "", "eventId": "" }',
  teams_list_meetings: '{ "userId": "", "top": 10 }',
  teams_get_meeting: '{ "userId": "", "meetingId": "" }',
  teams_create_meeting: '{ "userId": "", "subject": "", "startDateTime": "", "endDateTime": "" }',
  teams_delete_meeting: '{ "userId": "", "meetingId": "" }',
  teams_get_meeting_attendance: '{ "userId": "", "meetingId": "" }',
};

const TOOLS_PER_PAGE = 6;

function getCategoryForTool(name: string): string {
  if (READ_TOOLS.has(name)) return "Read";
  if (WRITE_TOOLS.has(name)) return "Write";
  return "Other";
}

function isToolAvailable(name: string, isLoggedIn: boolean): boolean {
  if (HIDDEN_TOOLS.has(name)) return false;
  if (READ_TOOLS.has(name)) return true;
  if (WRITE_TOOLS.has(name)) return isLoggedIn;
  return false;
}

function parseSse(body: string): unknown | null {
  let text = body;
  if (body.startsWith("event:")) {
    for (const line of body.split("\n")) {
      if (line.startsWith("data:")) { text = line.slice(5).trim(); break; }
    }
  }
  try { return JSON.parse(text); } catch { return null; }
}

export function Playground() {
  const { user } = useAuth();
  const [tools, setTools] = useState<McpTool[]>([]);
  const [selected, setSelected] = useState<string>("");
  const [args, setArgs] = useState("{}");
  const [result, setResult] = useState<ToolResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [copied, setCopied] = useState(false);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(0);
  const [expandedTool, setExpandedTool] = useState<string | null>(null);
  const [toolsLoading, setToolsLoading] = useState(true);

  useEffect(() => {
    setToolsLoading(true);
    fetch(`${MCP_URL}/mcp`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json, text/event-stream",
      },
      body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/list", params: {} }),
    })
      .then((r) => r.text())
      .then((body) => {
        const json = parseSse(body) as { result?: { tools?: McpTool[] } } | null;
        if (json?.result?.tools) {
          setTools(json.result.tools.map((t: McpTool) => ({
            ...t,
            description: t.description || TOOL_DESCRIPTIONS[t.name] || "",
          })));
        }
      })
      .catch(() => {})
      .finally(() => setToolsLoading(false));
  }, []);

  const filteredTools = useMemo(() => {
    return tools.filter((t) => {
      if (!isToolAvailable(t.name, !!user)) return false;
      if (!search) return true;
      const q = search.toLowerCase();
      return t.name.toLowerCase().includes(q) || getCategoryForTool(t.name).toLowerCase().includes(q);
    });
  }, [tools, search, user]);

  const totalPages = Math.ceil(filteredTools.length / TOOLS_PER_PAGE);
  const pagedTools = filteredTools.slice(page * TOOLS_PER_PAGE, (page + 1) * TOOLS_PER_PAGE);

  useEffect(() => {
    setPage(0);
  }, [search]);

  const runTool = useCallback(async () => {
    if (!selected) return;
    setLoading(true);
    setResult(null);
    const start = Date.now();
    try {
      const parsed = JSON.parse(args || "{}");
      if (user?.userId && !parsed.authenticatedUserId) {
        parsed.authenticatedUserId = user.userId;
      }
      const resp = await fetch(`${MCP_URL}/mcp`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json, text/event-stream",
        },
        body: JSON.stringify({ jsonrpc: "2.0", id: Date.now(), method: "tools/call", params: { name: selected, arguments: parsed } }),
      });
      const body = await resp.text();
      const json = parseSse(body) as { result?: { content?: { text: string }[] }; error?: unknown } | null;
      const duration = Date.now() - start;

      if (json?.result?.content?.[0]?.text) {
        try {
          const parsed = JSON.parse(json.result.content[0].text);
          setResult({ success: true, data: parsed, duration });
        } catch {
          setResult({ success: true, data: json.result.content[0].text, duration });
        }
      } else if (json?.error) {
        setResult({ success: false, error: JSON.stringify(json.error), duration });
      } else {
        setResult({ success: false, error: "Unexpected response format", duration });
      }
    } catch (e) {
      setResult({ success: false, error: String(e), duration: Date.now() - start });
    } finally {
      setLoading(false);
    }
  }, [selected, args]);

  const selectTool = (name: string) => {
    setSelected(name);
    setArgs(TOOL_PRESETS[name] ?? "{}");
    setResult(null);
    setExpandedTool(null);
  };

  const copyResult = () => {
    if (!result) return;
    navigator.clipboard.writeText(JSON.stringify(result.data ?? result.error, null, 2));
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const selectedTool = tools.find((t) => t.name === selected);
  const requiredParams = selectedTool?.inputSchema?.required ?? [];
  const allParams = selectedTool?.inputSchema?.properties ?? {};

  return (
    <div className="flex flex-col gap-6">
      {/* Tool Catalog */}
      <Card className="border-border/50 bg-card/50 backdrop-blur-xl">
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="font-display text-lg flex items-center gap-2">
                <Zap className="h-5 w-5 text-neon-cyan" />
                Tool Catalog
                {user ? (
                  <Badge variant="default" className="text-[10px] px-1.5 py-0 bg-neon-green/20 text-neon-green border-neon-green/30 ml-2">
                    delegated
                  </Badge>
                ) : (
                  <Badge variant="secondary" className="text-[10px] px-1.5 py-0 ml-2">
                    app-only
                  </Badge>
                )}
              </CardTitle>
              <CardDescription>
                {toolsLoading ? "Loading tools..." : `${filteredTools.length} tools available${!user ? " (read-only — login for write)" : ""}`}
                {user && ` • Authenticated as ${user.email}`}
              </CardDescription>
            </div>
            <div className="relative w-64">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Search tools..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-9 h-9 bg-muted/30 border-border/50 text-sm"
              />
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {toolsLoading ? (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="h-24 rounded-lg bg-muted/20 animate-pulse" />
              ))}
            </div>
          ) : (
            <>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                {pagedTools.map((tool) => {
                  const cat = getCategoryForTool(tool.name);
                  const isWrite = WRITE_TOOLS.has(tool.name);
                  const catColor = isWrite ? "text-neon-magenta" : "text-neon-cyan";
                  const Icon = isWrite ? Settings : Users;
                  const isSelected = selected === tool.name;
                  const isExpanded = expandedTool === tool.name;
                  const req = tool.inputSchema?.required ?? [];
                  const props = tool.inputSchema?.properties ?? {};

                  return (
                    <div
                      key={tool.name}
                      className={`group rounded-lg border transition-all cursor-pointer ${
                        isSelected
                          ? "border-neon-cyan/50 bg-neon-cyan/5 shadow-neon-sm"
                          : "border-border/50 bg-muted/20 hover:border-border hover:bg-muted/40"
                      }`}
                    >
                      <button
                        type="button"
                        onClick={() => selectTool(tool.name)}
                        className="w-full text-left p-3"
                      >
                        <div className="flex items-start gap-2">
                          <Icon className={`h-4 w-4 mt-0.5 shrink-0 ${catColor}`} />
                          <div className="flex-1 min-w-0">
                            <p className="font-mono text-xs text-foreground truncate">{tool.name}</p>
                            <p className="text-[11px] text-muted-foreground mt-0.5">
                              {req.length > 0 ? `${req.length} required` : "no params"}
                            </p>
                          </div>
                          {isSelected && (
                            <Badge variant="default" className={`text-[10px] px-1.5 py-0 ${isWrite ? "bg-neon-magenta/20 text-neon-magenta border-neon-magenta/30" : "bg-neon-cyan/20 text-neon-cyan border-neon-cyan/30"}`}>
                              {isWrite ? "write" : "read"}
                            </Badge>
                          )}
                        </div>
                      </button>
                      {isSelected && Object.keys(props).length > 0 && (
                        <div className="border-t border-border/30">
                          <button
                            type="button"
                            onClick={() => setExpandedTool(isExpanded ? null : tool.name)}
                            className="w-full px-3 py-1.5 flex items-center justify-between text-[10px] text-muted-foreground hover:bg-muted/30"
                          >
                            <span>{Object.keys(props).length} params</span>
                            {isExpanded ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
                          </button>
                          {isExpanded && (
                            <div className="px-3 pb-2 space-y-1">
                              {Object.entries(props).map(([key, param]) => (
                                <div key={key} className="flex items-baseline gap-2 text-[10px]">
                                  <span className={`font-mono ${req.includes(key) ? "text-neon-magenta" : "text-muted-foreground"}`}>
                                    {key}
                                    {req.includes(key) && <span className="text-neon-magenta">*</span>}
                                  </span>
                                  <span className="text-muted-foreground/60">{param.type}</span>
                                  {param.description && (
                                    <span className="text-muted-foreground/40 truncate">{param.description}</span>
                                  )}
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>

              {/* Pagination */}
              {totalPages > 1 && (
                <div className="flex items-center justify-between mt-4 pt-3 border-t border-border/30">
                  <p className="text-xs text-muted-foreground">
                    {page * TOOLS_PER_PAGE + 1}–{Math.min((page + 1) * TOOLS_PER_PAGE, filteredTools.length)} of {filteredTools.length}
                  </p>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage((p) => Math.max(0, p - 1))}
                      disabled={page === 0}
                      className="h-7 w-7 p-0"
                    >
                      <ChevronLeft className="h-3 w-3" />
                    </Button>
                    {Array.from({ length: totalPages }).map((_, i) => (
                      <Button
                        key={i}
                        variant={i === page ? "default" : "outline"}
                        size="sm"
                        onClick={() => setPage(i)}
                        className={`h-7 w-7 p-0 text-xs ${i === page ? "bg-neon-cyan/20 text-neon-cyan border-neon-cyan/30" : ""}`}
                      >
                        {i + 1}
                      </Button>
                    ))}
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage((p) => Math.min(totalPages - 1, p + 1))}
                      disabled={page >= totalPages - 1}
                      className="h-7 w-7 p-0"
                    >
                      <ChevronRight className="h-3 w-3" />
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      {/* Runner */}
      {selected && (
        <Card className="border-border/50 bg-card/50 backdrop-blur-xl animate-fade-in">
          <CardHeader>
            <CardTitle className="font-display text-lg flex items-center gap-2">
              <Play className="h-5 w-5 text-neon-cyan" />
              Execute: <span className="font-mono text-sm text-neon-cyan">{selected}</span>
            </CardTitle>
            <CardDescription>
              {requiredParams.length > 0
                ? `Required: ${requiredParams.join(", ")}`
                : "No parameters required"}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <div>
              <label className="text-xs text-muted-foreground mb-1 block">Arguments (JSON)</label>
              <Textarea
                value={args}
                onChange={(e) => setArgs(e.target.value)}
                className="font-mono text-xs min-h-[100px] bg-muted/30 border-border/50"
                placeholder='{"key": "value"}'
              />
            </div>
            <div className="flex items-center gap-3">
              <Button
                onClick={runTool}
                disabled={loading}
                className="bg-neon-cyan text-black hover:bg-neon-cyan/90"
              >
                {loading ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : <Play className="h-4 w-4 mr-2" />}
                {loading ? "Running..." : "Execute"}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => { setSelected(""); setResult(null); setArgs("{}"); }}
                className="text-muted-foreground"
              >
                Clear
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Result */}
      {result && (
        <Card className="border-border/50 bg-card/50 backdrop-blur-xl animate-fade-in">
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle className="font-display text-lg">Result</CardTitle>
              <CardDescription>{result.duration}ms</CardDescription>
            </div>
            <div className="flex items-center gap-2">
              <Badge variant={result.success ? "default" : "destructive"}>
                {result.success ? "OK" : "ERROR"}
              </Badge>
              <Button variant="outline" size="sm" onClick={copyResult}>
                {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            {result.error && (
              <div className="rounded-lg border border-red-500/20 bg-red-500/5 p-4 text-sm text-red-400 font-mono whitespace-pre-wrap">
                {result.error}
              </div>
            )}
            {result.data !== undefined && result.data !== null && (
              <div className="overflow-auto max-h-[600px]">
                <ResultView data={result.data} />
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function ResultView({ data }: { data: unknown }) {
  if (data === null || data === undefined) {
    return <p className="text-muted-foreground text-sm">(null)</p>;
  }

  if (typeof data === "string") {
    return (
      <pre className="text-xs font-mono whitespace-pre-wrap text-muted-foreground p-3 rounded-lg bg-muted/20">
        {data}
      </pre>
    );
  }

  if (Array.isArray(data)) {
    if (data.length === 0) {
      return <p className="text-muted-foreground text-sm">(empty array)</p>;
    }
    if (typeof data[0] === "object" && data[0] !== null) {
      return <TableResult items={data as Record<string, unknown>[]} />;
    }
    return (
      <div className="flex flex-wrap gap-2">
        {data.map((item, i) => (
          <Badge key={i} variant="secondary" className="font-mono text-xs">
            {String(item)}
          </Badge>
        ))}
      </div>
    );
  }

  if (typeof data === "object") {
    const entries = Object.entries(data as Record<string, unknown>);
    if (entries.length === 0) {
      return <p className="text-muted-foreground text-sm">(empty object)</p>;
    }
    return (
      <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
        {entries.map(([key, value]) => (
          <div key={key} className="rounded-lg border border-border/30 bg-muted/20 p-3">
            <p className="text-[10px] text-muted-foreground font-mono mb-1">{key}</p>
            <ResultValue value={value} />
          </div>
        ))}
      </div>
    );
  }

  return (
    <pre className="text-xs font-mono whitespace-pre-wrap text-muted-foreground">
      {JSON.stringify(data, null, 2)}
    </pre>
  );
}

function ResultValue({ value }: { value: unknown }) {
  if (value === null || value === undefined) {
    return <span className="text-muted-foreground/50 text-xs">—</span>;
  }
  if (typeof value === "string") {
    return <span className="text-xs text-foreground break-all">{value}</span>;
  }
  if (typeof value === "boolean") {
    return <Badge variant={value ? "default" : "secondary"} className="text-[10px]">{String(value)}</Badge>;
  }
  if (typeof value === "number") {
    return <span className="text-xs text-neon-cyan font-mono">{value}</span>;
  }
  if (Array.isArray(value)) {
    return <span className="text-xs text-muted-foreground">Array({value.length})</span>;
  }
  return (
    <span className="text-xs text-muted-foreground font-mono">
      {JSON.stringify(value).slice(0, 100)}
      {JSON.stringify(value).length > 100 ? "..." : ""}
    </span>
  );
}

function TableResult({ items }: { items: Record<string, unknown>[] }) {
  if (items.length === 0) return <p className="text-muted-foreground text-sm">(empty)</p>;

  const allKeys = new Set<string>();
  for (const item of items) {
    for (const k of Object.keys(item)) allKeys.add(k);
  }
  const keys = Array.from(allKeys);

  return (
    <div className="overflow-auto rounded-lg border border-border/30">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b border-border/50 bg-muted/30">
            {keys.map((k) => (
              <th key={k} className="px-3 py-2 text-left font-mono text-muted-foreground whitespace-nowrap">{k}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {items.map((item, i) => (
            <tr key={i} className="border-b border-border/20 hover:bg-muted/20">
              {keys.map((k) => (
                <td key={k} className="px-3 py-2 max-w-[250px]">
                  <ResultValue value={item[k]} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <div className="px-3 py-2 border-t border-border/20 bg-muted/10">
        <p className="text-[10px] text-muted-foreground">{items.length} items</p>
      </div>
    </div>
  );
}
