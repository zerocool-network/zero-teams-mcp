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

## Límites / hardening pendiente

- `teams_get_chat_messages` acepta cualquier `chatId` conocido. Los `chatId` son aleatorios y solo se obtienen vía `teams_list_chats` (ya scoped), así que el riesgo práctico es bajo. Si hace falta, se puede verificar pertenencia al chat por membresía Graph.
- Los tools de escritura (`teams_send_message`, etc.) están disponibles para cualquier cliente autenticado. Si quieres restringirlos a admin, se agrega un guard en esos tools.
- La key admin se usa también para los `admin_*` tools (vía `ADMIN_TOKEN`).
