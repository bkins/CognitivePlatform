using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Admin.Services.Testing;

public static class TestOperationEndpoints
{
    public static void MapTestOperations(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/operations");
        group.AddEndpointFilter(async (context, next) =>
        {
            var remote = context.HttpContext.Connection.RemoteIpAddress;
            if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
            if (remote is null || !IPAddress.IsLoopback(remote)) return Results.StatusCode(403);
            var options = context.HttpContext.RequestServices.GetRequiredService<TestOperationsOptions>();
            var supplied = context.HttpContext.Request.Headers["X-Admin-Secret"].ToString();
            if (string.IsNullOrEmpty(options.Secret) || string.IsNullOrEmpty(supplied)
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(options.Secret)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)))) return Results.Unauthorized();
            try { return await next(context); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
        });
        group.MapGet("/test-profiles", (TestRunCoordinator coordinator) => Results.Json(coordinator.Profiles));
        group.MapPost("/test-runs", (TestRunRequest request, TestRunCoordinator coordinator) =>
        {
            var run = coordinator.Start(request);
            return Results.Accepted($"/api/operations/test-runs/{run.Id}", new { run.Id, run.Profile, Status = run.Status.ToString() });
        });
        group.MapGet("/test-runs/{id:guid}", (Guid id, TestRunCoordinator coordinator) =>
            coordinator.Get(id) is { } run ? Results.Json(run, TestRunStore.JsonOptions) : Results.NotFound());
        group.MapGet("/test-runs/{id:guid}/results", (Guid id, TestRunCoordinator coordinator) =>
            coordinator.Get(id) is { } run ? Results.Json(run, TestRunStore.JsonOptions) : Results.NotFound());
        group.MapGet("/test-runs/{id:guid}/summary", (Guid id, TestRunCoordinator coordinator) => Results.Text(coordinator.Summary(id), "text/markdown"));
        group.MapGet("/test-runs/{id:guid}/logs", (Guid id, int? after, int? limit, TestRunCoordinator coordinator) =>
            Results.Json(coordinator.Logs(id, after ?? 0, limit ?? 200)));
        group.MapPost("/test-runs/{id:guid}/cancel", (Guid id, TestRunCoordinator coordinator) =>
            coordinator.Cancel(id) ? Results.Accepted($"/api/operations/test-runs/{id}") : Results.NotFound());
    }
}
