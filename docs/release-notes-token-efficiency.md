# Release notes — Token efficiency + dev mode (ADR-0010, 2026-11-08)

## Nuevos parámetros MCP (mail)
- `mail_list_messages`: `format=triage|compact|full` (default `full` legacy), `includeQueryEcho` (default `false`), `previewChars`, `fields` (whitelist csv). `triage` recomendado para barridos cronológicos.
- `mail_get_message`: `bodyFormat=text|html|none` (default `html` legacy), `maxBodyChars` 0..8000 (default 8000), `includeHeaders`, `bodyTruncated`.
- `mail_read_attachment`: `offset`, `maxChars` 1..20000 (default 8000), `returnedChars/nextOffset/truncated/totalChars`.
- Nuevo `mail_search_hits`: búsqueda acotada multi-buzón/rango/query, dedupe, `coverage/truncated/nextSuggested` (límites duros). `coverage[].cursor` reanuda unidades pendientes (skip; solo sin `$search`).

## Dev mode
- `MCP_DEV_MODE=true|1`: diagnósticos seguros (Server log Debug + `responseChars` sin PII/cuerpos). **No relaja** auth/API key, `RestrictedAccessGuard`, denylist ni disclosure.
- ControlPlane: Swagger/UI solo con `MCP_DEV_MODE` o `Development`.

## Defaults por entorno (fase 2)
- `MCP_MAIL_DEFAULTS=efficient` (o `1`/`true`): aplica defaults baratos **solo** cuando el cliente no pasa el parámetro — `mail_list_messages.format=triage`, `mail_get_message.bodyFormat=text` y `maxBodyChars=2000`. Sin la variable se mantiene `full`/`html`/`8000` (legacy).
- Un valor explícito del cliente siempre gana. No afecta a auth/guards/denylist.
- Medición local de referencia (mismo buzón, top=10): `mail_list_messages` 7 574 → 3 681 chars (-51%); `mail_get_message` 10 663 → 1 717 chars (-84%).

## stdio
- En `TRANSPORT=stdio` los logs van a **stderr**; stdout queda solo para JSON-RPC (antes se mezclaban y rompían clientes estrictos).

## Compatibilidad
- Defaults legacy sin `MCP_MAIL_DEFAULTS`; con `efficient` el cliente puede forzar `format`/`bodyFormat` explícitos. Cambio de defaults sin env será versionado.

## Tests
- `tests/ZeroTeamsMcp.Tests/MailSearchContractTests.cs`: contrato de red con Graph simulado — cursor → `$skip`, `$search` nunca lleva `$skip`, cursor inválido falla sin llamar a Graph y `coverage.cursor` reanuda (233/233).

## Deploy
- Rebuild/restart del servidor MCP requerido. Ver `docs/SDD-token-efficiency-devmode.md`, `docs/adr/ADR-0010-token-efficient-mcp-responses.md`.
- Producción (`optimafront-1`) activa `MCP_MAIL_DEFAULTS=efficient` desde 2026-11-09; rollback = quitar la línea de `server.env` y reiniciar `daemon-1150054:*`.
