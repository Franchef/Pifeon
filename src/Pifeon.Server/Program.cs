using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.HttpResults;
using Pifeon.Server;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

// =========================================================================
// 1. REGISTRAZIONE SERVICE DEFAULTS (Prima di builder.Build())
// Configura OpenTelemetry (Metrics, Traces, Logging), Health Checks e Resiliency
// =========================================================================
builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Registra la gestione sessioni in-memory
builder.Services.AddSingleton<PairingManager<WebSocket>>();

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

Todo[] sampleTodos =
[
    new(1, "Walk the dog"),
    new(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new(4, "Clean the bathroom"),
    new(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
];

RouteGroupBuilder todosApi = app.MapGroup("/todos");
todosApi.MapGet("/", () => sampleTodos)
        .WithName("GetTodos");

todosApi.MapGet("/{id}", Results<Ok<Todo>, NotFound> (int id) =>
    sampleTodos.FirstOrDefault(a => a.Id == id) is { } todo
        ? TypedResults.Ok(todo)
        : TypedResults.NotFound())
    .WithName("GetTodoById");

// Endpoint di Pairing unico via WebSockets
// Endpoint WebSocket per il signaling / pairing
app.Map("/ws/pairing", async context =>
{
    // Risoluzione esplicita del servizio dalla Dependency Injection
    PairingManager<WebSocket> manager = context.RequestServices.GetRequiredService<PairingManager<WebSocket>>();
    await WebSocketHandler.HandlePairingAsync(context, manager);
});

// Health check endpoint per Aspire / Docker
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

await app.RunAsync();
