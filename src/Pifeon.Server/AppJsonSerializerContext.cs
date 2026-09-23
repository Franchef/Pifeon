using System.Text.Json.Serialization;

namespace Pifeon.Server;

[JsonSerializable(typeof(Todo[]))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{

}
