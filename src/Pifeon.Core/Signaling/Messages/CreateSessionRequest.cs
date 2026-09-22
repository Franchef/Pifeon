using System;
using System.Collections.Generic;
using System.Text;

namespace Pifeon.Core.Signaling.Messages;

public record CreateSessionRequest(string Action = "CREATE");
