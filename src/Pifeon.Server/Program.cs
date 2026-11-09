using System.Net.WebSockets;
using System.Threading.RateLimiting;
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

    // Rate limiting is based only on the trusted remote endpoint. Client-supplied
    // headers must never create independent brute-force budgets.
    options.AddPolicy(policyName: IpRateLimitPolicyName, httpContext =>
    {
        string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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
app.MapGet("/ws/session/create", HandleCreateSessionAsync)
    .RequireRateLimiting(IpRateLimitPolicyName);

// -------------------------------------------------------------------------
// Route B: Receiver join using the temporary pairing code in the URL
// -------------------------------------------------------------------------
app.MapGet("/ws/session/join/{code}", HandleJoinSessionAsync)
    .RequireRateLimiting(IpRateLimitPolicyName);

// Health check endpoint per Aspire / Docker
app.MapGet("/health", HandleHealthAsync);

await app.RunAsync();

static async Task HandleCreateSessionAsync(HttpContext context)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        await Results.BadRequest(new { error = "A WebSocket connection is required." }).ExecuteAsync(context);
        return;
    }

    IPairingManager<WebSocket> manager = context.RequestServices.GetRequiredService<IPairingManager<WebSocket>>();
    using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await WebSocketHandler.HandleSenderAsync(webSocket, manager, context.RequestAborted);
}

static async Task HandleJoinSessionAsync(HttpContext context)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        await Results.BadRequest(new { error = "A WebSocket connection is required." }).ExecuteAsync(context);
        return;
    }

    string code = context.Request.RouteValues["code"]?.ToString() ?? string.Empty;
    IPairingManager<WebSocket> manager = context.RequestServices.GetRequiredService<IPairingManager<WebSocket>>();
    if (!manager.TryClaimSession(code, out PairingSession<WebSocket>? session) || session is null)
    {
        await Results.NotFound(new { error = "Session code not found or expired." }).ExecuteAsync(context);
        return;
    }

    using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await WebSocketHandler.HandleReceiverAsync(code, webSocket, session, manager, context.RequestAborted);
}

static Task HandleHealthAsync(HttpContext context) =>
    Results.Ok(new HealthyStatus("Healthy")).ExecuteAsync(context);
