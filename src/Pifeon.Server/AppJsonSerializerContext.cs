using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Pifeon.Server;

[ExcludeFromCodeCoverage]
[JsonSerializable(typeof(Todo[]))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{

}
