# ADR-0010: Respuestas MCP eficientes en tokens y diagnósticos dev seguros

## Estado

**Propuesto** — 2026-11-08

## Contexto

Las tools de lectura (`mail_list_messages`, `mail_get_message`, adjuntos, chats) devuelven cuerpos HTML, previews largos, eco de filtros y estructuras verbosas. Investigaciones cronológicas (múltiples buzones/rangos/queries + paginación manual) consumen muchos tokens y llamadas, y son propensas a “saltos” si no se reporta cobertura.

Además no existe un *dev mode* explícito para medir tamaños/truncado con seguridad.

Referencias: `docs/SDD-token-efficiency-devmode.md`, `docs/adr/ADR-0002-transporte-dual.md`, `src/ZeroTeamsMcp.Server/Services/ServerDisclosurePolicy.cs`.

## Decisión

Implementar por fases (ver SDD):

1. **Fase 1 (compat):** añadir params opcionales a `mail_list_messages`/`mail_get_message` con defaults **legacy** (`format=full`, `bodyFormat=html`, `maxBodyChars=8000`), omitiendo nulos solo donde aplique; `ServerDisclosurePolicy` instruye `triage`/`text`/`2000` para barridos.
2. **Fase 2:** chunking de adjuntos.
3. **Fase 3:** `mail_search_hits` con límites duros, dedupe, `coverage/truncated/nextSuggested`.
4. **Fase 4:** `MCP_DEV_MODE` para diagnósticos seguros (meta/logs redaccionados, sin PII/secretos). **No relaja** auth, `RestrictedAccessGuard`, denylist ni disclosure.

### Reglas clave
- Seguridad inmutable (R-T2, E1).
- Límites server-side con clamp/validación (R-T4).
- HTML→texto determinista, sin ejecutar HTML (R-T6).
- Cobertura explícita en búsquedas paginadas (R-T7, D6–D7).

## Consecuencias

**Positivas:** menos tokens/llamadas, investigación cronológica auditable, medición segura.

**Costes/riesgos:** más complejidad en tools; defaults legacy reducen rotura inicial pero retrasan ahorro automático; cambio de defaults (fase 3) requiere release notes/OpenAPI.

## Alternativas consideradas

- Solo acortar `Trim` global: simple, insuficiente (HTML, eco, round-trips).
- Cambiar defaults de inmediato: mayor ahorro, riesgo de romper clientes parseando HTML/estructura. Rechazado para fase 1.
- Resumir con LLM: añade coste/latencia y riesgo de omisión; no apto para evidencia.
