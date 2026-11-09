# ADR-0008: Módulo Insider Risk con gobierno de caso

## Estado

**Aceptado** — 2026-10-26

## Contexto

La organización sospecha que empleados están saliendo a la competencia llevándose información. Se pide al MCP poder
leer correos y mensajes de Teams para investigarlo.

Al revisar el estado real (no el README), el token app-only **ya incluía** `Mail.Read` y `Files.Read.All` con
consentimiento de admin. Es decir: la capacidad de leer todos los buzones y todos los OneDrive ya existía, sin
ningún control. El problema no era conceder un permiso nuevo, sino que el permiso existente era una llave maestra
sin trazabilidad.

Restricciones:

- Acceder a comunicaciones de empleados exige base legal, necesidad y proporcionalidad (LOPDGDD, art. 20.3 ET).
  Sin trazabilidad, la prueba es débil y la empresa es sancionable.
- `Mail.Read` app-only **no se puede acotar por buzón desde Graph**; solo con `New-ApplicationAccessPolicy` en
  Exchange Online.
- El `ILogger` existente rota y no es append-only: no sirve para demostrar quién consultó qué.
- Modo cloud: los clientes regulares son solo lectura y están limitados a su propio email (`ClientContext`).

### Opciones consideradas

1. **Tool directo `read_mailbox(userId)`** — simple, sin gobierno. Rechazada: convierte el MCP en un buscador de
   correos ajenos sin rastro; cualquier filtrador con una API key lee toda la plantilla.
2. **Solo metadatos y audit log** — mínimo riesgo legal, pero no cubre los casos que exigen contenido.
3. **Gobierno de caso + señales primero + contenido acotado** — el acceso solo existe dentro de un caso aprobado,
   con custodios nominados, ventana limitada y auditoría encadenada.

## Decisión

Implementamos la opción 3.

- **`insider_case_open`** exige `justification` (≥ 20 caracteres), `approvedBy` distinto del solicitante y
  `approverToken` que coincide con `INSIDER_APPROVER_TOKEN`. Sin aprobador configurado, falla cerrado.
- El caso guarda custodios (máx. 10), scopes (`mail`, `chat`, `files`, `signals`) y caducidad (máx. 90 días).
- Todo tool de lectura exige `caseId` abierto, vigente y con el scope pedido; el custodio debe estar nominado.
- **`InsiderStore`** (SQLite) persiste casos y un log append-only con `prev_hash`/`hash` SHA-256. Ninguna entrada se
  actualiza ni se borra. `insider_audit_verify` recorre la cadena y señala la primera fila alterada.
- **`InsiderRunner`** envuelve todos los tools: kill switch (`INSIDER_TOOLS`) → permiso admin → trabajo → auditoría
  (`ok`/`denied`/`error`). Un tool no puede saltarse el registro.
- **`insider_permissions_check`** lee los roles del propio token y devuelve la matriz real de capacidades, para no
  prometer investigaciones que el permiso no soporta.
- Los endpoints no cubiertos por el SDK de Graph (audit log `beta`, drive permissions) se llaman por REST con el
  token app-only, en vez de forzar formas del SDK.

### Orden de uso impuesto por el diseño

Señales de metadatos antes que contenido. El cuerpo del mensaje es opt-in (`includeBody=false`). No es una limitación
técnica: es que el 90% de los casos se resuelven con reglas de reenvío, sharing externo y audit log, sin leer
conversaciones personales.

## Consecuencias

### Positivas

- La capacidad peligrosa que ya existía pasa a estar gobernada: casos, custodios, caducidad y auditoría.
- Cada lectura es atribuible a una persona, un caso y una justificación.
- Las negativas también se registran: permite demostrar que se intentó y se bloqueó.
- El kill switch permite apagar el módulo sin desplegar.
- `insider_permissions_check` elimina la deriva entre documentación y permisos reales (el README estaba desactualizado).

### Negativas

- **`Mail.Read` y `Files.Read.All` siguen siendo tenant-wide.** El módulo no lo arregla: lo hace auditable. La
  mitigación real es `New-ApplicationAccessPolicy` acotando la app a un grupo de buzones. Documentado, no automatizado
  (requiere Exchange PowerShell y propagación de hasta 24 h).
- Un caso abierto con aprobador complaciente sigue siendo un riesgo. La doble aprobación es un control de proceso,
  no técnico.
- `insider_audit_verify` es O(n) sobre la tabla: sirve bajo demanda, no en cada request.
- Sin `AuditLog.Read.All`, la fuente más probatoria (audit log de Purview) no está disponible. Es el siguiente permiso
  a solicitar si el módulo se usa de verdad.
- SQLite sigue limitando a una réplica para escrituras, igual que el resto del proyecto ([ADR-0005](ADR-0005-controlplane-rest.md)).
