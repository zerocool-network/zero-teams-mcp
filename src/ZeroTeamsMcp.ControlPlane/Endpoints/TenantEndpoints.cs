using Microsoft.AspNetCore.Mvc;
using ZeroTeamsMcp.ControlPlane.Services;
using ZeroTeamsMcp.Core.Models;

namespace ZeroTeamsMcp.ControlPlane.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tenants");

        group.MapGet("/", (TenantStore store) =>
        {
            var tenants = store.GetAll().Select(t => new
            {
                t.TenantId,
                t.ClientId,
                t.DisplayName,
                t.CreatedAt,
                Consent = store.GetConsent(t.TenantId)?.Status.ToString()
            });
            return Results.Ok(tenants);
        });

        group.MapGet("/{tenantId}", (string tenantId, TenantStore store) =>
        {
            var tenant = store.GetById(tenantId);
            if (tenant is null) return Results.NotFound();
            var consent = store.GetConsent(tenantId);
            return Results.Ok(new
            {
                tenant.TenantId,
                tenant.DisplayName,
                tenant.ClientId,
                tenant.CreatedAt,
                Consent = consent is null ? null : new
                {
                    consent.Status,
                    consent.RequestedAt,
                    consent.GrantedAt,
                    consent.RevokedAt,
                    consent.ErrorMessage
                }
            });
        });

        group.MapPost("/", ([FromBody] TenantConfig config, TenantStore store) =>
        {
            var added = store.Add(config);
            if (!added) return Results.Conflict(new { error = "Tenant already exists" });
            var consentUrl = $"https://login.microsoftonline.com/{config.TenantId}/adminconsent?client_id={config.ClientId}";
            return Results.Created($"/api/tenants/{config.TenantId}", new
            {
                config.TenantId,
                config.DisplayName,
                ConsentUrl = consentUrl
            });
        });

        group.MapDelete("/{tenantId}", (string tenantId, TenantStore store) =>
        {
            var removed = store.Remove(tenantId);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/{tenantId}/consent", async (string tenantId, HttpContext http, TenantStore store) =>
        {
            var tenant = store.GetById(tenantId);
            if (tenant is null) { http.Response.StatusCode = 404; return; }
            var action = await http.Request.ReadFromJsonAsync<ConsentAction>();
            var status = action?.Status ?? ConsentStatus.Pending;
            var updated = store.UpdateConsent(tenantId, status, action?.Error);
            var consentUrl = $"https://login.microsoftonline.com/{tenantId}/adminconsent?client_id={tenant.ClientId}";
            http.Response.StatusCode = updated ? 200 : 404;
            await http.Response.WriteAsJsonAsync(new { tenantId, Status = status.ToString(), ConsentUrl = consentUrl });
        });

        group.MapGet("/{tenantId}/consent", (string tenantId, TenantStore store) =>
        {
            var tenant = store.GetById(tenantId);
            if (tenant is null) return Results.NotFound();
            var consent = store.GetConsent(tenantId);
            var consentUrl = $"https://login.microsoftonline.com/{tenantId}/adminconsent?client_id={tenant.ClientId}";
            return consent is not null
                ? Results.Ok(new { consent.Status, consent.RequestedAt, consent.GrantedAt, consent.RevokedAt, consent.ErrorMessage, ConsentUrl = consentUrl })
                : Results.Ok(new { Status = "Unknown", ConsentUrl = consentUrl });
        });
    }

    public static void MapHealthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", (TenantStore store) =>
        {
            var tenants = store.GetAll();
            var granted = tenants.Count(t => store.GetConsent(t.TenantId)?.Status == ConsentStatus.Granted);
            return Results.Ok(new
            {
                Status = "healthy",
                Timestamp = DateTime.UtcNow,
                Tenants = new
                {
                    Total = tenants.Count,
                    Consented = granted,
                    Pending = tenants.Count - granted
                }
            });
        });

        routes.MapGet("/health/detailed", (TenantStore store) =>
        {
            var tenants = store.GetAll();
            var details = tenants.Select(t =>
            {
                var consent = store.GetConsent(t.TenantId);
                return new
                {
                    t.TenantId,
                    t.DisplayName,
                    ConsentStatus = consent?.Status.ToString() ?? "Unknown",
                    consent?.GrantedAt,
                    consent?.RevokedAt,
                    consent?.ErrorMessage
                };
            });
            return Results.Ok(new
            {
                Status = "healthy",
                Timestamp = DateTime.UtcNow,
                Tenants = details
            });
        });
    }

    public static void MapMetricsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/metrics", () =>
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# HELP process_cpu_seconds_total Total CPU time in seconds");
            sb.AppendLine("# TYPE process_cpu_seconds_total counter");
            sb.AppendLine($"process_cpu_seconds_total {process.TotalProcessorTime.TotalSeconds:F3}");
            
            sb.AppendLine("# HELP process_memory_bytes Current memory usage in bytes");
            sb.AppendLine("# TYPE process_memory_bytes gauge");
            sb.AppendLine($"process_memory_bytes {process.WorkingSet64}");
            
            sb.AppendLine("# HELP process_virtual_memory_bytes Virtual memory size in bytes");
            sb.AppendLine("# TYPE process_virtual_memory_bytes gauge");
            sb.AppendLine($"process_virtual_memory_bytes {process.VirtualMemorySize64}");
            
            sb.AppendLine("# HELP dotnet_gc_heap_size_bytes GC heap size in bytes");
            sb.AppendLine("# TYPE dotnet_gc_heap_size_bytes gauge");
            sb.AppendLine($"dotnet_gc_heap_size_bytes {gc.HeapSizeBytes}");
            
            sb.AppendLine("# HELP dotnet_gc_gen0_collections_total Gen0 GC count");
            sb.AppendLine("# TYPE dotnet_gc_gen0_collections_total counter");
            sb.AppendLine($"dotnet_gc_gen0_collections_total {GC.CollectionCount(0)}");
            
            sb.AppendLine("# HELP dotnet_gc_gen1_collections_total Gen1 GC count");
            sb.AppendLine("# TYPE dotnet_gc_gen1_collections_total counter");
            sb.AppendLine($"dotnet_gc_gen1_collections_total {GC.CollectionCount(1)}");
            
            sb.AppendLine("# HELP dotnet_gc_gen2_collections_total Gen2 GC count");
            sb.AppendLine("# TYPE dotnet_gc_gen2_collections_total counter");
            sb.AppendLine($"dotnet_gc_gen2_collections_total {GC.CollectionCount(2)}");
            
            sb.AppendLine("# HELP dotnet_thread_count_total Current thread count");
            sb.AppendLine("# TYPE dotnet_thread_count_total gauge");
            sb.AppendLine($"dotnet_thread_count_total {process.Threads.Count}");
            
            return Results.Text(sb.ToString(), "text/plain; version=0.0.4");
        });
    }
}

public sealed class ConsentAction
{
    public ConsentStatus? Status { get; init; }
    public string? Error { get; init; }
}
