# Alcance MVP — zero-teams-mcp

## Objetivo

El MVP de zero-teams-mcp provee acceso de lectura y escritura a Microsoft Teams vía MCP, cubriendo chats, canales, miembros, mensajes y administración multi-tenant. Incluye autenticación app-only, Bot Framework con Adaptive Cards, panel de administración y CLI.

**Total: 25 MCP tools (13 Teams + 5 Meetings + 4 Calendar + 6 Admin)**

---

## Tools del MCP

| Tool | Método Graph API | Descripción | Lectura/Escritura |
|------|-----------------|-------------|-------------------|
| `teams_list_teams` | `GET /v1.0/me/joinedTeams` | Lista equipos del usuario autenticado | Lectura |
| `teams_get_team` | `GET /v1.0/teams/{team-id}` | Detalle de un equipo específico | Lectura |
| `teams_list_channels` | `GET /v1.0/teams/{team-id}/channels` | Lista canales de un equipo | Lectura |
| `teams_get_channel` | `GET /v1.0/teams/{team-id}/channels/{channel-id}` | Detalle de un canal | Lectura |
| `teams_list_members` | `GET /v1.0/teams/{team-id}/members` | Miembros de un equipo | Lectura |
| `teams_get_member` | `GET /v1.0/teams/{team-id}/members/{member-id}` | Detalle de un miembro | Lectura |
| `teams_search_messages` | `GET /v1.0/teams/{team-id}/channels/{channel-id}/messages?$filter` | Buscar mensajes por texto | Lectura |
| `teams_get_message` | `GET /v1.0/teams/{team-id}/channels/{channel-id}/messages/{message-id}` | Detalle de un mensaje | Lectura |
| `teams_list_chats` | `GET /v1.0/me/chats` | Chats 1:1 y grupales del usuario | Lectura |
| `teams_send_message` | `POST /v1.0/teams/{team-id}/channels/{channel-id}/messages` | Enviar mensaje a canal | Escritura |
| `teams_send_chat_message` | `POST /v1.0/me/chats/{chat-id}/messages` | Enviar mensaje a chat | Escritura |
| `teams_reply_message` | `POST /v1.0/teams/{team-id}/channels/{channel-id}/messages/{message-id}/replies` | Responder a un hilo | Escritura |
| `teams_create_channel` | `POST /v1.0/teams/{team-id}/channels` | Crear canal nuevo | Escritura |

### Detalle de cada tool

#### teams_list_teams

```json
{
  "name": "teams_list_teams",
  "description": "Lista todos los equipos a los que pertenece el usuario autenticado",
  "inputSchema": {
    "type": "object",
    "properties": {},
    "required": []
  }
}
```

**Graph API**: `GET /v1.0/me/joinedTeams`
**Permisos**: `Team.ReadBasic.All`
**Respuesta**: Array de objetos `Team` con `id`, `displayName`, `description`, `webUrl`.

---

#### teams_get_team

```json
{
  "name": "teams_get_team",
  "description": "Obtiene el detalle de un equipo específico",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": {
        "type": "string",
        "description": "ID del equipo"
      }
    },
    "required": ["teamId"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}`
**Permisos**: `Team.ReadBasic.All`

---

#### teams_list_channels

```json
{
  "name": "teams_list_channels",
  "description": "Lista todos los canales de un equipo",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": {
        "type": "string",
        "description": "ID del equipo"
      }
    },
    "required": ["teamId"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}/channels`
**Permisos**: `Channel.ReadBasic.All`

---

#### teams_get_channel

```json
{
  "name": "teams_get_channel",
  "description": "Obtiene el detalle de un canal específico",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "channelId": { "type": "string", "description": "ID del canal" }
    },
    "required": ["teamId", "channelId"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}/channels/{channel-id}`
**Permisos**: `Channel.ReadBasic.All`

---

#### teams_list_members

```json
{
  "name": "teams_list_members",
  "description": "Lista los miembros de un equipo",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" }
    },
    "required": ["teamId"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}/members`
**Permisos**: `Team.ReadBasic.All`

---

#### teams_get_member

```json
{
  "name": "teams_get_member",
  "description": "Obtiene el detalle de un miembro del equipo",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "memberId": { "type": "string", "description": "ID del miembro" }
    },
    "required": ["teamId", "memberId"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}/members/{member-id}`
