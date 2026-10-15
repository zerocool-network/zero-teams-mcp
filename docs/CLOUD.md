# Cloud — acceso remoto multi-cliente

El MCP server expuesto como cloud para que cualquier persona, desde cualquier lado, lo use vía HTTP sin correr su propio host. Cada cliente se registra con su email y recibe una API key.

## Modelo de seguridad

| Actor | Registro | Acceso |
|-------|----------|--------|
| **Cliente regular** | `register --email noe@...` | Limitado a su propio email. `teams_list_chats` fuerza su email. |
| **Admin** (`*`) | `register --admin-token <ADMIN_TOKEN>` | Ve cualquier usuario. Pasa cualquier `userId`. |

La API key viaja en el header `X-Api-Key`. El ControlPlane guarda solo el hash SHA-256 de la key; el texto plano se devuelve una sola vez al registrar.

## Endpoints nuevos (ControlPlane)

| Método | Ruta | Descripción |
|--------|------|-------------|
| `POST` | `/api/clients/register` | Registra cliente. Body `{ email, adminToken? }`. Devuelve `{ apiKey, email, isAdmin, serverUrl }` |
| `GET` | `/api/clients/validate?key=` | Valida key (lo usa el Server). Devuelve `{ email, isAdmin }` |
| `GET` | `/api/clients/` | Lista clientes (header `x-admin-token`) |
| `DELETE` | `/api/clients/{key}` | Revoca key (header `x-admin-token`) |

## Registro CLI

```bash
# Cliente regular
zero-teams register --email noe.francisco@optimaretail.es --url https://cp.tudominio.com

# Admin (wildcard *)
zero-teams register --admin-token $ADMIN_TOKEN --url https://cp.tudominio.com
```

Imprime la API key y el JSON listo para pegar en `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "teams": {
      "url": "https://mcp.tudominio.com/mcp",
      "headers": { "X-Api-Key": "zk_..." }
    }
  }
}
```

## Flujo en Claude

1. Cliente pega el JSON anterior en `claude_desktop_config.json`.
2. El agente llama `teams_list_chats` con `userId` = el email (admin) o sin `userId` (cliente regular, se fuerza su email).
3. Luego `teams_get_chat_messages` con el `chatId` devuelto.

## Despliegue en Forge + Cloudflare tunnel

1. Subir el repo al server Forge (o usar el site existente).
2. Crear `deploy/.env` desde `deploy/.env.example` con credenciales reales + `ENCRYPTION_KEY` + `ADMIN_TOKEN` + `MCP_SERVER_URL=https://mcp.tudominio.com/mcp`.
3. Levantar:

```bash
docker compose -f deploy/docker-compose.yml up -d --build
```

4. Crear dos rutas DNS en el tunnel de Cloudflare:

```bash
cloudflared tunnel route dns <tunnel-uuid> cp.tudominio.com   # -> ControlPlane :5100
cloudflared tunnel route dns <tunnel-uuid> mcp.tudominio.com  # -> MCP server :5200
```

5. Configurar el ingress del tunnel para apuntar:
   - `cp.tudominio.com` → `http://localhost:5100`
   - `mcp.tudominio.com` → `http://localhost:5200`

6. Probar:

```bash
curl -s https://cp.tudominio.com/health
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://mcp.tudominio.com/mcp \
  -H "Content-Type: application/json" -H "X-Api-Key: zk_..." \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## Panel web (Next.js)

El panel (`apps/panel`) se sirve bajo el prefijo `/panel` del mismo dominio cloud
(`https://<dominio>/panel`) usando `basePath`, para no necesitar un subdominio extra.
El `basePath` es opcional y se controla por env: **sin** `NEXT_PUBLIC_BASE_PATH` el panel
se sirve en la raíz (uso Docker/compose).

### Build

```bash
cd apps/panel
NEXT_PUBLIC_BASE_PATH=/panel \
NEXT_PUBLIC_CONTROLPLANE_URL=https://<dominio> \
NEXT_PUBLIC_MCP_URL=https://<dominio> \
pnpm build
```

Salida `output: "standalone"`. En monorepo el resultado queda en
`.next/standalone/apps/panel/` (contiene `server.js` + `node_modules` + `.next`).

### Despliegue (sin Docker)

1. `apps/panel/.next/standalone/apps/panel/` → `<base>/panel/`
2. `apps/panel/.next/static/` → `<base>/panel/.next/static/`
3. `apps/panel/public/` → `<base>/panel/public/`
4. Run script `<base>/panel.run.sh`:
   ```bash
   export NODE_ENV=production PORT=3000 HOSTNAME=127.0.0.1
   cd <base>/panel && exec node server.js
   ```
5. Daemon supervisor `mcp-panel` (Forge background process) apuntando al run script.
6. nginx: `location /panel { proxy_pass http://127.0.0.1:3000; }`. El prefijo `/panel`
   es a la vez el `basePath` de Next y la ruta nginx: **sin rewrite** (pasar la URI tal cual).

Rutas: `/panel` (dashboard), `/panel/login`, `/panel/tenants`, `/panel/consents`,
`/panel/health`, `/panel/metrics`, `/panel/playground`.

## Modelo de autorización (hardening)

Reglas aplicadas por `CloudAccessGuard`. Solo actúan en **modo cloud**; en
stdio/legacy (sin identidad) no se restringe nada, para no romper el uso local
de un solo usuario.

- **Escritura = solo admin.** Requieren admin en modo cloud:
  `teams_create_chat`, `teams_send_message`, `teams_create_channel`,
  `teams_send_channel_message`, `teams_create_calendar_event`,
  `teams_delete_calendar_event`, `teams_add_chat_member`, `teams_remove_chat_member`.
- **Lectura de chat limitada a miembros.** `teams_get_chat_messages` comprueba
  (vía `GET /chats/{id}/members`) que el cliente regular sea miembro del chat.
  El admin no se ve afectado. Requiere permiso app-only `ChatMember.Read.All`
  (o `Chat.Read.All`); si falta, el acceso se deniega (fail closed).
- **Scoping por identidad.** `teams_list_chats`, `teams_list_calendar_events` y
  `teams_get_calendar_event` fuerzan el email del cliente regular
  (`ClientContext.ResolveTarget`); el admin puede consultar cualquier usuario.
- **`admin_*`**: siguen autenticándose con `ADMIN_TOKEN` (parámetro del tool).

### Notas / pendiente

- Los tools de lectura que reciben un `chatId`/`teamId` suelto
  (`teams_get_chat_members`, `teams_list_channels`, `teams_get_channel_members`)
  no validan membresía: son GUID aleatorios no adivinables y solo se obtienen
  vía llamadas ya scoped. Endurecible si hiciera falta.
- El MCP Server sirve a un **único tenant** de Entra (el de la App Registration
  configurada); el scoping cloud es por **usuario** dentro de ese tenant.
