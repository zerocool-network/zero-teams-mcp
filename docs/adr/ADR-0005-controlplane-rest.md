# ADR-0005: ControlPlane REST API

## Estado

**Aprobado** — 2026-09-05

## Contexto

Para gestionar múltiples tenants MCP, se necesita una capa de orquestación que permita:

- Registrar y eliminar tenants
- Iniciar el flujo de consentimiento admin
- Monitorear el estado de cada host MCP
- Ver métricas de uso agregadas
- Realizar health checks

### Opciones consideradas

1. **REST API** (.NET Minimal API): API HTTP ligera, integrada en el ecosistema .NET
2. **gRPC**: Comunicación eficiente, pero más complejo de consumir desde Next.js
3. **GraphQL**: Flexible, pero over-engineering para este caso
4. **CLI únicamente**: Sin API web, gestión solo vía comandos

---

## Decisión

Implementamos un **ControlPlane REST API** usando .NET Minimal API, con un panel Next.js como cliente principal.

### Justificación

1. **Consistencia tecnológica**: Todo el backend es .NET, incluido el ControlPlane
2. **Minimal API**: Ligero, sin controllers, sin繁sinnecesario
3. **Consumo fácil**: Next.js consume la API directamente vía fetch
4. **Documentación**: Swagger/OpenAPI generado automáticamente
5. **Testing**: Integration tests con WebApplicationFactory

### Arquitectura

```
┌─────────────────────────────┐
│   Next.js Panel (UI)        │
│   shadcn/ui Neón M365       │
└──────────┬──────────────────┘
           │ HTTP/JSON
           ▼
┌─────────────────────────────┐
│   ControlPlane REST API     │
│   .NET Minimal API          │
│   ─ CRUD Tenants            │
│   ─ Consent Flow            │
│   ─ Stats & Health          │
└──────────┬──────────────────┘
           │
           ├──→ SQLite (tenants, estado)
           ├──→ Host MCP processes (status)
           └──→ Azure AD (consent redirect)
```

### Endpoints

| Método | Ruta | Descripción | Request Body | Response |
|--------|------|-------------|--------------|----------|
| `GET` | `/api/tenants` | Listar tenants | — | `Tenant[]` |
| `POST` | `/api/tenants` | Registrar tenant | `CreateTenantDto` | `Tenant` |
| `GET` | `/api/tenants/{id}` | Detalle tenant | — | `Tenant` |
| `DELETE` | `/api/tenants/{id}` | Eliminar tenant | — | `204` |
| `POST` | `/api/tenants/{id}/consent` | Generar URL consent | — | `ConsentUrl` |
| `GET` | `/api/tenants/{id}/status` | Estado del host | — | `HostStatus` |
| `GET` | `/api/stats` | Métricas agregadas | — | `Stats` |
| `GET` | `/api/health` | Health check | — | `HealthResponse` |

### Modelos

```csharp
public record Tenant(
    Guid Id,
    string TenantDomain,
    string TenantId,
    string Status,        // "pending" | "consented" | "active" | "error"
    DateTime CreatedAt,
    DateTime? LastActivityAt,
    int TotalCalls,
    int ErrorCount
);

public record CreateTenantDto(
    string TenantDomain,
    string TenantId
);

public record ConsentUrl(
    string Url,
    string State,
    DateTime ExpiresAt
);

public record HostStatus(
    Guid TenantId,
    bool IsRunning,
    DateTime? LastPing,
    string Transport,     // "stdio" | "http"
    int ActiveConnections
);

public record Stats(
    int TotalTenants,
    int ActiveTenants,
    int TotalCallsToday,
    int TotalErrorsToday,
    double AvgResponseTimeMs
);
```

### Implementación

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlite("Data Source=controlplane.db");
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapGet("/api/tenants", async (ITenantRepository repo) =>
    await repo.GetAllAsync());

app.MapPost("/api/tenants", async (CreateTenantDto dto, ITenantRepository repo) =>
{
    var tenant = new Tenant(Guid.NewGuid(), dto.TenantDomain, dto.TenantId, ...);
    await repo.CreateAsync(tenant);
    return Results.Created($"/api/tenants/{tenant.Id}", tenant);
});

app.MapPost("/api/tenants/{id}/consent", async (Guid id, ITenantRepository repo) =>
{
    var tenant = await repo.GetByIdAsync(id);
    var consentUrl = GenerateConsentUrl(tenant);
    return Results.Ok(new ConsentUrl(consentUrl, Guid.NewGuid().ToString(), DateTime.UtcNow.AddHours(1)));
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
```

### Autenticación del ControlPlane

```csharp
// NextAuth.js con Microsoft Entra ID
// Panel se autentica contra Entra ID
// API valida JWT tokens del panel

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/{tenant-id}/v2.0";
        options.Audience = "{client-id}";
    });
```

---

## Consecuencias

### Positivas

- API REST simple y estandarizada
- Consumo directo desde Next.js sin wrappers
- Swagger generado automáticamente para testing
- Integración nativa con el ecosistema .NET
- SQLite para persistencia ligera (sin infraestructura externa)
- Testing con WebApplicationFactory

### Negativas

- SQLite no escala horizontalmente (suficiente para MVP)
- Requiere hosting separado del MCP server (o same host)
- CORS configurado para el panel
- Sin autenticación por defecto (requiere implementación)

### Mitigaciones

- SQLite → PostgreSQL si se necesita escalar
- CORS configurado solo para el dominio del panel
- Autenticación JWT implementada en F4
- Rate limiting en endpoints públicos
