# Arquitectura — zero-teams-mcp

## Visión general

**zero-teams-mcp** es un servidor MCP (.NET 10) que expone Microsoft Teams como herramientas MCP para agentes IA. Un único registro de aplicación multi-tenant administra el acceso a Graph API; cada tenant remoto ejecuta su propio host con transporte stdio o HTTP.

---

## Stack

| Capa | Tecnología | Versión | Propósito |
|------|-----------|---------|-----------|
| Runtime | .NET | 10 | Plataforma de ejecución, hosting Kestrel |
| MCP SDK | ModelContextProtocol | 2.2.0 | Comunicación MCP (stdio / HTTP SSE) |
| Graph SDK | Microsoft.Graph | 6.5.0 | Abstracción tipada sobre Microsoft Graph API |
| Bot Framework | Microsoft.Bot.Builder | 4.23.1 | Adaptive Cards, canal Teams, interacción |
| Panel UI | Next.js | 15 | Dashboard de administración multi-tenant |
| Design System | shadcn/ui | — | Componentes Neón M365, accesibles, componibles |
| Persistencia | SQLite / EF Core | — | Tokens de refresh, estado multi-tenant |
| Secrets | Variables de entorno | — | Client secret, tenant ID — nunca en código |

---

## Estructura de solución

```
zero-teams-mcp/
├── src/
│   ├── ZeroTeams.Mcp.Core/          # Modelos, auth, Graph client wrapper
│   ├── ZeroTeams.Mcp.Server/        # Host MCP, registración de tools
│   ├── ZeroTeams.Mcp.Bot/           # Bot Framework adapter + Adaptive Cards
│   └── ZeroTeams.Mcp.Cli/           # CLI para gestioanr tenants
├── apps/
│   └── controlplane/                # Next.js panel (shadcn/ui Neón)
├── packages/
│   └── ZeroTeams.Mcp.Sdk/          # Paquete NuGet reutilizable
├── tests/
│   ├── ZeroTeams.Mcp.Core.Tests/
│   ├── ZeroTeams.Mcp.Server.Tests/
│   └── ZeroTeams.Mcp.Bot.Tests/
├── deploy/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── bicep/                       # IaC Azure (App Service / Container Apps)
└── docs/
    ├── arquitectura.md
    ├── alcance-mvp.md
    ├── roadmap.md
    └── adr/
```

---

## Diagrama de componentes

```
┌─────────────────────────────────────────────────────────┐
│                    CLIENTE MCP                           │
│         (Claude Desktop, Copilot Studio, CLI)           │
└──────────────┬──────────────────────────────┬───────────┘
               │ stdio                         │ HTTP/SSE
               ▼                               ▼
┌──────────────────────┐       ┌──────────────────────────┐
│   ZeroTeams.Mcp.     │       │   ZeroTeams.Mcp.         │
│   Server (stdio)     │       │   Server (HTTP/Kestrel)  │
│   ─ Transport        │       │   ─ Transport             │
└──────────┬───────────┘       └──────────┬───────────────┘
           │                               │
           └───────────┬───────────────────┘
                       ▼
          ┌────────────────────────┐
          │   ZeroTeams.Mcp.Core   │
          │   ─ GraphClientFactory │
          │   ─ TokenCache         │
          │   ─ ToolDefinitions    │
          │   ─ RetryPolicy        │
          └────────────┬───────────┘
                       ▼
          ┌────────────────────────┐
          │   Microsoft.Graph      │
          │   SDK 6.5.0            │
          └────────────┬───────────┘
                       ▼
          ┌────────────────────────┐
          │   Microsoft Graph API  │
          └────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                    Bot Layer                             │
│   ZeroTeams.Mcp.Bot                                     │
│   ─ BotFrameworkAdapter                                 │
│   ─ AdaptiveCardsBuilder (Neón M365)                    │
│   ─ Action.Execute handler                              │
└──────────────┬──────────────────────────────────────────┘
               ▼
          ┌────────────────────────┐
          │   Microsoft Teams      │
          └────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│               ControlPlane                              │
│   REST API (.NET Minimal API)                           │
│   ─ GET /api/tenants                                   │
│   ─ POST /api/tenants/{id}/consent                     │
│   ─ GET /api/stats                                     │
│   ─ GET /api/health                                    │
└──────────────┬──────────────────────────────────────────┘
               ▼
┌─────────────────────────────────────────────────────────┐
│   Next.js Panel (shadcn/ui Neón M365)                   │
│   ─ Dashboard tenants                                  │
│   ─ Flujo consent admin                                │
│   ─ Estadísticas uso                                   │
└─────────────────────────────────────────────────────────┘
```

