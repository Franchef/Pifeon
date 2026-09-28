using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.HttpResults;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;
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

// Abilita i WebSockets
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

// Endpoint di Pairing unico via WebSockets
// Endpoint WebSocket per il signaling / pairing
app.Map("/ws/pairing", async (HttpContext context, CancellationToken cancellationToken) => 
{
    // Risoluzione esplicita del servizio dalla Dependency Injection
    IPairingManager<WebSocket> manager = context.RequestServices.GetRequiredService<IPairingManager<WebSocket>>();
    await WebSocketHandler.HandlePairingAsync(context, manager, cancellationToken);
});

// Health check endpoint per Aspire / Docker
app
    .MapGet("/health", () => Results.Ok(new HealthyStatus("Healthy")))
    .Produces<HealthyStatus>();

await app.RunAsync();
