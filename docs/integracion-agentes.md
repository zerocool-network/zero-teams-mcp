# Integración con Agentes IA — SDD

> **Software Design Document** — Cómo integrar zero-teams-mcp con agentes IA (Claude Desktop, Cursor, Copilot Studio, agentes personalizados).

---

## 1. Visión general

**zero-teams-mcp** expone 25 herramientas de Microsoft Teams como un servidor MCP (Model Context Protocol). Cualquier agente IA compatible con MCP puede descubrir y ejecutar estas herramientas para interactuar con Teams: listar chats, enviar mensajes, crear reuniones, gestionar calendarios, administrar tenants.

### Modelos de integración

| Modelo | Transporte | Caso de uso | Agentes |
|--------|-----------|-------------|---------|
| **Local stdio** | stdin/stdout | Desarrollo, un solo tenant | Claude Desktop, Cursor |
| **HTTP/SSE** | TCP/IP | Producción, multi-tenant | Copilot Studio, agentes remotos, APIs |
| **Híbrido** | stdio + HTTP | ControlPlane local + MCP remoto | Agentes con panel de control |

---

## 2. Arquitectura de integración

```
┌─────────────────────────────────────────────────────────────────┐
│                     AGENTE IA                                    │
│  (Claude Desktop / Cursor / Copilot Studio / Custom Agent)      │
└──────────────┬────────────────────────────┬──────────────────────┘
               │                            │
          stdio transport              HTTP/SSE transport
               │                            │
               ▼                            ▼
┌──────────────────────────┐   ┌──────────────────────────────────┐
│   zero-teams-mcp         │   │   zero-teams-mcp                 │
│   Server (stdio)         │   │   Server (HTTP :5200)            │
│   ─ 25 MCP tools         │   │   ─ 25 MCP tools                 │
│   ─ 1 tenant por proceso │   │   ─ Multi-tenant via headers     │
└──────────┬───────────────┘   └──────────┬───────────────────────┘
           │                              │
           └──────────┬───────────────────┘
                      ▼
           ┌────────────────────────┐
           │   ZeroTeamsMcp.Core    │
           │   ─ GraphAuthProvider  │
           │   ─ GraphService       │
           │   ─ 25 tool classes    │
           └────────────┬───────────┘
                        ▼
           ┌────────────────────────┐
           │   Microsoft Graph API  │
           │   (Teams, Calendar,    │
           │    Meetings, Chat)     │
           └────────────────────────┘
```

---

## 3. Transporte stdio (Local)

### 3.1 Flujo

```
Agente IA inicia proceso
  └→ spawn: dotnet run --project src/ZeroTeamsMcp.Server
       └→ MCP Server arranca con WithStdioServerTransport()
            └→ Lee stdin (JSON-RPC 2.0)
            └→ Responde en stdout
            └→ 25 tools descubiertos vía WithToolsFromAssembly()
```

### 3.2 Configuración por agente

#### Claude Desktop

**Archivo:** `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "teams": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/Users/zerocool/Proyectos/ZeroCoolNetwork/zero-teams-mcp/src/ZeroTeamsMcp.Server"
      ],
      "env": {
        "AZURE_TENANT_ID": "tu-tenant-id",
        "AZURE_CLIENT_ID": "tu-client-id",
        "AZURE_CLIENT_SECRET": "tu-client-secret"
      }
    }
  }
}
```

#### Cursor

**Archivo:** `.cursor/mcp.json` (en el proyecto o global `~/.cursor/mcp.json`)

```json
{
  "mcpServers": {
    "teams": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/Users/zerocool/Proyectos/ZeroCoolNetwork/zero-teams-mcp/src/ZeroTeamsMcp.Server"
      ],
      "env": {
        "AZURE_TENANT_ID": "tu-tenant-id",
        "AZURE_CLIENT_ID": "tu-client-id",
        "AZURE_CLIENT_SECRET": "tu-client-secret"
      }
    }
  }
}
```

#### Agente personalizado (Node.js / Python)