---

## Flujo de autenticación

### 1. Client Credentials → Token Cache

```
App startup
  └→ GraphClientFactory.Create(tenantId, clientId, clientSecret)
       └→ ClientSecretCredential(clientId, clientSecret, tenantId)
            └→ TokenCredential.GetTokenAsync() → AccessToken
                 └→ Cache (MemoryCache, TTL 50 min)
                      └→ GraphServiceClient(credential)
```

### 2. Retry automático

- **Policy**: Polly `WaitAndRetry` con 3 intentos
- **Backoff**: exponencial 2s → 4s → 8s
- **429 Too Many Requests**: respeta `Retry-After` header
- **401 Unauthorized**: invalida cache, re-intenta 1 vez
- **5xx**: reintenta con backoff

### 3. Seguridad

| Regla | Implementación |
|-------|---------------|
| Client secret | Solo en `CLIENT_SECRET` env var |
| Token cache | In-memory, nunca persistido a disco |
| Tenant ID | Parámetro de configuración por host |
| Graph scopes | Delegados al mínimo necesario (principio de mínimo privilegio) |
| Secrets en código | Prohibido — linter + CI check |

---

## Modelo multi-tenant

### Registro único multi-tenant

Se usa **una sola aplicación registrada** en Azure AD con tipo `Accounts in any organizational directory (Multitenant)`.

```
App Registration (Azure AD)
├── Client ID: 00000000-0000-0000-0000-000000000000
├── Client Secret: [solo en env]
├── Type: Multitenant
├── Redirect URI: https://<controlplane>/auth/callback
└── API Permissions:
    ├── User.Read.All
    ├── Team.ReadBasic.All
    ├── Channel.ReadBasic.All
    ├── ChannelMessage.Read
    ├── ChannelMessage.Send
    ├── Chat.Read.All
    ├── Chat.ReadWrite.All
    └── ChatMessage.Send
```

### Admin Consent por tenant

```
Tenant admin accede a:
https://login.microsoftonline.com/{tenant-id}/adminconsent
  ?client_id={app-client-id}
  &redirect_uri={controlplane-url}/auth/callback

Flujo:
1. Admin hace login en su tenant
2. Azure AD muestra permisos solicitados
3. Admin aprueba (consent global)
4. Redirect → ControlPlane almacena tenant_id + estado "consented"
5. Host MCP configurado con ese tenant_id puede operar
```

### Un host por tenant

Cada tenant tiene su propio proceso MCP:

```
Host A (tenant "acme.onmicrosoft.com")
  → tenantId: xxx-xxx-xxx
  → transport: stdio (Claude Desktop)
  → GraphServiceClient configurado para ese tenant

Host B (tenant "globex.onmicrosoft.com")
  → tenantId: yyy-yyy-yyy
  → transport: HTTP (API remota)
  → GraphServiceClient configurado para ese tenant
```

---

## Transporte dual

### stdio

- **Uso**: Claude Desktop, herramientas locales
- **Protocolo**: MCP stdio transport
- **Deploy**: ejecutable standalone por tenant

```json
{
  "mcpServers": {
    "teams-acme": {
      "command": "zero-teams-mcp",
      "args": ["--tenant", "acme.onmicrosoft.com"],
      "env": {
        "CLIENT_ID": "00000000-...",
        "CLIENT_SECRET": "abc123...",
        "TENANT_ID": "xxx-xxx-xxx"
      }
    }
  }
}
```

### HTTP/SSE

- **Uso**: Copilot Studio, APIs remotas, múltiples clientes
- **Protocolo**: MCP HTTP+SSE transport sobre Kestrel
- **Deploy**: Container App / App Service

