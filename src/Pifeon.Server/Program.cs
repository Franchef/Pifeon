using System.Net.WebSockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;
using Pifeon.Core.Signaling.Models;
using Pifeon.Server;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

// =========================================================================
// 1. SERVICE DEFAULTS REGISTRATION (Before builder.Build())
// Configures OpenTelemetry (Metrics, Traces, Logging), Health Checks and Resiliency
// =========================================================================
builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, Pifeon.Core.Signaling.Messages.SignalingJsonContext.Default);
});

// Register in-memory session management
builder.Services.AddSingleton<IPairingManager<WebSocket>, PairingManager<WebSocket> >();

const string IpRateLimitPolicyName = "IpRateLimit";

// Native ASP.NET Core rate limiter to prevent brute force and DDoS
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Rate limiting policy based on router public IP + individual device Client-Id
    options.AddPolicy(policyName: IpRateLimitPolicyName, httpContext =>
    {
        string clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // If the client does not send the custom X-Pifeon-Client-Id header, use "anonymous"
        string clientId = httpContext.Request.Headers["X-Pifeon-Client-Id"].ToString();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = "anonymous";
        }

        // Composite key: protects IP but distinguishes clients behind the same NAT
        string partitionKey = $"{clientIp}:{clientId}";

        // Uses higher limits for testing, normal limits for production
        bool isTestEnvironment = builder.Environment.IsEnvironment("Test");
        int permitLimit = isTestEnvironment ? 1000 : 5;
        TimeSpan window = TimeSpan.FromMinutes(1);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,        // 5 for production, 1000 for testing
                Window = window,                  // 1 minute
                QueueLimit = 0                    // No queue: reject immediately with HTTP 429
            });
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// =========================================================================
// 2. SERVICE DEFAULTS ENDPOINT MAPPING (After builder.Build())
// Exposes Health Check endpoints (/health, /alive) for Aspire and Docker
// =========================================================================
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

// Enable WebSockets
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});


// -------------------------------------------------------------------------
// Route A: Create session for the Sender
// -------------------------------------------------------------------------
app.MapGet("/ws/session/create", async (HttpContext context, [FromServices]IPairingManager<WebSocket> manager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "A WebSocket connection is required." });
    }

    using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await WebSocketHandler.HandleSenderAsync(webSocket, manager, context.RequestAborted);

    return Results.Empty;
}).RequireRateLimiting(IpRateLimitPolicyName);

// -------------------------------------------------------------------------
// Route B: Receiver join using 6-digit code in the URL
// -------------------------------------------------------------------------
app.MapGet("/ws/session/join/{code}", async (string code, HttpContext context, [FromServices]IPairingManager<WebSocket> manager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "A WebSocket connection is required." });
    }

    // Immediate HTTP 404 response before allocating the WebSocket if code does not exist
    if (!manager.TryGetSession(code, out PairingSession<WebSocket>? session) || session == null)
    {
        return Results.NotFound(new { error = "Session code not found or expired." });
    }

    using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await WebSocketHandler.HandleReceiverAsync(code, webSocket, session, manager, context.RequestAborted);

    return Results.Empty;
}).RequireRateLimiting(IpRateLimitPolicyName);

// Health check endpoint per Aspire / Docker
app
    .MapGet("/health", () => Results.Ok(new HealthyStatus("Healthy")))
    .Produces<HealthyStatus>();

await app.RunAsync();
