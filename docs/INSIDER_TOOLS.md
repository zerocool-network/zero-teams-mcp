# Insider Risk — módulo `insider_*`

Investigación interna de fuga de información y salidas a la competencia.

Diseño: **sin caso abierto y aprobado por dos personas distintas, ningún tool lee datos**. Toda consulta —incluidas
las denegadas— queda en un log append-only encadenado por SHA-256.

> Este módulo **no vive en el MCP de Teams**. Es un servicio aparte (`src/ZeroTeamsMcp.Insider`, puerto 5300) con
> binario, credenciales de Azure y endpoint propios. Ver [ADR-0009](adr/ADR-0009-insider-mcp-separado.md).

---

## 0. Despliegue

```bash
# Local (solo escucha en loopback)
TRANSPORT=http \
INSIDER_KEYS="miguel=clave-larga-1,legal=clave-larga-2" \
INSIDER_APPROVER_TOKEN="..." \
INSIDER_ORG_DOMAINS="tudominio.com" \
INSIDER_AZURE_TENANT_ID=... INSIDER_AZURE_CLIENT_ID=... INSIDER_AZURE_CLIENT_SECRET=... \
dotnet run --project src/ZeroTeamsMcp.Insider
# -> http://127.0.0.1:5300/mcp   (header X-Insider-Key)
```

El servicio **no arranca** sin `INSIDER_KEYS` ni sin credenciales de Azure. Es a propósito.

Usa `INSIDER_AZURE_*` a propósito: si alguien lanza este servicio en una shell que ya tiene `AZURE_CLIENT_ID` del MCP
de Teams, no debe heredar esos permisos sin darse cuenta. `AZURE_*` solo se lee como fallback cuando la variable
`INSIDER_AZURE_*` no existe. Al arrancar, el log muestra qué `clientId` está usando.

### Claves per-persona

`INSIDER_KEYS="nombre=clave,nombre2=clave2"`. El nombre es el actor que queda en la auditoría, así que cada persona
tiene su clave y se revoca individualmente. Mínimo 16 caracteres, comparación en tiempo constante.

Las claves del ControlPlane **no sirven aquí**: son superficies distintas.

### Docker

`deploy/docker-compose.yml` define `mcp-insider` **sin `ports:`** y con `INSIDER_TOOLS` por defecto en `off`. Solo es alcanzable dentro de la red de compose.
Para activarlo hay que poner `INSIDER_TOOLS=on` de forma explícita en el entorno: el servicio arranca, pero los tools
no leen nada hasta que se habilite a propósito.

### Producción actual (optimafront-1)

Desplegado el 2026-11-11 como daemon de Forge `1222475` (`insider-mcp`), **solo loopback**:

| Elemento | Valor |
|---|---|
| Base | `/home/forge/mcp-insider.optimaonline.eu` |
| App | `app/ZeroTeamsMcp.Insider.dll` (publish `linux-x64`, framework-dependent) |
| Entorno | `insider.env` (600), dueño `forge` |
| Arranque | `insider.run.sh` (755) → `dotnet app/ZeroTeamsMcp.Insider.dll` |
| Bind | `http://127.0.0.1:5300` |
| Store | `data/insider.db` (SQLite WAL) |
| Daemon | `/etc/supervisor/conf.d/daemon-1222475.conf` (creado por Forge) |

Crear/actualizar el daemon **por Forge**, no copiando el `.conf` a mano (el usuario `forge` no tiene sudo para `cp`):

```
POST https://forge.laravel.com/api/orgs/<org>/servers/1216150/background-processes
{"name":"insider-mcp","command":"/home/forge/mcp-insider.optimaonline.eu/insider.run.sh",
 "user":"forge","directory":"/home/forge/mcp-insider.optimaonline.eu","processes":1}
```

Validación tras arrancar: `/health` → 200, `POST /mcp` sin `X-Insider-Key` → 401, `tools/list` con clave → 12 tools
`insider_*`, y `data/insider.db` creado. No hay puerto público: el acceso es túnel
`ssh -L 5300:127.0.0.1:5300 optimafront-1` y luego `http://127.0.0.1:5300/mcp`.

Credenciales: reutiliza la App Registration del `server.env` de Teams (`INSIDER_AZURE_*` copiados). **Mientras no exista
Application Access Policy, este proceso puede leer todos los buzones del tenant** — ver 1.1 antes de dar claves a nadie.

