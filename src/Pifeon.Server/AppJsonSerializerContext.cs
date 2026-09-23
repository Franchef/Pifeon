using System.Text.Json.Serialization;

namespace Pifeon.Server;

[JsonSerializable(typeof(Todo[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
