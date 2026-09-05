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
    }
}

public sealed class ConsentAction
{
    public ConsentStatus? Status { get; init; }
    public string? Error { get; init; }
}
