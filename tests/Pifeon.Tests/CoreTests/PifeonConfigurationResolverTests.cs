using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Configuration;

namespace Pifeon.Tests.CoreTests;

public class PifeonConfigurationResolverTests
{
    [Fact]
    public void ResolveSignalingUrl_ShouldPrioritizeCliParamOverAll()
    {
        // Arrange
        string cliUrl = "wss://cli.custom.com";
        string appSettingsUrl = "wss://appsettings.custom.com";
        Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, "wss://env.custom.com");

        try
        {
            // Act
            string resolved = PifeonConfigurationResolver.ResolveSignalingUrl(cliUrl, appSettingsUrl);

            // Assert
            Assert.Equal(cliUrl, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PifeonConfigurationResolver.EnvSignalingUrl, null);
        }
    }

    [Fact]
    public void ResolveSignalingUrl_ShouldFallbackToDefaultWhenAllNull()
    {
        // Act
        string resolved = PifeonConfigurationResolver.ResolveSignalingUrl();

        // Assert
        Assert.Equal(PifeonConfigurationResolver.DefaultSignalingUrl, resolved);
    }
}
