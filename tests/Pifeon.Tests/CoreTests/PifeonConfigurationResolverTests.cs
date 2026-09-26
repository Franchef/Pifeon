using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Configuration;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonConfigurationResolverTests : IDisposable
{
    public PifeonConfigurationResolverTests()
    {
        // Pulisce la variabile d'ambiente prima di ogni test
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, null);
    }

    public void Dispose()
    {
        // Assicura la pulizia della variabile d'ambiente al termine di ogni test
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, null);
    }

    [Theory]
    [InlineData("wss://cli.example.com", "wss://cli.example.com")]
    [InlineData("  wss://cli.example.com/ws  ", "wss://cli.example.com/ws")] // Verifica il .Trim()
    public void ResolveSignalingUrl_ShouldReturnCliOrGuiParam_WhenProvided(string inputParam, string expected)
    {
        // Arrange
        string appSettings = "wss://appsettings.example.com";
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, "wss://env.example.com");

        // Act
        string resolved = PifeonConfigurationResolver.ResolveSignalingUrl(inputParam, appSettings);

        // Assert
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("wss://env.example.com", "wss://env.example.com")]
    [InlineData("  wss://env.example.com/ws  ", "wss://env.example.com/ws")] // Verifica il .Trim()
    public void ResolveSignalingUrl_ShouldReturnEnvVariable_WhenCliParamIsMissingOrWhitespace(string envValue, string expected)
    {
        // Arrange
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, envValue);
        string appSettings = "wss://appsettings.example.com";

        // Act & Assert (testa con null, stringa vuota e soli spazi per la CLI)
        Assert.Equal(expected, PifeonConfigurationResolver.ResolveSignalingUrl(null, appSettings));
        Assert.Equal(expected, PifeonConfigurationResolver.ResolveSignalingUrl("", appSettings));
        Assert.Equal(expected, PifeonConfigurationResolver.ResolveSignalingUrl("   ", appSettings));
    }

    [Theory]
    [InlineData("wss://appsettings.example.com", "wss://appsettings.example.com")]
    [InlineData("  wss://appsettings.example.com/ws  ", "wss://appsettings.example.com/ws")] // Verifica il .Trim()
    public void ResolveSignalingUrl_ShouldReturnAppSettings_WhenCliAndEnvAreMissingOrWhitespace(string appSettingsValue, string expected)
    {
        // Arrange
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, "   "); // Env con spazi

        // Act & Assert (testa con null e whitespace per CLI e Env)
        Assert.Equal(expected, PifeonConfigurationResolver.ResolveSignalingUrl(null, appSettingsValue));
        Assert.Equal(expected, PifeonConfigurationResolver.ResolveSignalingUrl("  ", appSettingsValue));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void ResolveSignalingUrl_ShouldReturnDefaultFallback_WhenAllInputsAreMissingOrWhitespace(string? cliParam, string? appSettings)
    {
        // Arrange
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, "  ");

        // Act
        string resolved = PifeonConfigurationResolver.ResolveSignalingUrl(cliParam, appSettings);

        // Assert
        Assert.Equal(PifeonConfigurationResolver.DefaultSignalingUrl, resolved);
    }
}
