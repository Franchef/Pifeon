IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddDotnetProject("PifeonServer", "../Pifeon.Server/Pifeon.Server.csproj");

await builder.Build().RunAsync();