```typescript
// Node.js — usando @modelcontextprotocol/sdk
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

// Listar tools disponibles
const tools = await client.listTools();
console.log(tools); // 25 tools de Teams

// Ejecutar un tool
const result = await client.callTool({
  name: "teams_list_teams",
  arguments: {},
});
```

```python
# Python — usando mcp SDK
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

server_params = StdioServerParameters(
    command="dotnet",
    args=["run", "--project", "src/ZeroTeamsMcp.Server"],
    env={
        "AZURE_TENANT_ID": "xxx",
        "AZURE_CLIENT_ID": "yyy",
        "AZURE_CLIENT_SECRET": "zzz",
    },
)

async with stdio_client(server_params) as (read, write):
    async with ClientSession(read, write) as session:
        await session.initialize()
        tools = await session.list_tools()
        result = await session.call_tool("teams_list_teams", {})
```

### 3.3 Limitaciones stdio

| Limitación | Descripción |
|------------|-------------|
| Un solo tenant | Cada proceso MCP = 1 tenant (env vars al spawn) |
| Sin concurrencia | Un agente a la vez por proceso |
| Local only | No accesible desde red |

---

## 4. Transporte HTTP/SSE (Remoto)

### 4.1 Flujo

```
Agente IA envía HTTP POST
  └→ http://localhost:5200/mcp
       └→ MCP Server con WithHttpTransport()
            └→ Kestrel procesa JSON-RPC 2.0
            └→ Responde con SSE stream
            └→ 25 tools descubiertos
```

### 4.2 Endpoints

| Método | Ruta | Content-Type | Descripción |
|--------|------|-------------|-------------|
| `POST` | `/mcp` | `application/json` | Envío de mensajes MCP (JSON-RPC 2.0) |
| `GET` | `/mcp` | `text/event-stream` | SSE stream de notificaciones |

### 4.3 Configuración por agente

#### Claude Desktop (HTTP)

```json
{
  "mcpServers": {
    "teams": {
      "url": "http://localhost:5200"
    }
  }
}
```

#### Cursor (HTTP)

```json
{
  "mcpServers": {
    "teams": {
      "url": "http://localhost:5200"
    }
  }
}
```

#### Copilot Studio / Agente remoto

```yaml
# Conexión MCP en Copilot Studio
endpoint: http://tu-servidor:5200/mcp
transport: http
```

#### cURL (prueba manual)

```bash
# Listar tools
curl -X POST http://localhost:5200/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/list",
    "params": {}
  }'

# Ejecutar tool
curl -X POST http://localhost:5200/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
      "name": "teams_list_teams",
      "arguments": {}
    }
  }'
```

### 4.4 Multi-tenant via HTTP

El servidor HTTP puede manejar múltiples tenants si se configura con un tenant por defecto o se usan headers personalizados:

```
POST /mcp
X-Tenant-ID: <tenant-id>
```

**Implementación actual:** Un solo tenant por instancia del servidor. Para multi-tenant real, desplegar una instancia por tenant o implementar routing por header en `Program.cs`.

---

## 5. Catálogo de herramientas MCP

### 5.1 Descubrimiento

Cuando un agente se conecta al servidor MCP, ejecuta `tools/list` y recibe el catálogo completo:

```json
{
  "tools": [
    {
      "name": "teams_list_teams",
      "description": "Lista todos los equipos del usuario actual",
      "inputSchema": {
        "type": "object",
        "properties": {}
      }
    },
    {
      "name": "teams_send_message",
      "description": "Envía un mensaje a un chat de Teams",
      "inputSchema": {
        "type": "object",
        "properties": {
          "chatId": { "type": "string", "description": "ID del chat" },
          "message": { "type": "string", "description": "Contenido del mensaje" }
        },
        "required": ["chatId", "message"]
      }
    }
    // ... 23 tools más
  ]
}
```

### 5.2 Categorías

| Categoría | Tools | Permisos Graph |
|-----------|-------|----------------|
| **Teams** | 13 | Team.ReadBasic.All, Channel.ReadBasic.All, Chat.ReadWrite.All, ChatMessage.Send |
| **Meetings** | 5 | OnlineMeetings.ReadWrite, OnlineMeetingAttendance.Read |
| **Calendar** | 4 | Calendars.ReadWrite |
| **Admin** | 6 | Ninguno (ControlPlane REST) |

