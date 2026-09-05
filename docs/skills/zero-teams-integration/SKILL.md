# Skill: zero-teams-integration

Integra agentes IA con zero-teams-mcp (servidor MCP para Microsoft Teams).

## Trigger

Cuando el usuario pregunte sobre:
- Cómo conectar un agente a zero-teams-mcp
- Configurar Claude Desktop / Cursor / Copilot Studio con Teams
- Agregar tools de Teams a un agente
- Transporte stdio vs HTTP
- Auth y consent para el MCP server

## Contexto

- **Proyecto**: `zero-teams-mcp/` — servidor MCP .NET 10 con 25 tools de Microsoft Teams
- **Docs**: `docs/integracion-agentes.md` — SDD completo de integración
- **Transportes**: stdio (local) + HTTP/SSE (remoto en `:5200/mcp`)
- **Auth**: Client credentials (app-only), admin consent por tenant
- **Tools**: 13 Teams + 5 Meetings + 4 Calendar + 6 Admin

## Workflow

### 1. Identificar agente destino

Preguntar al usuario:
- ¿Qué agente usa? (Claude Desktop, Cursor, Copilot Studio, custom)
- ¿stdio o HTTP? (stdio = local, HTTP = remoto)
- ¿Ya tiene Azure AD app registration?

### 2. Generar configuración MCP

**Claude Desktop (stdio):**
```json
{
  "mcpServers": {
    "teams": {
      "command": "dotnet",
      "args": ["run", "--project", "/ruta/a/zero-teams-mcp/src/ZeroTeamsMcp.Server"],
      "env": {
        "AZURE_TENANT_ID": "tu-tenant-id",
        "AZURE_CLIENT_ID": "tu-client-id",
        "AZURE_CLIENT_SECRET": "tu-secret"
      }
    }
  }
}
```

**Claude Desktop (HTTP):**
```json
{
  "mcpServers": {
    "teams": {
      "url": "http://localhost:5200"
    }
  }
}
```

**Cursor:** Mismo formato, archivo `.cursor/mcp.json`

**Custom agent (Node.js):**
```typescript
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const transport = new StdioClientTransport({
  command: "dotnet",
  args: ["run", "--project", "src/ZeroTeamsMcp.Server"],
  env: {
    AZURE_TENANT_ID: "xxx",
    AZURE_CLIENT_ID: "yyy",
    AZURE_CLIENT_SECRET: "zzz",
  },
});

const client = new Client({ name: "my-agent", version: "1.0.0" });
await client.connect(transport);
const tools = await client.listTools(); // 25 tools
```

### 3. Verificar prerequisitos

- [ ] .NET SDK 10.0+ instalado
- [ ] Azure AD App Registration creado
- [ ] Permisos Graph configurados (8 permisos)
- [ ] Admin consent ejecutado para el tenant
- [ ] Variables de entorno configuradas

### 4. Probar conexión

```bash
# 1. Arrancar servidor
TRANSPORT=http dotnet run --project src/ZeroTeamsMcp.Server

# 2. Listar tools
curl -X POST http://localhost:5200/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'

# 3. Ejecutar tool
curl -X POST http://localhost:5200/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"teams_list_teams","arguments":{}}}'
```

### 5. Documentar integración

Si el usuario crea una integración nueva, ofrecer documentar en `docs/integracion-agentes.md` con:
- Nombre del agente
- Configuración MCP utilizada
- Transporte seleccionado
- Notas especiales

## Tools disponibles

### Teams (13)
| Tool | Descripción |
|------|-------------|
| `teams_list_teams` | Lista equipos |
| `teams_list_chats` | Lista chats |
| `teams_create_chat` | Crea chat |
| `teams_send_message` | Envía mensaje |
| `teams_get_chat_messages` | Obtiene mensajes |
| `teams_list_channels` | Lista canales |
| `teams_create_channel` | Crea canal |
| `teams_send_channel_message` | Envía mensaje a canal |
| `teams_get_chat_members` | Miembros de chat |
| `teams_add_chat_member` | Agrega miembro |
| `teams_remove_chat_member` | Elimina miembro |
| `teams_get_channel_members` | Miembros de canal |
| `teams_get_team_channels` | Alias de list_channels |

### Meetings (5)
| Tool | Descripción |
|------|-------------|
| `teams_list_meetings` | Lista reuniones |
| `teams_get_meeting` | Detalle de reunión |
| `teams_create_meeting` | Crea reunión |
| `teams_delete_meeting` | Elimina reunión |
| `teams_get_meeting_attendance` | Reporte asistencia |

### Calendar (4)
| Tool | Descripción |
|------|-------------|
| `teams_list_calendar_events` | Lista eventos |
| `teams_get_calendar_event` | Detalle de evento |
| `teams_create_calendar_event` | Crea evento |
| `teams_delete_calendar_event` | Elimina evento |

### Admin (6)
| Tool | Descripción |
|------|-------------|
| `admin_list_tenants` | Lista tenants |
| `admin_get_tenant` | Detalle tenant |
| `admin_create_tenant` | Crea tenant |
| `admin_delete_tenant` | Elimina tenant |
| `admin_get_consent` | Estado consent |
| `admin_health` | Health check |

## Error handling

| Error | Causa | Solución |
|-------|-------|----------|
| "Tool not found" | Tool name incorrecto | Verificar nombre exacto del tool |
| 401 Unauthorized | Token expirado | Re-intenta (auto-retry en servidor) |
| 403 Forbidden | Permiso no concedido | Verificar permisos en Azure AD |
| 429 Rate Limited | Límite de Graph API | Respeta Retry-After header |
| Connection refused | Servidor MCP no arrancado | Verificar que el proceso está corriendo |

## Referencias

- SDD completo: `docs/integracion-agentes.md`
- Arquitectura: `docs/arquitectura.md`
- Alcance MVP: `docs/alcance-mvp.md`
- README: `README.md`
