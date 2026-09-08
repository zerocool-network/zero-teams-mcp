# Teams for AI Agents

[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com)
[![MCP SDK 2.2](https://img.shields.io/badge/MCP-2.2.0-blue)](https://github.com/modelcontextprotocol/csharp-sdk)
[![Graph SDK 6.5](https://img.shields.io/badge/Graph-6.5.0-green)](https://learn.microsoft.com/en-us/graph/sdks/sdk-installation)
[![Bot Builder 4.23](https://img.shields.io/badge/Bot.Builder-4.23.1-orange)](https://github.com/microsoft/botbuilder-dotnet)
[![CI](https://img.shields.io/github/actions/workflow/status/zerocool-network/zero-teams-mcp/ci.yml?label=CI)](.github/workflows/ci.yml)

**Microsoft Teams for AI Agents** — Servidor MCP para Microsoft Teams con .NET 10 y Microsoft Graph API. Multi-tenant, app-only, stdio + HTTP dual transport.

---

## Stack

| Componente | Versión | Propósito |
|---|---|---|
| .NET SDK | 10.0.400 | Runtime y herramientas |
| ModelContextProtocol | 2.2.0 | Servidor MCP (stdio + HTTP) |
| Microsoft.Graph | 6.5.0 | API de Microsoft Teams |
| Microsoft.Bot.Builder | 4.23.1 | Adaptive Cards interactivas |
| Next.js + shadcn | 15.x | Panel del orquestador |
| Docker Compose | — | Despliegue multi-tenant |

## Arquitectura

```
┌─────────────────────────────────────────────────────────┐
│                  Teams for AI Agents                     │
├─────────────┬─────────────┬─────────────┬───────────────┤
│    Core     │   Server    │     Bot     │ ControlPlane  │
│  (Graph,    │  (MCP stdio │  (Adaptive  │  (REST API,   │
│   Auth,     │   + HTTP)   │   Cards)    │   Tenants)    │
│   Models)   │  19 tools   │  9 cards    │  7 endpoints  │
└──────┬──────┴──────┬──────┴──────┬──────┴───────┬───────┘
       │             │             │              │
       │        ┌────┴────┐  ┌─────┴─────┐  ┌────┴────┐
       │        │ Claude  │  │  Teams    │  │ Panel   │
       │        │ Desktop │  │  Bot      │  │ Next.js │
       │        └─────────┘  └───────────┘  └─────────┘
       │
  ┌────┴────┐
  │  Graph  │
  │   API   │
  └─────────┘
```

---

## Quick Start

### Opción A: Wizard interactivo (recomendado)

```bash
./scripts/setup.sh
```

Te guía paso a paso: crea la app en Azure AD, copia credenciales, configura permisos, valida la conexión.

### Opción B: Manual

#### 1. Prerrequisitos

- .NET SDK 10.0+
- Node.js 18+ / pnpm (para el panel)
- App registration en Azure AD

#### 2. Configurar Azure AD

1. Crear **App Registration** en [Entra Portal](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) →「New registration」
2. Name: `Teams for AI Agents`
3. Account type: **Accounts in this organizational directory only**
4. Redirect URI: dejar vacío (usamos client credentials, no browser)
5. Guardar: `TenantId`, `ClientId`, `ClientSecret`

#### 3. API Permissions (Application)

| Permiso | Para |
|---|---|
| `Team.ReadBasic.All` | Listar equipos |
| `Channel.ReadBasic.All` | Listar canales |
| `Chat.ReadWrite.All` | Chats + enviar mensajes |
| `ChatMessage.Send` | Enviar mensajes |
| `Calendars.ReadWrite` | Calendario |
| `OnlineMeetings.ReadWrite` | Crear/gestionar reuniones |
| `OnlineMeetingAttendance.Read` | Asistencia a reuniones |
| `User.Read.All` | Info de usuarios |

6. **Grant admin consent** para cada permiso (botón verde)

#### 4. Variables de entorno

```bash
export AZURE_TENANT_ID="tu-tenant-id"
export AZURE_CLIENT_ID="tu-client-id"
export AZURE_CLIENT_SECRET="tu-client-secret"
```

O crear `deploy/.env`:

```bash
AZURE_TENANT_ID=xxx
AZURE_CLIENT_ID=xxx
AZURE_CLIENT_SECRET=xxx
```

### 5. Ejecutar

**Docker Compose (todo junto):**

```bash
docker compose -f deploy/docker-compose.yml up -d
```

Servicios: ControlPlane (:5100), Panel (:3000), MCP Server (:5200)

**Manual — MCP Server (stdio — para Claude Desktop):**

```bash
dotnet run --project src/ZeroTeamsMcp.Server
```

**Manual — MCP Server (HTTP):**

```bash
TRANSPORT=http dotnet run --project src/ZeroTeamsMcp.Server
```

**Manual — ControlPlane:**

```bash
dotnet run --project src/ZeroTeamsMcp.ControlPlane
```

**Manual — Panel:**

```bash
cd apps/panel && pnpm install && pnpm dev
```

---

## MCP Tools — 19 herramientas activas

### Teams (13 tools)

| Tool | Descripción | Graph API | Tipo |
|------|-------------|-----------|------|
| `teams_list_teams` | Lista todos los equipos | `GET /me/joinedTeams` | Lectura |
| `teams_list_chats` | Lista chats 1:1 y grupales | `GET /users/{id}/chats` | Lectura |
| `teams_create_chat` | Crea chat 1:1 o grupal | `POST /chats` | Escritura |
| `teams_send_message` | Envía mensaje a un chat | `POST /chats/{id}/messages` | Escritura |
| `teams_get_chat_messages` | Obtiene mensajes de un chat | `GET /chats/{id}/messages` | Lectura |
| `teams_list_channels` | Lista canales de un equipo | `GET /teams/{id}/channels` | Lectura |
| `teams_create_channel` | Crea canal nuevo | `POST /teams/{id}/channels` | Escritura |
| `teams_send_channel_message` | Envía mensaje a un canal | `POST /teams/{id}/channels/{id}/messages` | Escritura |
| `teams_get_chat_members` | Miembros de un chat | `GET /chats/{id}/members` | Lectura |
| `teams_add_chat_member` | Agrega miembro a chat | `POST /chats/{id}/members` | Escritura |
| `teams_remove_chat_member` | Elimina miembro de chat | `DELETE /chats/{id}/members/{id}` | Escritura |
| `teams_get_channel_members` | Miembros de un canal | `GET /teams/{id}/channels/{id}/members` | Lectura |
| `teams_get_team_channels` | Alias de `teams_list_channels` | `GET /teams/{id}/channels` | Lectura |

### Admin (6 tools)

| Tool | Descripción | Endpoint |
|------|-------------|----------|
| `admin_list_tenants` | Lista todos los tenants registrados | `GET /api/tenants` |
| `admin_get_tenant` | Detalle de un tenant | `GET /api/tenants/{id}` |
| `admin_create_tenant` | Registra nuevo tenant | `POST /api/tenants` |
| `admin_delete_tenant` | Elimina tenant | `DELETE /api/tenants/{id}` |
| `admin_get_consent` | Estado de consentimiento | `GET /api/tenants/{id}/consent` |
| `admin_health` | Health check del ControlPlane | `GET /health` |

> **Nota:** Meetings y Calendar tools están disponibles pero desactivados (requieren Exchange Online Application Access Policy).

---

## Testing Tools

### CLI — `zero-teams test`

```bash
# Listar herramientas disponibles
zero-teams tools

# Probar una herramienta
zero-teams test teams_list_teams

# Con argumentos JSON
zero-teams test teams_list_channels --args '{"teamId":"<id>"}'

# Respuesta JSON crudo
zero-teams test teams_list_teams --json

# Tabla formateada (auto-detect para arrays)
zero-teams test teams_list_teams --table
```

### Panel — Playground

Accede al Playground en `http://localhost:3000/playground` para probar herramientas interactivamente con selector de tools, editor de argumentos JSON y vista de resultados en tabla o JSON crudo.

---

## Claude Desktop — Configuración

Agregar en `~/Library/Application Support/Claude/claude_desktop_config.json`:

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

Para HTTP transport (remoto o misma red):

```json
{
  "mcpServers": {
    "teams": {
      "url": "http://localhost:5200"
    }
  }
}
```

---

## ControlPlane — REST API

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `GET` | `/api/tenants` | Lista todos los tenants |
| `GET` | `/api/tenants/{id}` | Detalle de tenant |
| `POST` | `/api/tenants` | Crear tenant (body: `tenantId`, `clientId`, `clientSecret`, `displayName`) |
| `DELETE` | `/api/tenants/{id}` | Eliminar tenant |
| `POST` | `/api/tenants/{id}/consent` | Iniciar flujo de consentimiento |
| `GET` | `/api/tenants/{id}/consent` | Estado de consentimiento |
| `GET` | `/health` | Health check (incluye conteo de tenants y consents) |

Swagger UI disponible en `http://localhost:5100/swagger`.

---

## Panel — Next.js Dashboard

- **Dashboard** (`/`) — Resumen de tenants, estado, health
- **Tenants** (`/tenants`) — CRUD con consent status badges
- **Consents** (`/consents`) — Lista de consentimientos por tenant
- **Health** (`/health`) — Estado de servicios
- **Playground** (`/playground`) — Testing interactivo de MCP tools

Design: Neón M365 (dark, glassmorphism, cyan/magenta neon accents).

---

## CLI — zero-teams

```bash
# Agregar tenant
zero-teams tenant-add --tenant-id <ID> --client-id <ID> --client-secret <SECRET> --display-name "Mi Tenant"

# Listar tenants
zero-teams tenant-list

# Iniciar consentimiento
zero-teams tenant-consent --tenant-id <ID>

# Health check
zero-teams health

# Eliminar tenant
zero-teams tenant-delete --tenant-id <ID>

# Listar herramientas MCP
zero-teams tools

# Probar una herramienta
zero-teams test <tool-name> [--args '{}'] [--json] [--table]
```

---

## Permisos Graph API

| Permiso | Tipo | Tools |
|---------|------|-------|
| `User.Read.All` | Application | Contexto de usuario |
| `Team.ReadBasic.All` | Application | `teams_list_teams` |
| `Channel.ReadBasic.All` | Application | `teams_list_channels`, `teams_create_channel` |
| `ChannelMessage.Read` | Application | `teams_get_chat_messages` |
| `ChannelMessage.Send` | Application | `teams_send_channel_message` |
| `Chat.Read.All` | Application | `teams_list_chats` |
| `Chat.ReadWrite.All` | Application | `teams_create_chat`, `teams_add_chat_member`, `teams_remove_chat_member` |
| `ChatMessage.Send` | Application | `teams_send_message` |
| `OnlineMeetings.ReadWrite` | Application | Meetings (desactivado) |
| `OnlineMeetingAttendance.Read` | Application | Meetings (desactivado) |
| `Calendars.ReadWrite` | Application | Calendar (desactivado) |

---

## Proyectos

```
zero-teams-mcp/
├── ZeroTeamsMcp.slnx
├── src/
│   ├── ZeroTeamsMcp.Core/          # Auth (MSAL), GraphService, Models, CardTemplates
│   ├── ZeroTeamsMcp.Server/        # MCP stdio + HTTP, 19 tools
│   ├── ZeroTeamsMcp.Bot/           # Bot Framework, Adaptive Cards, invoke dispatch
│   ├── ZeroTeamsMcp.ControlPlane/  # REST API tenants, consent, health
│   └── ZeroTeamsMcp.Cli/           # CLI: zero-teams
├── apps/
│   └── panel/                      # Next.js 15 + shadcn/ui
├── tests/
│   └── ZeroTeamsMcp.Tests/         # 20 tests (xUnit)
├── deploy/
│   ├── docker-compose.yml          # 3 servicios
│   ├── Dockerfile.controlplane
│   ├── Dockerfile.panel
│   ├── Dockerfile.server
│   └── .env.example
└── docs/
    ├── arquitectura.md
    ├── alcance-mvp.md
    ├── roadmap.md
    ├── integracion-agentes.md      # SDD: integración con agentes IA
    ├── skills/
    │   └── zero-teams-integration/
    │       └── SKILL.md            # Skill de integración
    └── adr/                        # 6 ADRs
```

---

## Desarrollo

```bash
# Build completo
dotnet build ZeroTeamsMcp.slnx

# Tests
dotnet test tests/ZeroTeamsMcp.Tests/

# Panel
cd apps/panel && pnpm install && pnpm dev

# CI local
dotnet build ZeroTeamsMcp.slnx -c Release && dotnet test tests/ZeroTeamsMcp.Tests/
```

---

## Documentación

- [Arquitectura](docs/arquitectura.md) — Diagrama de componentes, auth flow, multi-tenant, seguridad
- [Alcance MVP](docs/alcance-mvp.md) — 25 tools con schema, permisos y criterios de aceptación
- [Integración con Agentes](docs/integracion-agentes.md) — SDD: cómo conectar agentes IA (Claude Desktop, Cursor, Copilot Studio)
- [Roadmap](docs/roadmap.md) — F0–F8 con entregables y dependencias
- [Copilot Studio Setup](docs/copilot-studio/SETUP.md) — Guía completa de integración con Copilot Studio

### Skills

| Skill | Descripción |
|-------|-------------|
| [zero-teams-integration](docs/skills/zero-teams-integration/SKILL.md) | Workflow de integración con agentes IA |

### Copilot Studio Integration

| Archivo | Descripción |
|---------|-------------|
| [SETUP.md](docs/copilot-studio/SETUP.md) | Guía completa: Azure AD, Bot Framework, Custom Connector |
| [manifest.json](docs/copilot-studio/manifest.json) | Teams App Manifest template |
| [openapi.yaml](docs/copilot-studio/openapi.yaml) | OpenAPI spec para Custom Connector |
| [zeroteams-mcp.zip](docs/copilot-studio/zeroteams-mcp.zip) | App package listo para Teams |

### ADRs

| # | Decisión | Estado |
|---|----------|--------|
| 0001 | Microsoft.Graph SDK 6.5.0 | Aceptada |
| 0002 | Transporte dual (stdio + HTTP) | Aceptada |
| 0003 | App multi-tenant única | Aceptada |
| 0004 | Action.Execute para Adaptive Cards | Aceptada |
| 0005 | ControlPlane REST API | Aceptada |
| 0006 | Design system shadcn + Neón M365 | Aceptada |

---

## Roadmap

| Fase | Estado | Contenido |
|------|--------|-----------|
| F0 | ✅ | Scaffolding: solution, projects, Core, auth, models |
| F1 | ✅ | Chat escritura MVP: 13 Teams tools |
| F2 | ✅ | ControlPlane + Docker + CLI |
| F3 | ✅ | Dashboard Panel Integration |
| F4 | ✅ | Bot Framework + Adaptive Cards |
| F5 | ✅ | MCP Admin Tools (6) |
| F6 | ✅ | Multi-transport (stdio + HTTP) |
| F7 | ✅ | Tests (20) + CI |
| F8 | ✅ | Meetings + Calendar (deferred) |

---

## Licencia

MIT