### 5.3 Ejemplo de invocación desde agente

```
Agente: "¿Qué equipos tengo?"
  → callTool("teams_list_teams", {})
  ← [{ id: "team-1", displayName: "Engineering", ... }, ...]
  → Agente formatea respuesta: "Tienes 3 equipos: Engineering, Design, Marketing"

Agente: "Envía un mensaje al chat del equipo Engineering"
  → callTool("teams_list_chats", {})
  ← [{ id: "chat-1", topic: "Engineering", ... }, ...]
  → callTool("teams_send_message", { chatId: "chat-1", message: "Hola equipo" })
  ← { status: "sent", messageId: "msg-123" }
  → Agente: "Mensaje enviado al chat Engineering"
```

---

## 6. Autenticación

### 6.1 Client Credentials (app-only)

```
Azure AD App Registration
  ├── Tenant ID: xxx-xxx-xxx
  ├── Client ID: yyy-yyy-yyy
  ├── Client Secret: zzz-zzz-zzz
  └── Permissions: Application (8 permisos)
```

**Flow:**
1. MCP Server inicia → `GraphAuthProvider` obtiene token via MSAL
2. Token cached 50 minutos (in-memory)
3. Cada llamada a Graph API usa el token cached
4. Si 401 → invalida cache, re-intenta 1 vez

### 6.2 Admin Consent (una vez por tenant)

```
Admin del tenant accede a:
https://login.microsoftonline.com/{tenant-id}/adminconsent
  ?client_id={app-client-id}

Flujo:
1. Admin login
2. Azure AD muestra permisos
3. Admin aprueba → consent global para la app
4. MCP Server puede operar en ese tenant
```

### 6.3 Seguridad

| Regla | Implementación |
|-------|---------------|
| Secrets en env vars | Nunca en código fuente |
| Token in-memory | Nunca persistido a disco |
| Mínimo privilegio | Solo permisos necesarios |
| Multi-tenant app | Una app, admin consent por tenant |
| HTTPS en producción | TLS termination en proxy/reverse proxy |

---

## 7. Manejo de errores

### 7.1 Errores MCP estándar

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32600,
    "message": "Tool not found: teams_invalid_tool"
  }
}
```

### 7.2 Errores Graph API

| HTTP Code | Significado | Acción del agente |
|-----------|-------------|-------------------|
| 400 | Bad Request | Revisar parámetros |
| 401 | Unauthorized | Token expirado, reintentar |
| 403 | Forbidden | Permiso no concedido |
| 404 | Not Found | Recurso no existe |
| 429 | Rate Limited | Respeta Retry-After |
| 500 | Server Error | Reintentar con backoff |

### 7.3 Retry automático (en el servidor)

- **Policy**: Polly WaitAndRetry, 3 intentos
- **Backoff**: Exponencial 2s → 4s → 8s
- **429**: Respeta header `Retry-After`
- **401**: Invalida cache, re-intenta 1 vez

---

## 8. Patrones de uso del agente

### 8.1 Conversación multi-turn

```
Usuario: "Crea una reunión mañana a las 10am con el equipo Engineering"

Agente:
1. callTool("teams_list_teams") → [{ id: "eng-1", displayName: "Engineering" }]
2. callTool("teams_get_chat_members", { chatId: "chat-eng" }) → [{ id: "user-1" }, ...]
3. callTool("teams_create_meeting", {
     subject: "Reunión Engineering",
     startDateTime: "2026-09-06T10:00:00",
     endDateTime: "2026-09-06T11:00:00",
     attendees: ["user-1@contoso.com", "user-2@contoso.com"]
   })
4. Respuesta: "Reunión creada para mañana 10:00-11:00. Join URL: https://..."
```

### 8.2 Búsqueda y filtrado

```
Usuario: "¿Qué mensajes hay en el canal general?"

Agente:
1. callTool("teams_list_teams") → selecciona equipo
2. callTool("teams_list_channels", { teamId: "eng-1" }) → [{ id: "gen-1", displayName: "General" }]
3. callTool("teams_get_chat_messages", { chatId: "gen-1" }) → últimos mensajes
4. Respuesta formateada con contexto
```

### 8.3 CRUD completo

```
Usuario: "Crea un canal nuevo llamado 'Proyectos' en el equipo Engineering"

