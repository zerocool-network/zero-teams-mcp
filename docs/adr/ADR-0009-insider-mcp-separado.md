# ADR-0009: MCP de investigación como servicio separado

## Estado

**Aceptado** — 2026-10-26

Complementa [ADR-0008](ADR-0008-insider-risk-case-governance.md), que definió el gobierno de los casos pero dejó los
tools dentro del MCP de Teams.

## Contexto

Tras implementar el módulo `insider_*` en `ZeroTeamsMcp.Server`, el análisis de despliegue mostró cuatro problemas:

1. **Mismo binario, mismos permisos.** La App Registration usada por el MCP público (`ebea4bb8-…`) acumula
   `Mail.Read` y `Files.Read.All` junto a los scopes de Teams. Si se compromete la clave del MCP de Teams, el atacante
   hereda lectura de todos los buzones y todos los OneDrive, incluidas nóminas y RRHH.
2. **Endpoint público.** `mcp-server-default` publica `0.0.0.0:5200` con autenticación por `X-Api-Key` contra el
   ControlPlane. Un `Mail.Read` tenant-wide detrás de ese endpoint es una superficie desproporcionada.
3. **Descubrimiento.** `tools/list` devuelve los 12 tools a cualquier cliente autenticado, aunque luego los deniegue.
   Anuncia capacidad y filtra nombres de herramientas internas.
4. **`WithToolsFromAssembly()`.** Registra todo lo que haya en el ensamblado. Basta un refactor o un `partial` mal
   ubicado para re-exponer los tools de correo en el servidor público sin que nadie lo revise.

### Opciones consideradas

1. **Dejarlo en el MCP de Teams** con `RequireAdmin`. Funciona, pero los cuatro problemas siguen ahí.
2. **Mismo ensamblado, dos contenedores** con distinto `AZURE_CLIENT_ID` y filtro de tools por env. Separa permisos,
   pero no elimina el riesgo de re-exposición accidental ni el tamaño del binario.
3. **Proyecto, binario y despliegue separados.** Un ensamblado distinto **físicamente no puede** registrar sus tools
   en el servidor público.

## Decisión

Opción 3.

- Nuevo proyecto `src/ZeroTeamsMcp.Insider` (`Microsoft.NET.Sdk.Web`), dependencias mínimas: `ModelContextProtocol`,
  `Microsoft.Data.Sqlite`, `Microsoft.Extensions.Http.Resilience`. Sin `PdfPig`, sin `OpenXml`, sin Bot Framework.
- Los 12 tools, `InsiderStore`, `InsiderGuard` y la auditoría se mueven allí. `ZeroTeamsMcp.Server` vuelve a 19 tools
  y pierde la dependencia de SQLite.
- **Autenticación propia**: `InsiderKeyring` con `INSIDER_KEYS="nombre=clave,..."` — una clave por persona, comparación
  en tiempo constante, y el **nombre de la clave es el actor que aparece en la auditoría**. Así se revoca a una
  persona sin rotar al resto. Las claves del ControlPlane no valen aquí, a propósito.
- **Fail closed**: en modo HTTP el proceso no arranca sin claves ni sin credenciales de Azure.
- **Default seguro de bind**: `http://127.0.0.1:5300`. En compose no publica `ports:`; solo es alcanzable en la red
  interna. Exponerlo a internet es una decisión explícita, detrás de proxy y allowlist.
- `INSIDER_ALLOWED_IPS` opcional para restringir origen.
- Sin `ClientContext` ni `CloudAccessGuard`: todo el que llega a este servidor es equipo de seguridad. El control de
  acceso es la clave per-persona, no un flag `is_admin`.
- Transporte dual (`TRANSPORT=http|stdio`) como el resto del proyecto; **default `http`**, y en stdio los logs van a
  stderr para no romper el protocolo.

## Consecuencias

### Positivas

- Un incidente en el MCP público ya no otorga acceso a buzones ni a OneDrive.
- Se puede apagar el módulo de investigación sin afectar a Teams (`docker compose stop mcp-insider`).
- Cada lectura se atribuye a una persona concreta, no a "el admin".
- Superficie de dependencias y binario mucho menor.
- La App Registration del MCP de Teams puede perder `Mail.Read` y `Files.Read.All`, reduciendo su alcance real.

### Negativas

- Dos despliegues, dos App Registrations, dos juegos de credenciales que rotar.
- El `insider_*` deja de estar disponible como tool local en Claude Desktop sin apuntar al nuevo endpoint.
- Sigue haciendo falta **`New-ApplicationAccessPolicy`** para acotar `Mail.Read`: el aislamiento de proceso no acota
  el permiso de Azure. Sin esa política, el nuevo servicio sigue pudiendo leer todos los buzones (aunque ya solo con
  sus propias claves).
- Persistencia en volumen `insider-data` separada: los backups de auditoría son un ítem operativo nuevo.
