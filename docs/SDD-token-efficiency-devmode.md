# SDD — Token Efficiency & Dev Mode (zero-teams-mcp)

**Estado:** Propuesto (borrador para implementación)  
**Fecha:** 2026-11-08  
**Ámbito:** `ZeroTeamsMcp.Server` (MCP tools + disclosure), `ZeroTeamsMcp.ControlPlane` (gating diagnóstico), tests.  
**Motivación:** Reducir consumo de tokens LLM y nº de llamadas en barridos/investigaciones (correo/chats), y añadir un *dev mode* seguro para medir/depurar sin filtrar datos.

## 1. Objetivos
- Bajar tokens por respuesta (cuerpos, previews, eco de filtros, campos innecesarios).
- Bajar round-trips (paginación/cobertura explícita; herramienta de búsqueda acotada).
- Hacer medible el coste (metadatos de tamaño/truncado solo en dev o bajo flag).
- Mantener seguridad: auth, `RestrictedAccessGuard`/denylist, disclosure, sin PII/secretos en logs o meta expuesta indebidamente.

## 2. No objetivos
- No cambiar permisos Graph ni semántica de acceso.
- No desactivar controles de seguridad en dev mode.
- No garantizar extracción de adjuntos cuando Graph no entrega bytes (limitación conocida; ver `mail_read_attachment`/`download`).
- No optimizar Teams chats en fase 1 salvo patrón reusable (fase 2).

## 3. Reglas transversales (aplican a todas las implementaciones)

| Regla | Condición | Comportamiento |
|---|---|---|
| R-T1 Compatibilidad | Cliente no envía nuevos params | Mantener salida **legacy** por defecto en release inicial **o** activar defaults nuevos solo con `compat=false` explícito en fase de migración. Decisión de rollout: ver §9. |
| R-T2 Seguridad | Siempre | `AuthenticatedUserIdValidator`, `RestrictedAccessGuard`, `CloudAuth` (HTTP `/mcp`), denylist y visibilidad de correo **antes** de devolver datos. Ningún cambio de formato relaja esto. |
| R-T3 Sin secretos/PII en meta | `meta`/logs | Prohibido incluir `X-Api-Key`, tokens, `ENCRYPTION_KEY`, cuerpos completos, adjuntos, ni emails sin necesidad. En dev, preferir conteos/hashes/flags. |
| R-T4 Límites duros | Cualquier `top/maxChars/maxCalls/maxHits` | `Math.Clamp`/validación server-side; rechazar valores fuera de rango con `Error` estable. |
| R-T5 Omisión de nulos | Respuestas JSON | Omitir campos `null`/vacíos no esenciales cuando el formato sea `compact|triage` (o global si se define política); no omitir campos de error/IDs críticos. |
| R-T6 Texto plano seguro | `bodyFormat=text` | HTML→texto determinista (strip tags, decode entidades, colapsar whitespace, preservar saltos razonables). No ejecutar/interpretar HTML. |
| R-T7 Cobertura explícita | Búsquedas paginadas/rango | Respuesta incluye `before/nextPage/coverage/truncated` según aplique para evitar “saltos de fechas”. |
| R-T8 Errores estables | Fallos | Objeto `{ error, message?, ... }`/código OData mapeado (`GraphError`) sin stack traces al cliente salvo futuro flag dev **nunca** con datos sensibles. |
| R-T9 Idempotencia lectura | Tools read-only | Sin efectos secundarios; mismos inputs → mismo resultado salvo cambios en Graph. |

## 4. Implementación A — `mail_list_messages` (formato compacto/triage)

**Archivos:** `src/ZeroTeamsMcp.Server/Tools/MailDriveTools.cs` (`ListMessages`, `Prepare/Configure/Trim`), `src/ZeroTeamsMcp.Server/Services/MailListQuery.cs` (si aplica helpers), tests `tests/ZeroTeamsMcp.Tests/MailListQueryTests.cs` + nuevos.