Agente:
1. callTool("teams_list_teams") → [{ id: "eng-1" }]
2. callTool("teams_create_channel", {
     teamId: "eng-1",
     displayName: "Proyectos",
     description: "Canal para discusión de proyectos"
   })
3. Respuesta: "Canal 'Proyectos' creado en Engineering"
```

---

## 9. Administración de tenants

### 9.1 Tools admin (vía MCP)

El agente puede gestionar tenants directamente:

```
Agente: "¿Qué tenants tengo configurados?"
  → callTool("admin_list_tenants", {})
  ← [{ id: "t1", displayName: "Contoso", status: "active" }, ...]

Agente: "Agrega el tenant de Fabrikam"
  → callTool("admin_create_tenant", {
      displayName: "Fabrikam",
      tenantId: "fabrikam-tenant-id",
      clientId: "fabrikam-client-id",
      clientSecret: "fabrikam-secret"
    })
  ← { id: "t2", consentUrl: "https://login.microsoftonline.com/..." }
  → Agente: "Tenant creado. Envía esta URL al admin de Fabrikam para consent"
```

### 9.2 Panel web (alternativa)

Para usuarios no técnicos, el panel Next.js en `:3000` ofrece CRUD visual de tenants y flujo de consentimiento guiado.

---

## 10. Deployment

### 10.1 Local (desarrollo)

```bash
# stdio para Claude Desktop
dotnet run --project src/ZeroTeamsMcp.Server

# HTTP para testing
TRANSPORT=http dotnet run --project src/ZeroTeamsMcp.Server
```

### 10.2 Docker Compose (producción local)

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Servicios: ControlPlane (:5100), Panel (:3000), MCP Server (:5200)

### 10.3 Cloud (producción)

```bash
# Azure Container Apps
az containerapp up \
  --name zero-teams-mcp \
  --resource-group myRG \
  --image zerocool/zero-teams-mcp:latest \
  --env-vars AZURE_TENANT_ID=x AZURE_CLIENT_ID=y AZURE_CLIENT_SECRET=z \
  --transport http
```

---

## 11. Checklist de integración

Para integrar zero-teams-mcp con un nuevo agente:

- [ ] **Transporte**: ¿stdio o HTTP? (stdio = local, HTTP = remoto)
- [ ] **Configuración MCP**: Crear config en el agente (ver §3.2 o §4.3)
- [ Azure AD**: App registration con permisos (ver §6.1)
- [ ] **Admin Consent**: Ejecutar una vez por tenant (ver §6.2)
- [ ] **Variables de entorno**: Configurar AZURE_TENANT_ID, CLIENT_ID, CLIENT_SECRET
- [ ] **Firewall**: Si HTTP, abrir puerto 5200
- [ ] **TLS**: En producción, reverse proxy con certificado
- [ ] **Testing**: Probar `teams_list_teams` como primer tool

---

## 12. Referencias

| Recurso | URL |
|---------|-----|
| MCP SDK C# | https://github.com/modelcontextprotocol/csharp-sdk |
| MCP Spec | https://spec.modelcontextprotocol.io |
| Microsoft Graph | https://learn.microsoft.com/en-us/graph/ |
| Graph API Teams | https://learn.microsoft.com/en-us/graph/api/resources/team |
| MSAL.NET | https://learn.microsoft.com/en-us/msal/dotnet/ |
| Bot Framework | https://learn.microsoft.com/en-us/bot-framework/ |

---

## Estado

| Sección | Estado |
|---------|--------|
| Transporte stdio | ✅ Implementado |
| Transporte HTTP | ✅ Implementado |
| 25 MCP tools | ✅ Implementados |
| Auth client credentials | ✅ Implementado |
| Admin consent | ✅ Implementado |
| Multi-tenant | ✅ Implementado (app única) |
| Retry/Polly | ⏳ Pendiente (F9) |
| Rate limiting | ⏳ Pendiente (F9) |