**Permisos**: `Team.ReadBasic.All`

---

#### teams_search_messages

```json
{
  "name": "teams_search_messages",
  "description": "Busca mensajes en un canal por texto contenido",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "channelId": { "type": "string", "description": "ID del canal" },
      "query": { "type": "string", "description": "Texto a buscar" },
      "top": { "type": "integer", "description": "Número máximo de resultados", "default": 10 }
    },
    "required": ["teamId", "channelId", "query"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}/channels/{channel-id}/messages?$filter=contains(body/content,'{query}')`
**Permisos**: `ChannelMessage.Read`

---

#### teams_get_message

```json
{
  "name": "teams_get_message",
  "description": "Obtiene el detalle de un mensaje específico",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "channelId": { "type": "string", "description": "ID del canal" },
      "messageId": { "type": "string", "description": "ID del mensaje" }
    },
    "required": ["teamId", "channelId", "messageId"]
  }
}
```

**Graph API**: `GET /v1.0/teams/{team-id}/channels/{channel-id}/messages/{message-id}`
**Permisos**: `ChannelMessage.Read`

---

#### teams_list_chats

```json
{
  "name": "teams_list_chats",
  "description": "Lista los chats del usuario (1:1 y grupales)",
  "inputSchema": {
    "type": "object",
    "properties": {
      "top": { "type": "integer", "description": "Número máximo de resultados", "default": 20 }
    },
    "required": []
  }
}
```

**Graph API**: `GET /v1.0/me/chats?$top={top}`
**Permisos**: `Chat.Read.All`

---

#### teams_send_message

```json
{
  "name": "teams_send_message",
  "description": "Envía un mensaje a un canal de Teams",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "channelId": { "type": "string", "description": "ID del canal" },
      "content": { "type": "string", "description": "Contenido del mensaje (HTML)" }
    },
    "required": ["teamId", "channelId", "content"]
  }
}
```

**Graph API**: `POST /v1.0/teams/{team-id}/channels/{channel-id}/messages`
**Permisos**: `ChannelMessage.Send`

---

#### teams_send_chat_message

```json
{
  "name": "teams_send_chat_message",
  "description": "Envía un mensaje a un chat 1:1 o grupal",
  "inputSchema": {
    "type": "object",
    "properties": {
      "chatId": { "type": "string", "description": "ID del chat" },
      "content": { "type": "string", "description": "Contenido del mensaje (HTML)" }
    },
    "required": ["chatId", "content"]
  }
}
```

**Graph API**: `POST /v1.0/me/chats/{chat-id}/messages`
**Permisos**: `ChatMessage.Send`

---

#### teams_reply_message

```json
{
  "name": "teams_reply_message",
  "description": "Responde a un mensaje existente en un canal",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "channelId": { "type": "string", "description": "ID del canal" },
      "messageId": { "type": "string", "description": "ID del mensaje a responder" },
      "content": { "type": "string", "description": "Contenido de la respuesta (HTML)" }
    },
    "required": ["teamId", "channelId", "messageId", "content"]
  }
}
```

**Graph API**: `POST /v1.0/teams/{team-id}/channels/{channel-id}/messages/{message-id}/replies`
**Permisos**: `ChannelMessage.Send`

---

#### teams_create_channel

```json
{
  "name": "teams_create_channel",
  "description": "Crea un canal nuevo en un equipo",
  "inputSchema": {
    "type": "object",
    "properties": {
      "teamId": { "type": "string", "description": "ID del equipo" },
      "displayName": { "type": "string", "description": "Nombre del canal" },
      "description": { "type": "string", "description": "Descripción del canal" }
    },
    "required": ["teamId", "displayName"]
  }
}
```

**Graph API**: `POST /v1.0/teams/{team-id}/channels`
**Permisos**: `Channel.CreateRestricted`

---

## Permisos Graph API

### Permisos necesarios para el MVP