### 4.1 API (params nuevos, opcionales)
| Param | Tipo | Default | Condición | Regla |
|---|---|---|---:|---|
| `format` | `compact` \| `triage` \| `full` | `compact`* | inválido → `Error="format"` | `triage` minimiza tokens; `full` = legacy. (*ver §9) |
| `fields` | string[]/csv | null | valores fuera de whitelist → ignorar o error; definir whitelist | Restringe campos por mensaje. |
| `includeQueryEcho` | bool | `false` | — | Si `false`, omite `sinceDate/untilDate/fromAddress/toAddress/query/subject/hasAttachments` en respuesta. |
| `previewChars` | int | 120 (`compact`), 0 (`triage`), 280 (`full`) | clamp 0..280 | Solo si preview habilitado. |

**Whitelist `fields`:** `id,subject,from,to,receivedDateTime,sentDateTime,hasAttachments,preview,mailbox,folder`.

### 4.2 Reglas/condiciones
- **A1** `format=triage`: por mensaje solo `{ id, subject, from, receivedDateTime|sentDateTime (según `dateField`/presencia), hasAttachments }` + `mailbox,folder` solo si se piden/útil; **sin** `preview`, **sin** `to`.
- **A2** `format=compact`: incluye `preview` texto plano, `previewChars` clamp; omite `To` si vacío.
- **A3** `format=full`: replica comportamiento actual (preview 280, eco permitido si `includeQueryEcho=true` o compat).
- **A4** Respuesta mínima: siempre `folder,page,mode,count,nextPage,before,messages`; eco solo si `includeQueryEcho=true`.
- **A5** `mode=search|list` preservado.
- **A6** Con `RestrictedAccessGuard.IsRestricted`, filtrado **antes** de proyectar/formatear; `count` refleja visibles.
- **A7** `before` se calcula sobre mensajes visibles (como hoy, pero consistente con filtro).

### 4.3 Errores
- `format` inválido → `{ error:"format", message }`.
- Mantener `folder/date/search` errores existentes.

### 4.4 Tests
- Formato `triage` no contiene `preview`/`to`/eco.
- `compact` respeta `previewChars` y plaintext.
- `full` compat.
- `includeQueryEcho=false` omite filtros.
- Clamp límites.

## 5. Implementación B — `mail_get_message` (body texto + límites)

**Archivos:** `MailDriveTools.cs` (`GetMessage`), util HTML→texto (nuevo helper interno o `TeamsFileTools`/`TextUtil`), tests.

### 5.1 API
| Param | Tipo | Default | Condición |
|---|---|---:|---|
| `bodyFormat` | `text` \| `html` \| `none` | `text`* | inválido → error |
| `maxBodyChars` | int | `2000` | clamp `0..8000` |
| `includeHeaders` | bool | `true` | — |

(*ver §9)

### 5.2 Reglas/condiciones
- **B1** `bodyFormat=none`: no incluye `body` (ahorra tokens); mantener metadatos si `includeHeaders=true`.
- **B2** `bodyFormat=text`: convertir `m.Body.Content` a texto (R-T6), luego `Trim(..., maxBodyChars)`. Si cuerpo vacío → `body:null`/omitido.
- **B3** `bodyFormat=html`: devolver HTML truncado a `maxBodyChars` (legacy).
- **B4** Incluir `bodyTruncated: true|false` y `bodyFormat` en respuesta (barato, útil).
- **B5** `includeHeaders=false`: solo `id,subject,from,body?,bodyTruncated?,bodyFormat,hasAttachments` mínimos.
- **B6** `Select` de Graph: mantener `body` solo si `bodyFormat!=none`; considerar `bodyPreview` no necesario aquí.
- **B7** `RestrictedAccessGuard.EnsureMailVisible` antes de retornar.

