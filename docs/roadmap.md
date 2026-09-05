# Roadmap — zero-teams-mcp

## Visión general

El desarrollo se divide en 9 fases iterativas. Cada fase produce un incremento funcional visible y testeable. El orden respeta dependencias técnicas: core antes que bot, auth antes que tools, infra antes que UI.

---

## F0: Scaffolding + Docs

**Estado**: ✅ Completada

### Objetivo
Establecer estructura del proyecto, solution, 5 proyectos, auth MSAL, GraphService, modelo de datos, documentación.

### Entregables
- Solución .NET 10 (`ZeroTeamsMcp.slnx`)
- Proyectos: Core, Server, Bot, ControlPlane, CLI
- Core: `GraphAuthProvider` (MSAL), `GraphService` (Kiota), `TenantConfig`, `CardTemplates` base
- 6 ADRs + arquitectura.md + alcance-mvp.md + roadmap.md

---

## F1: Chat Escritura MVP

**Estado**: ✅ Completada

### Objetivo
13 MCP tools para Teams: chats, canales, miembros, mensajes.

### Entregables
- `TeamsChatTools.cs` — 4 tools: list_chats, create_chat, send_message, get_chat_messages
- `TeamsChannelTools.cs` — 3 tools: list_channels, create_channel, send_channel_message
- `TeamsMemberTools.cs` — 4 tools: get_chat_members, add_chat_member, remove_chat_member, get_channel_members
- `TeamsListTeamsTool.cs` — 1 tool: list_teams
- `TeamsModels.cs` — ChatInfo, ChannelInfo, ChatMessageInfo, MemberInfo
- DI wiring: `GraphAuthProvider` + `GraphService` en Server Program.cs

---

## F2: ControlPlane + Docker

**Estado**: ✅ Completada

### Objetivo
REST API para gestión multi-tenant, CLI real, Docker Compose production-ready.

### Entregables
- ControlPlane: `TenantStore` (JSON persistence), 7 endpoints CRUD + consent + health
- CLI: 5 comandos reales (tenant-add, tenant-list, tenant-consent, health, tenant-delete)
- Server: dual transport (TRANSPORT=http → HTTP/SSE, TRANSPORT=stdio)
- Docker: docker-compose.yml (3 servicios), 3 Dockerfiles, .env.example

---

## F3: Dashboard Panel Integration

**Estado**: ✅ Completada

### Objetivo
Panel Next.js con datos reales del ControlPlane.

### Entregables
- 4 rutas: `/` (dashboard), `/tenants`, `/consents`, `/health`
- `src/lib/api.ts` — typed fetch wrapper a ControlPlane
- `src/lib/types.ts` — TenantConfig, ConsentRecord, HealthResponse
- Sidebar neon M365, auto-refresh 30s
- Build: pnpm build (7 routes)

---

## F4: Bot Framework

**Estado**: ✅ Completada

### Objetivo
Bot Framework con Adaptive Cards Action.Execute para interacciones Teams.

### Entregables
- `CardTemplates.cs` — 9 builders (welcome, chat list, message list, team list, channel list, send prompt, sent confirmation, error)
- `TeamsActivityHandler.cs` — invoke dispatch: list_chats, list_teams, get_chat_messages, list_channels, send_channel_message_prompt
- Text commands: help, chats, teams
- Program.cs: DI completa

---

## F5: MCP Admin Tools

**Estado**: ✅ Completada

### Objetivo
6 tools MCP para gestión del ControlPlane desde clientes MCP.

### Entregables
- `AdminTools.cs` — admin_list_tenants, admin_get_tenant, admin_create_tenant, admin_delete_tenant, admin_get_consent, admin_health
- IHttpClientFactory para calls al ControlPlane

---

## F6: Multi-Transport

**Estado**: ✅ Completada

### Objetivo
Soporte dual stdio + HTTP/SSE en un solo host.

### Entregables
- `TRANSPORT=http` → `WebApplication` + `MapMcp()` en `:5200`
- `TRANSPORT=stdio` → `Host.CreateApplicationBuilder` + `WithStdioServerTransport()`

---

## F7: Tests + CI

**Estado**: ✅ Completada

### Objetivo
Tests unitarios y pipeline CI.

### Entregables
- 20 tests xUnit: CardTemplates (8) + TenantStore (12) — ALL PASS
- CI: `.github/workflows/ci.yml` — .NET build + test + Next.js panel build

---

## F8: Meetings + Calendar

**Estado**: ✅ Completada

### Objetivo
Expandir MCP tools con gestión de reuniones online y calendario.

### Entregables
- `TeamsMeetingTools.cs` — 5 tools: list_meetings, get_meeting, create_meeting, delete_meeting, get_meeting_attendance
- `TeamsCalendarTools.cs` — 4 tools: list_calendar_events, get_calendar_event, create_calendar_event, delete_calendar_event
- Permisos: OnlineMeetings.ReadWrite, OnlineMeetingAttendance.Read, Calendars.ReadWrite

### Graph SDK Type Notes
- `OnlineMeeting`: no `Status`, `IsCancelled`, `IsRecording`, `IsTranscription`, `AttendanceReportCount` — usar propiedades base
- `MeetingParticipantInfo`: no `UphistoryIdentity` — solo `Identity`, `Role`, `Upn`
- `MeetingAttendanceReport`: no `UnifiedRoles` — usar `AttendanceRecords`, `TotalParticipantCount`
- `Tab`: tipo no existe en SDK v6.5 — pendiente para futuro
- `DateTimeTimeZone`: debe crearse explícitamente, no con anonymous types

---

## Resumen visual

```
F0 ──→ F1 ──→ F2 ──→ F3 ──→ F5 ──→ F6
 │              │       │       │
 └──────→ F4 ──┘       └──→ F7 ──→ F8
```

### Estado actual

| Fase | Estado | Tools |
|------|--------|-------|
| F0 | ✅ | — |
| F1 | ✅ | 13 Teams |
| F2 | ✅ | — |
| F3 | ✅ | — |
| F4 | ✅ | — |
| F5 | ✅ | 6 Admin |
| F6 | ✅ | — |
| F7 | ✅ | 20 tests |
| F8 | ✅ | 9 (5 meetings + 4 calendar) |

**Total: 25 MCP tools, 20 tests, 0 build errors**

---

## Post-MVP

| Funcionalidad | Graph API | Tools estimados |
|---------------|-----------|-----------------|
| Files | `/v1.0/teams/{id}/channels/{id}/filesFolder` | `teams_list_files`, `teams_upload_file`, `teams_download_file` |
| Policy Admin | Graph Beta endpoints | `teams_list_policies`, `teams_assign_policy` |
| Apps | `/v1.0/appCatalogs/teamsApps` | `teams_list_apps`, `teams_install_app` |
| Tabs | `/v1.0/teams/{id}/channels/{id}/tabs` | `teams_list_tabs`, `teams_create_tab` (SDK v6.5 lacks Tab entity) |