1. Aplica primero la Application Access Policy (sección 1.1).
2. Publica detrás de un reverse proxy con TLS.
3. Fija `INSIDER_ALLOWED_IPS` con los rangos del proxy.
4. Nunca lo dejes en el mismo hostname ni con las mismas claves que el MCP de Teams.

---

## 1. Antes de usar esto

### 1.1 Estado real de permisos

Este servidor corre con credenciales **app-only** y consentimiento de admin. El token actual incluye:

| Rol | Alcance |
|---|---|
| `Mail.Read` | **todos los buzones del tenant** |
| `Files.Read.All` | **todos los OneDrive/SharePoint** |
| `Chat.ReadWrite.All` | todos los chats, lectura y envío |
| `User.ReadBasic.All` | directorio |

No existe scoping por buzón en Graph. La única forma de acotarlo es una **Application Access Policy** de Exchange
Online:

```powershell
# 1. Grupo de seguridad con los buzones investigables
New-UnifiedGroup -DisplayName "GRP-Insider-Investigations" -Alias "grp-insider-investigations"

# 2. Restringir la app a ese grupo
New-ApplicationAccessPolicy -AppId <AZURE_CLIENT_ID> `
  -PolicyScopeGroupId GRP-Insider-Investigations@tudominio.com `
  -AccessRight RestrictAccess `
  -Description "zero-teams-mcp: solo buzones de investigacion"

# 3. Verificar
Test-ApplicationAccessPolicy -Identity victima@tudominio.com -AppId <AZURE_CLIENT_ID>
Test-ApplicationAccessPolicy -Identity cualquiera@tudominio.com -AppId <AZURE_CLIENT_ID>
```

**Sin este paso el módulo es una llave maestra sobre toda la plantilla.** La política tarda hasta 24 h en propagarse.

### 1.2 Antes de leer nada

1. **Legal hold** sobre buzón y OneDrive del sospechoso (Purview eDiscovery). Si lee primero y el empleado borra,
   la prueba desapareció y encima se le avisó.
2. **Señales de metadatos** (`insider_forwarding_rules`, `insider_external_sharing`). Es donde está el 90% de los casos.
3. **Contenido** (`insider_mail_search`, `insider_chat_search`) solo sobre custodios concretos y ventana corta.

### 1.3 Base legal (España)

Acceder a comunicaciones de un empleado requiere, como mínimo:

- Política de uso aceptable **previa** y comunicada (art. 90 LOPDGDD, art. 20.3 ET).
- Juicio de **necesidad y proporcionalidad**: medida concreta, fin legítimo, sin alternativa menos invasiva.
- Información a la representación legal de los trabajadores cuando el acceso derive de un procedimiento disciplinario.
- Participación del DPO.

Sin esto, la prueba puede ser inadmisible y la empresa sancionable. Este módulo deja el rastro que hace la medida
demostrable; no sustituye al asesoramiento jurídico.

---

## 2. Flujo

```
insider_permissions_check      → ¿qué puedo hacer con el token actual?
insider_case_open              → caso + custodios + justificación + aprobador (2 personas)
        ↓
insider_forwarding_rules       ┐
insider_external_sharing       ├─ señales: no invasivas, primero
insider_audit_search           ┘
        ↓
insider_mail_search            ┐
insider_chat_search            ├─ contenido: solo custodios del caso
        ↓                      ┘
insider_export_evidence        → JSON + SHA-256 (conservación de prueba)
        ↓
insider_audit_trail            → quién consultó qué
insider_audit_verify           → ¿alguien editó la auditoría?
insider_case_close             → cerrar (requiere aprobador)
```

### Ejemplo

```jsonc
// 1. Abrir caso. Quien solicita (identidad del cliente) != approvedBy.
{ "name": "Posible exfiltración — equipo comercial",
  "justification": "3 bajas al competidor en 6 semanas con descargas masivas de OneDrive",
  "custodians": "empleado1@empresa.com,empleado2@empresa.com",
  "approvedBy": "legal@empresa.com",
  "approverToken": "<INSIDER_APPROVER_TOKEN>",
  "legalBasis": "Política uso aceptable v3, art. 20.3 ET",
  "days": 30 }
// -> { "caseId": "CASE-20261026-435DE7", ... }

// 2. Señal barata: reenvío a externos
{ "caseId": "CASE-20261026-435DE7", "custodian": "empleado1@empresa.com" }

