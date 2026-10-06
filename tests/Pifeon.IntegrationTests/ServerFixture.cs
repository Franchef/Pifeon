using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Services;
using System.Net.WebSockets;

namespace Pifeon.IntegrationTests;

// Fixture condivisa per evitare di riavviare il server ad ogni singolo test
public class ServerFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment variable to disable/increase rate limiting for tests
        builder.UseEnvironment("Test");
    }
}

public sealed class DirectTransferServerFixture : ServerFixture
{
    public DirectTransferServerFixture()
    {
        UseKestrel(0);
    }
}

// Fixture for testing rate limiter behavior - uses production rate limits
public class ProductionRateLimitServerFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use default environment with production rate limits (5 per minute)
        builder.UseEnvironment("Production");
    }
}

public class ShortSessionTimeoutServerFixture : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPairingManager<WebSocket>>();
            services.AddSingleton<IPairingManager<WebSocket>>(_ =>
                new PairingManager<WebSocket>(TimeSpan.FromMilliseconds(350)));
        });
    }
}