| Permiso | Tipo | Tools que lo usan |
|---------|------|--------------------|
| `User.Read.All` | Delegado | Contexto de usuario autenticado |
| `Team.ReadBasic.All` | Delegado | `teams_list_teams`, `teams_get_team`, `teams_list_members`, `teams_get_member` |
| `Channel.ReadBasic.All` | Delegado | `teams_list_channels`, `teams_get_channel` |
| `ChannelMessage.Read` | Delegado | `teams_search_messages`, `teams_get_message` |
| `ChannelMessage.Send` | Delegado | `teams_send_message`, `teams_reply_message` |
| `Chat.Read.All` | Delegado | `teams_list_chats` |
| `Chat.ReadWrite.All` | Delegado | — (reservado post-MVP) |
| `ChatMessage.Send` | Delegado | `teams_send_chat_message` |

### Notas sobre permisos

- Todos los permisos son **delegados** (no application) porque el modelo usa client credentials con admin consent
- `Channel.CreateRestricted` puede requerir licencia específica de Teams
- Permisos adicionales se añaden en fases post-MVP

---

## Tools Admin (ControlPlane)

| Tool | Método HTTP | Endpoint | Descripción |
|------|-------------|----------|-------------|
| `admin_list_tenants` | `GET` | `/api/tenants` | Lista todos los tenants |
| `admin_get_tenant` | `GET` | `/api/tenants/{id}` | Detalle de un tenant |
| `admin_create_tenant` | `POST` | `/api/tenants` | Registrar nuevo tenant |
| `admin_delete_tenant` | `DELETE` | `/api/tenants/{id}` | Eliminar tenant |
| `admin_get_consent` | `GET` | `/api/tenants/{id}/consent` | Estado de consentimiento |
| `admin_health` | `GET` | `/health` | Health check del ControlPlane |

---

## Tools Meetings

| Tool | Método Graph API | Descripción | Lectura/Escritura |
|------|-----------------|-------------|-------------------|
| `teams_list_meetings` | `GET /me/onlineMeetings` | Lista reuniones online | Lectura |
| `teams_get_meeting` | `GET /me/onlineMeetings/{id}` | Detalle de una reunión | Lectura |
| `teams_create_meeting` | `POST /me/onlineMeetings` | Crea reunión online | Escritura |
| `teams_delete_meeting` | `DELETE /me/onlineMeetings/{id}` | Elimina reunión | Escritura |
| `teams_get_meeting_attendance` | `GET /me/onlineMeetings/{id}/attendanceReports` | Reporte de asistencia | Lectura |

---

## Tools Calendar

| Tool | Método Graph API | Descripción | Lectura/Escritura |
|------|-----------------|-------------|-------------------|
| `teams_list_calendar_events` | `GET /me/calendarView` | Lista eventos del calendario | Lectura |
| `teams_get_calendar_event` | `GET /me/events/{id}` | Detalle de un evento | Lectura |
| `teams_create_calendar_event` | `POST /me/events` | Crea evento (con opción online meeting) | Escritura |
| `teams_delete_calendar_event` | `DELETE /me/events/{id}` | Elimina evento | Escritura |

---

## Post-MVP

Funcionalidades planificadas para después del MVP:

| Funcionalidad | Graph API | Tools estimados |
|---------------|-----------|-----------------|
| Files | `/v1.0/teams/{id}/channels/{id}/filesFolder` | `teams_list_files`, `teams_upload_file`, `teams_download_file` |
| Policy Admin | Graph Beta endpoints | `teams_list_policies`, `teams_assign_policy` |
| Apps | `/v1.0/appCatalogs/teamsApps` | `teams_list_apps`, `teams_install_app` |

---

## Criterios de aceptación del MVP

1. **Autenticación**: Flujo client credentials con admin consent funcional ✅
2. **Tools Teams**: 13 tools de lectura y escritura (chats, canales, miembros) ✅
3. **Tools Admin**: 6 tools para gestión multi-tenant vía ControlPlane ✅
4. **Transporte**: stdio funcional con Claude Desktop; HTTP funcional con curl ✅
5. **ControlPlane**: API REST con CRUD de tenants y health check ✅
6. **Bot Framework**: Adaptive Cards con Action.Execute para interacciones Teams ✅
7. **Panel**: Dashboard Next.js con lista de tenants, consents y health ✅
8. **CLI**: Comandos para gestión de tenants desde terminal ✅
9. **Tests**: 20 tests (xUnit) — CardTemplates + TenantStore ✅
10. **CI**: GitHub Actions build + test + panel build ✅
