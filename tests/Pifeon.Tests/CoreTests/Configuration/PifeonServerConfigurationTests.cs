using System;
using System.Collections.Generic;
using System.Text;
using AutoFixture.Xunit3;
using Pifeon.Core.Configuration;

namespace Pifeon.Tests.CoreTests.Configuration;

public class PifeonServerConfigurationTests
{
    [Theory]
    [InlineAutoData]
    public void PifeonServerConfiguration_ShouldHaveValidProperties(int sessionTimeoutSeconds)
    {
        /// Act
        PifeonServerConfiguration configuration = new PifeonServerConfiguration
        {
            SessionTimeoutSeconds = sessionTimeoutSeconds
        };
        /// Assert
        Assert.Equal(sessionTimeoutSeconds, configuration.SessionTimeoutSeconds);
    }
}
