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
// 1. REGISTRAZIONE SERVICE DEFAULTS (Prima di builder.Build())
// Configura OpenTelemetry (Metrics, Traces, Logging), Health Checks e Resiliency
// =========================================================================
builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, Pifeon.Core.Signaling.Messages.SignalingJsonContext.Default);
});

// Registra la gestione sessioni in-memory
builder.Services.AddSingleton<IPairingManager<WebSocket>, PairingManager<WebSocket> >();

const string IpRateLimitPolicyName = "IpRateLimit";

// Rate Limiter nativo di ASP.NET Core per prevenire bruteforce e DDoS
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Politica di Rate Limiting basata su IP Pubblico del Router + Client-Id del singolo dispositivo
    options.AddPolicy(policyName: IpRateLimitPolicyName, httpContext =>
    {
        string clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Se il client non invia l'header custom X-Pifeon-Client-Id, usiamo "anonymous"
        string clientId = httpContext.Request.Headers["X-Pifeon-Client-Id"].ToString();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = "anonymous";
        }

        // Chiave composita: protegge l'IP ma distingue i client dietro lo stesso NAT
        string partitionKey = $"{clientIp}:{clientId}";

        // Usa limiti più alti per i test, limiti normali per produzione
        bool isTestEnvironment = builder.Environment.IsEnvironment("Test");
        int permitLimit = isTestEnvironment ? 1000 : 5;
        TimeSpan window = TimeSpan.FromMinutes(1);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,        // 5 per produzione, 1000 per test
                Window = window,                  // 1 minuto
                QueueLimit = 0                    // Nessuna coda: rigetta subito con HTTP 429
            });
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// =========================================================================
// 2. MAPPATURA ENDPOINT SERVICE DEFAULTS (Dopo builder.Build())
// Espone gli endpoint di Health Check (/health, /alive) per Aspire e Docker
// =========================================================================
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();

// Abilita i WebSockets
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});


// -------------------------------------------------------------------------
// Rotta A: Creazione Sessione per il Sender
// -------------------------------------------------------------------------
app.MapGet("/ws/session/create", async (HttpContext context, [FromServices]IPairingManager<WebSocket> manager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "È richiesta una connessione WebSocket." });
    }

    using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await WebSocketHandler.HandleSenderAsync(webSocket, manager, context.RequestAborted);

    return Results.Empty;
}).RequireRateLimiting(IpRateLimitPolicyName);

// -------------------------------------------------------------------------
// Rotta B: Join del Receiver tramite Codice a 6 cifre nell'URL
// -------------------------------------------------------------------------
app.MapGet("/ws/session/join/{code}", async (string code, HttpContext context, [FromServices]IPairingManager<WebSocket> manager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        return Results.BadRequest(new { error = "È richiesta una connessione WebSocket." });
    }

    // Risposta HTTP 404 immediata prima ancora di allocare il WebSocket se il codice non esiste
    if (!manager.TryGetSession(code, out PairingSession<WebSocket>? session) || session == null)
    {
        return Results.NotFound(new { error = "Codice sessione non trovato o scaduto." });
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
