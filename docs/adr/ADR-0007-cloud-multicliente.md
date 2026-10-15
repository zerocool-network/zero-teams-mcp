# ADR-0007: Cloud multi-cliente con API key

## Estado

**Aceptado** — 2026-10-15

## Contexto

zero-teams-mcp debía pasar de un host MCP local por tenant a un **cloud remoto** accesible desde cualquier lado. Requisitos:

- Cualquier persona instala el MCP en Claude y apunta a una URL pública.
- Al instalar, hace un `register` con su email Teams (ej. `noe@optimaretail.es`).
- El admin (único) usa wildcard `*` para consultar cualquier usuario; los clientes regulares quedan limitados a su propio email.
- La credencial Azure (app-only, `Chat.Read.All`) ya está consensuada en el tenant y lee chats 1:1 y grupales.

### Opciones consideradas

1. **API key por cliente, resuelta por el ControlPlane** — cada registro devuelve una key única; el Server la valida por request.
2. **Email declarado sin key** — el cliente manda solo su email; el server confía.
3. **JWT/OAuth por cliente** — flujo de login completo por usuario.

## Decisión

Implementamos **API key por cliente** (opción 1), con el ControlPlane como autoridad de registro/validación.

- `clients` (tabla SQLite): `key_hash` (SHA-256), `key_prefix`, `email`, `is_admin`, `revoked`, `created_at`, `last_used_at`. La key en texto plano se devuelve una sola vez.
- `POST /api/clients/register` (email o adminToken), `GET /api/clients/validate`, `GET/DELETE /api/clients`.
- El Server HTTP autentica el header `X-Api-Key` en `/mcp` contra el ControlPlane, con caché en memoria (TTL 5 min). Sin key válida → 401.
- `ClientContext` (scoped) expone `IsAdmin`/`Email` a los tools. `teams_list_chats` usa `ResolveTarget(userId)`: admin usa el `userId` pedido; cliente regular fuerza su email.
- Registro admin requiere `ADMIN_TOKEN` (env del ControlPlane); email del admin se marca como `*`.

## Consecuencias

### Positivas

- Un solo server HTTP sirve a N clientes, con aislamiento por email.
- Keys revocables y auditables (`last_used_at`, hash en disco).
- Sin credenciales Azure en el cliente: solo una API key.
- Scoping duro en el entry point de lectura (`teams_list_chats`).

### Negativas

- ControlPlane es dependencia del Server para autenticar (fall closed si cae).
- `teams_get_chat_messages` acepta cualquier `chatId` conocido; mitigado por IDs aleatorios solo obtenibles vía `list_chats` scoped.
- Tools de escritura disponibles para todo cliente autenticado.

### Mitigaciones

- Caché de key en el Server para no golpear el ControlPlane en cada tool call.
- Verificación de pertenencia al chat (membresía Graph) como hardening futuro para `get_chat_messages`.
- Guard de escritura solo-admin si se requiere.
