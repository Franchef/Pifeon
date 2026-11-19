using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Testing;

namespace Pifeon.AspireTests;

public sealed class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? _application;

    public Uri ServerUri => _application!.GetEndpoint("PifeonServer", "http");

    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Pifeon_AppHost>(cts.Token);
        DotnetProjectResource server = builder.Resources.OfType<DotnetProjectResource>()
            .Single(resource => resource.Name == "PifeonServer");
        builder.CreateResourceBuilder(server).WithHttpHealthCheck("/health");

        _application = await builder.BuildAsync(cts.Token);
        try
        {
            await _application.StartAsync(cts.Token);
            await _application.ResourceNotifications.WaitForResourceHealthyAsync("PifeonServer", cts.Token);
        }
        catch
        {
            await _application.DisposeAsync();
            _application = null;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }
}