// 3. Contenido acotado: 14 días, sin cuerpos
{ "caseId": "CASE-20261026-435DE7", "custodian": "empleado1@empresa.com",
  "kql": "from:competidor.com", "from": "2026-10-01", "to": "2026-10-15" }
```

---

## 3. Controles implementados

| Control | Dónde | Efecto |
|---|---|---|
| Caso obligatorio | `InsiderGuard.RequireOpenCase` | Sin `caseId` válido, error. No hay "leer todo" |
| Doble aprobación | `ValidateApproverToken` + `EnsureTwoPerson` | `approvedBy` ≠ solicitante y token del servidor válido |
| Caducidad | `insider_case_open(days)` | Máx. 90 días; caducado → denegado |
| Custodios nominados | `EnsureCustodian` | Un caso no sirve de comodín para otros buzones |
| Tope de custodios | `MaxCustodians = 10` | Un caso no puede ser media plantilla |
| Ventana temporal | `MaxWindowDays = 90` | Evita barridos históricos indefinidos |
| Cuerpo opt-in | `includeBody=false` por defecto | Metadatos primero |
| Auditoría encadenada | `InsiderStore` | Append-only, SHA-256, detecta edición/borrado |
| Kill switch | `INSIDER_TOOLS=off` | Desactiva los 12 tools |
| Clave per-persona | `InsiderKeyring` | Sin `X-Insider-Key` válida no hay request; el nombre de la clave es el actor auditado |
| Aislamiento | Proyecto y binario separados | Los tools no pueden registrarse en el MCP público (ADR-0009) |

### Auditoría: por qué no `ILogger`

El log de aplicación rota, no es append-only y no permite demostrar que nadie lo tocó. `insider_audit` es una tabla
SQLite con `prev_hash`/`hash`: cambiar una fila rompe la cadena y `insider_audit_verify` lo detecta.

```
seq | ts | case_id | actor | tool | target | query | result_count | outcome | detail | prev_hash | hash
```

`outcome` ∈ `ok` | `denied` | `error`. Las negativas se registran a propósito: un intento denegado es lo que hay que
poder demostrar después.

---

## 4. Qué funciona hoy y qué no

| Capacidad | Estado | Permiso que falta |
|---|---|---|
| Reglas de reenvío a externos | ✅ | — |
| Búsqueda en buzón (metadatos + cuerpo opcional) | ✅ | — |
| Búsqueda en chats 1:1 y grupales | ✅ | — |
| Enlaces anónimos / permisos externos OneDrive (raíz) | ✅ | — |
| Exportación con hash | ✅ | — |
| Audit log de Purview (lo más probatorio) | ❌ | `AuditLog.Read.All` |
| Mensajes de canales de Teams | ❌ | `ChannelMessage.Read.All` |
| Búsqueda cross-mailbox / legal hold | ❌ | `eDiscovery.Read.All` |
| Purview Insider Risk Management | ❌ | `InsiderRiskManagement.Read.All` |
| Riesgo de inicio de sesión / viaje imposible | ❌ | `IdentityRiskEvent.Read.All` |
| Copia a USB / impresión | ❌ | No está en Graph: Defender for Endpoint |

`insider_permissions_check` devuelve esta matriz calculada desde los roles reales del token, no desde el README.

### Limitaciones conocidas

- **`insider_mail_search`**: no combina `$search` con `$filter` (limitación de Graph); el rango de fechas va dentro
  del KQL. Sin `kql` filtra solo por fecha.
- **`insider_external_sharing`**: revisa permisos de la **raíz** del OneDrive. Las subcarpetas compartidas se detectan
  mejor con `SharingSet` / `AnonymousLinkCreated` del audit log.
- **`insider_chat_search`**: recorre chats y filtra en cliente; con muchos chats se alcanza el tope. Los canales de
  equipo requieren otro permiso.
- **`insider_audit_search`**: endpoint `beta`; el esquema puede cambiar sin aviso.

---

## 5. Verificación

```bash
python3 scripts/insider-smoke-test.py
```

Valida 21 comprobaciones sin leer ningún buzón: registro de tools, doble aprobación, custodios, caducidad de ventana,
integridad de la cadena, bloqueo tras cierre y control de acceso HTTP (401 sin clave / clave incorrecta).

Manipulación de la auditoría:

```bash
sqlite3 data/insider.db "UPDATE insider_audit SET detail='x' WHERE seq=1;"
# insider_audit_verify -> { "chainValid": false, "brokenAtSeq": 1, "reason": "hash de la fila no coincide..." }
```
