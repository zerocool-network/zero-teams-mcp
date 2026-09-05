# ADR-0002: Transporte Dual (stdio + HTTP)

## Estado

**Aprobado** — 2026-09-05

## Contexto

El servidor MCP debe soportar múltiples escenarios de despliegue:

- **Desarrollo local**: Claude Desktop u otras herramientas MCP se comunican vía stdio
- **Producción remota**: APIs y bots necesitan acceso HTTP/SSE
- **Múltiples clientes**: Diferentes herramientas deben conectarse simultáneamente

### Factores relevantes

- MCP SDK 2.2.0 soporta nativamente stdio y HTTP/SSE transports
- Claude Desktop solo acepta stdio
- Copilot Studio y bots requieren HTTP
- Un solo transport no cubre todos los casos de uso
- Mantener dos procesos separados duplica la lógica de negocio

---

## Decisión

Implementamos **transporte dual** en un solo host: stdio para herramientas locales y HTTP/SSE para acceso remoto.

### Justificación

1. **Un solo host, dos caras**: La lógica de negocio (tools, auth, Graph client) se comparte
2. **Configuración por variable de entorno**: `TRANSPORT=stdio|http` determina el modo
3. **Flexibilidad de despliegue**: El mismo binario sirve para ambos casos
4. **MVP pragmático**: No requerir infraestructura compleja para uso local

### Configuración

```bash
# Modo stdio (Claude Desktop)
TRANSPORT=stdio \
CLIENT_ID=xxx \
CLIENT_SECRET=yyy \
TENANT_ID=zzz \
zero-teams-mcp

# Modo HTTP (API remota)
TRANSPORT=http \
HTTP_PORT=5000 \
CLIENT_ID=xxx \
CLIENT_SECRET=yyy \
TENANT_ID=zzz \
zero-teams-mcp
```

### Implementación

```csharp
var builder = WebApplication.CreateBuilder(args);

// Registrar servicios comunes
builder.Services.AddMcpCore();

var transport = builder.Configuration["TRANSPORT"] ?? "stdio";

if (transport == "stdio")
{
    builder.Services.AddMcpStdioTransport();
}
else
{
    builder.Services.AddMcpHttpTransport();
    builder.WebHost.UseUrls($"http://*:{builder.Configuration["HTTP_PORT"] ?? "5000"}");
}

var app = builder.Build();
app.MapMcpEndpoints();
app.Run();
```

### Endpoints HTTP

| Ruta | Método | Descripción |
|------|--------|-------------|
| `/mcp/messages` | POST | Enviar mensaje MCP, recibir respuesta |
| `/mcp/sse` | GET | SSE endpoint para notificaciones |
| `/health` | GET | Health check |

---

## Consecuencias

### Positivas

- Un solo binario para ambos modos
- Misma lógica de negocio compartida
- Fácil testing: stdio para tests unitarios, HTTP para integración
- Escalabilidad: modo HTTP permite múltiples clientes simultáneos

### Negativas

- Complejidad de hosting: dos modos requieren configuración diferente
- stdio es bloqueante: un solo cliente a la vez
- HTTP requiere manejo de concurrencia (ya resuelto por Kestrel)
- Documentación duplicada para ambos modos

### Mitigaciones

- Configuración simple vía env vars
- stdio documentado como "desarrollo local"
- HTTP documentado como "producción"
- Tests cubren ambos modos
