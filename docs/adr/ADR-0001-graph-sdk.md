# ADR-0001: Uso de Microsoft.Graph SDK 6.5.0

## Estado

**Aprobado** — 2026-09-05

## Contexto

zero-teams-mcp necesita interactuar con Microsoft Graph API para acceder a Teams, canales, mensajes, chats y miembros. Existen dos opciones principales:

1. **Microsoft.Graph SDK 6.5.0**: Cliente tipado con modelos, autenticación integrada y manejo de paginación
2. **REST crudo** (`HttpClient` + modelos propios): Llamadas HTTP directas con deserialización manual

### Factores relevantes

- El proyecto ya depende de `Azure.Identity` para autenticación client credentials
- Graph API tiene 200+ endpoints; el SDK cubre la mayoría con tipos fuertes
- El SDK maneja paginación automática (`PageIterator`)
- El SDK incluye manejo de errores tipado (`ServiceException`)
- REST crudo ofrece control total pero requiere código boilerplate significativo
- La versión 6.x del SDK es la GA actual con soporte .NET 10

---

## Decisión

Usamos **Microsoft.Graph SDK 6.5.0** como cliente para todas las llamadas a Microsoft Graph API.

### Justificación

1. **Tipado fuerte**: Los modelos (`Team`, `Channel`, `ChatMessage`, `ConversationMember`) eliminan errores de serialización
2. **Paginación**: `PageIterator` maneja automáticamente la paginación de resultados de Graph
3. **Autenticación**: Integración nativa con `TokenCredential` de Azure.Identity
4. **Errores**: `ServiceException` con códigos de error tipados
5. **Mantenimiento**: Microsoft mantiene el SDK actualizado con cambios de API
6. **Consistencia**: Un solo patrón de código para todas las llamadas Graph

### Alternativas consideradas

| Alternativa | Pros | Contras | Decisión |
|------------|------|---------|----------|
| REST crudo | Control total, sin dependencias | Mucho boilerplate, error-prone | Rechazada |
| Graph SDK 5.x | Estable, bien documentado | Versión anterior, sin .NET 10 | Rechazada |
| Graph SDK beta | Acceso a todos los endpoints | Inestable, breaking changes | Usado solo para F5 (meetings) |

### Uso del SDK

```csharp
// Creación del cliente
var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
var graphClient = new GraphServiceClient(credential);

// Listar equipos
var teams = await graphClient.Me.JoinedTeams
    .GetAsync(config => {
        config.QueryParameters.Top = 20;
        config.QueryParameters.Select = new[] { "id", "displayName", "description" };
    });

// Enviar mensaje
var message = new ChatMessage {
    Body = new ItemBody {
        Content = "<p>Hola desde MCP</p>",
        ContentType = BodyType.Html
    }
};
await graphClient.Teams[teamId].Channels[channelId].Messages
    .PostAsync(message);
```

---

## Consecuencias

### Positivas

- Menos código boilerplate (~60% menos que REST crudo)
- Tipado compile-time catcha errores de nombre de campo
- Paginación automática sin código adicional
- Actualizaciones del SDK traen compatibilidad con nuevas APIs de Graph
- Integración directa con `ClientSecretCredential`

### Negativas

- Dependencia de paquete externo con su propio ciclo de releases
- Algunos endpoints beta no están en el SDK (requieren REST fallback)
- Tamaño del paquete (~5MB en producción)
- Breaking changes posibles entre versiones mayores (6.x → 7.x)

### Mitigaciones

- Para endpoints beta (F5): cliente HTTP separado con modelos propios
- Actualizaciones controladas via Dependabot + tests de integración
- SDK es referencia oficial de Microsoft — bajo riesgo de abandono