### 5.3 Tests
- HTML complejo → texto legible, sin tags.
- `maxBodyChars` clamp y `bodyTruncated`.
- `none` omite body.

## 6. Implementación C — `mail_read_attachment` (chunking)

**Archivos:** `MailDriveTools.cs` (`ReadAttachment`).

### 6.1 API
| Param | Tipo | Default | Condición |
|---|---|---:|---|
| `offset` | int | `0` | `>=0`, si `> length` → texto vacío + `truncated=false` |
| `maxChars` | int | `8000` | clamp `1..20000` |

### 6.2 Reglas/condiciones
- **C1** Extraer texto como hoy (`AttachmentText`) **una vez**; luego slice `[offset, offset+maxChars)`.
- **C2** Respuesta: `{ name, contentType, size, text, offset, returnedChars, nextOffset|null, truncated, totalChars }`.
- **C3** `nextOffset=null` cuando no hay más. `truncated=true` si queda texto.
- **C4** Mantener límite de archivo 15MB y tope interno actual (hoy `Trim(text, 50000)`) — chunking opera sobre texto ya acotado; documentar.
- **C5** Errores existentes (`noContent/tooLarge`) preservados.

### 6.3 Tests
- Paginación offset/nextOffset.
- Clamp `maxChars`.

## 7. Implementación D — `mail_search_hits` (investigación acotada, anti-saltos)

**Archivos:** nuevo tool en `MailDriveTools.cs` o `MailInvestigationTools.cs`; servicio `MailSearchHitsService.cs` (o en `Services/`); reutiliza `MailListQuery`/`ListMessages` lógica.

### 7.1 API
```json
{
  "userIds": ["a@..."],
  "folders": ["sent","inbox"],
  "ranges": [{"since":"YYYY-MM-DD","until":"YYYY-MM-DD"}],
  "queries": ["Desvinculación","BENITO"],
  "fromAddress": null, "toAddress": null, "subject": null,
  "maxHits": 50,
  "maxCalls": 12,
  "format": "triage"
}
```

### 7.2 Reglas/condiciones
- **D1** Límites duros: `maxHits` clamp 1..200, `maxCalls` clamp 1..30, `userIds` máx 10, `ranges` máx 12, `queries` máx 10. Exceso → clamp o error explícito (elegir consistente: **clamp + reportar** en `limitsApplied`).
- **D2** Validar fechas por rango con `MailListQuery.TryBound/TryFilter`; rango inválido → error `date` con índice.
- **D3** Para cada `(user,folder,range,query)` ejecutar búsqueda; **parar** al alcanzar `maxCalls` o `maxHits`.
- **D4** Dedupe por `messageId+mailbox` (o `id` estable); acumular `matchedBy: ["query"...]`.
- **D5** Respuesta **triage**: hits `{ id, mailbox, folder, subject, from, date, hasAttachments, matchedBy }`.
- **D6** `coverage`: lista de unidades ejecutadas y **pendientes** `{ userId, folder, since, until, query, status: done|pending|error, callsUsed }`. **Nunca** ocultar que quedó truncado.
- **D7** `truncated:true` si cortó por límites o quedaron pendientes; incluir `nextSuggested` (rangos/queries pendientes) para continuación determinista.
- **D8** Seguridad: `EnsureUserAsync` + visibilidad por cada buzón/mensaje según aplique; respetar restricted/denylist.
- **D9** No auto-paginar infinitamente cada búsqueda: máx N páginas por unidad (ej. 2) dentro de `maxCalls`; registrar cursor pendiente en `coverage`.

### 7.2.1 Cursor reanudable (implementado)
- `coverage[].cursor`: token compacto base64url (`{u,f,s,n,q,k}`) para reanudar por `skip`.
- Entrada: `cursor` (ignora `userIds/folders/ranges/queries/fromAddress/toAddress/subject`).
- Solo se emite en unidades **sin `$search`** (filtros/query vacíos); con `$search` Graph no soporta `$skip`, se devuelve `pendingCursor` (nextLink truncado) y `cursor:null`.
- `nextSuggested[].cursor` permite continuación directa sin reconstruir filtros.