```
POST /mcp/messages  →  SSE stream de respuestas
GET  /mcp/sse       →  SSE endpoint de notificaciones
```

---

## ControlPlane REST API

API minimalista para gestión multi-tenant. Implementada con .NET Minimal API.

### Endpoints

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/api/tenants` | Lista todos los tenants registrados |
| `POST` | `/api/tenants` | Registra un nuevo tenant |
| `GET` | `/api/tenants/{id}` | Detalle de un tenant |
| `DELETE` | `/api/tenants/{id}` | Elimina un tenant |
| `POST` | `/api/tenants/{id}/consent` | Genera URL de consent admin |
| `GET` | `/api/tenants/{id}/status` | Estado del host MCP |
| `GET` | `/api/stats` | Estadísticas de uso agregadas |
| `GET` | `/api/health` | Health check (liveness + readiness) |

### Modelo de datos

```csharp
public record Tenant(
    Guid Id,
    string TenantDomain,      // "acme.onmicrosoft.com"
    string TenantId,          // Azure AD tenant ID
    string Status,            // "pending" | "consented" | "active" | "error"
    DateTime CreatedAt,
    DateTime? LastActivityAt,
    int TotalCalls,
    int ErrorCount
);
```

---

## Panel Next.js

### Características

- **Framework**: Next.js 15 (App Router)
- **UI**: shadcn/ui con tema Neón M365
- **Auth**: NextAuth.js con Microsoft Entra ID
- **API**: Consumo directo del ControlPlane REST

### Vistas

| Vista | Descripción |
|-------|-------------|
| Dashboard | Total tenants, llamadas hoy, errores |
| Tenants | CRUD de tenants con estado de consent |
| Consent Flow | Botón que genera URL de admin consent |
| Logs | Últimas llamadas MCP, filtros por tenant |
| Health | Estado de cada host MCP en tiempo real |

### Design System Neón M365

- **Color primario**: Azul eléctrico (`#0078D4` — Brand Microsoft)
- **Acento**: Cian neón (`#00E5FF`)
- **Fondo**: Gris oscuro (`#1B1B1F`)
- **Superficies**: `#242428` con borde `#333336`
- **Texto primario**: `#FFFFFF`
- **Texto secundario**: `#ADADAD`
- **Cards**: gradiente sutil de fondo + borde luminoso en hover
- **Tipografía**: Segoe UI Variable (sistema) + fallback Inter

---

## Adaptive Cards — Bot Framework

### Flujo Action.Execute

```
Usuario en Teams escribe comando
  → Bot Framework recibe Activity (message)
    → Bot procesa Adaptive Card template
      → Card incluye Action.Execute con inputs
        → Usuario interactúa con card
          → Teams envía invoke activity (action/execute)
            → Bot handler procesa resultado
              → Responde con card actualizada
```

### Por qué Action.Execute (no Action.Submit)

| Action.Submit | Action.Execute |
|--------------|----------------|
| Solo funciona en chats 1:1 | Funciona en canales y chats |
| No soporta Adaptive Cards ricas | Soporta todas las propiedades |
| Input validation limitada | Validación server-side completa |
| No genera invoke activity | Genera invoke → procesamiento server |

### Template de ejemplo

```json
{
  "type": "AdaptiveCard",
  "body": [
    {
      "type": "TextBlock",
      "text": "Canales del equipo",
      "style": "heading",
      "color": "accent"
    },
    {
      "type": "Input.ChoiceSet",
      "id": "channelId",
      "choices": []
    }
  ],
  "actions": [
    {
      "type": "Action.Execute",
      "title": "Ver mensajes",
      "verb": "list-messages",
      "data": {
        "teamId": "{{teamId}}"
      }
    }
  ]
}
```

---

## Decisiones arquitectónicas clave

| Decisión | ADR |
|----------|-----|
| Microsoft.Graph SDK sobre REST crudo | ADR-0001 |
| Transporte dual stdio + HTTP | ADR-0002 |
| App multi-tenant única | ADR-0003 |
| Action.Execute para Adaptive Cards | ADR-0004 |
| ControlPlane REST API | ADR-0005 |
| shadcn/ui + Neón M365 | ADR-0006 |
