using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
public record CreateSessionRequest(string Action = "CREATE");