### 7.3 Errores
- Validación → `error` + `coverage` parcial si útil.

### 7.4 Tests
- Dedupe y `matchedBy`.
- Respeta `maxCalls/maxHits`, `truncated`, `coverage`.
- Rangos inválidos.
- Denylist/restricted filtra.

## 8. Implementación E — Dev Mode (seguro, observabilidad token)

**Archivos:** `src/ZeroTeamsMcp.Server/Program.cs`, `src/ZeroTeamsMcp.ControlPlane/Program.cs`, `src/ZeroTeamsMcp.Server/Tools/ToolLogger.cs` (o middleware), `MailDriveTools.cs` (meta opcional), config/docs.

### 8.1 API/config
| Señal | Tipo | Default | Regla |
|---|---|---:|---|
| `MCP_DEV_MODE` | bool env | `false` | Habilita diagnósticos **seguros**. |
| `ASPNETCORE_ENVIRONMENT` | env | — | Informativo; no relaja seguridad. |

### 8.2 Reglas/condiciones
- **E1** Seguridad inmutable: dev mode **no** desactiva `CloudAuth`, API key, `RestrictedAccessGuard`, disclosure, denylist.
- **E2** `meta` diagnóstica (solo si `MCP_DEV_MODE=true` **y** `includeMeta=true` param, default `false`): `{ responseCharsApprox, items, bodyTruncated, format }`. Sin PII/cuerpos/IDs sensibles más allá de conteos. Alternativa conservadora: **solo logs**.
- **E3** Logging: si dev, permitir `Debug` vía config; `ToolLogger` añade `responseBytes/chars`, `graphCalls`, `truncated` — **redactar** args sensibles (`X-Api-Key`, cuerpos, tokens).
- **E4** ControlPlane Swagger: **recomendado** gatear a dev/admin (`if (app.Environment.IsDevelopment() || MCP_DEV_MODE)`) — cambio de comportamiento; tratar como hardening separado con flag de compat.
- **E5** Errores al cliente: siempre compactos/sin stack (R-T8). Stack solo en logs dev.
- **E6** CORS/headers: no relajar en dev por defecto.

### 8.3 Tests
- Con dev off, no aparece `meta` aunque se pida (o se ignora).
- Guards siguen activos.

## 9. Rollout y compatibilidad
- **Fase 1 (segura):** añadir params nuevos; defaults = **legacy** (`format=full`, `bodyFormat=html`, `maxBodyChars=8000`) + `ServerDisclosurePolicy` instruye usar `triage/text/2000`. Minimiza rotura.
- **Fase 2 (opt-in):** clientes pasan nuevos params; medir con dev meta/logs.
- **Fase 3 (breaking, versionada):** cambiar defaults a `compact/text/2000`; anunciar en release notes/ADR; mantener `format=full,bodyFormat=html` como escape.
- Actualizar `docs/copilot-studio/openapi.yaml` si expone estos tools.

## 10. Criterios de aceptación global
- Mismos permisos/visibilidad que hoy en casos existentes (tests de `RestrictedAccessGuard` pasan).
- Barrido equivalente al caso “carta Luis” realizable con menos llamadas/respuestas usando `triage` + `mail_search_hits` + `mail_get_message(bodyFormat=text,maxBodyChars=2000)`.
- `coverage/truncated` evita falsos negativos por paginación/rangos.
- Ningún log/meta filtra secretos/PII.

## 11. ADRs/referencias
- Nuevo: **ADR-0010 — Token-Efficient MCP Responses & Safe Dev Diagnostics** (recomendado).
- Relacionados: ADR-0002 (transporte), `docs/MEJORAS_POST_FORENSIC.md` (hardening), `ServerDisclosurePolicy.cs`.
