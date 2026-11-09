# Release notes — Token efficiency + dev mode (ADR-0010, 2026-11-08)

## Nuevos parámetros MCP (mail)
- `mail_list_messages`: `format=triage|compact|full` (default `full` legacy), `includeQueryEcho` (default `false`), `previewChars`, `fields` (whitelist csv). `triage` recomendado para barridos cronológicos.
- `mail_get_message`: `bodyFormat=text|html|none` (default `html` legacy), `maxBodyChars` 0..8000 (default 8000), `includeHeaders`, `bodyTruncated`.
- `mail_read_attachment`: `offset`, `maxChars` 1..20000 (default 8000), `returnedChars/nextOffset/truncated/totalChars`.
- Nuevo `mail_search_hits`: búsqueda acotada multi-buzón/rango/query, dedupe, `coverage/truncated/nextSuggested` (límites duros). `coverage[].cursor` reanuda unidades pendientes (skip; solo sin `$search`).

## Dev mode
- `MCP_DEV_MODE=true|1`: diagnósticos seguros (Server log Debug + `responseChars` sin PII/cuerpos). **No relaja** auth/API key, `RestrictedAccessGuard`, denylist ni disclosure.
- ControlPlane: Swagger/UI solo con `MCP_DEV_MODE` o `Development`.

## Compatibilidad
- Defaults legacy en fase 1 para minimizar roturas; `ServerDisclosurePolicy` instruye params eficientes. Cambio futuro de defaults será versionado.

## Deploy
- Rebuild/restart del servidor MCP requerido. Ver `docs/SDD-token-efficiency-devmode.md`, `docs/adr/ADR-0010-token-efficient-mcp-responses.md`.
