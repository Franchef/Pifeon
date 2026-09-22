IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Pifeon_Server>("PifeonServer");

await builder.Build().RunAsync();
