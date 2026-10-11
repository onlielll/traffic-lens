using System.Text.Json.Serialization;

namespace ProxyTrafficFloat;

// Compile settings metadata once instead of discovering properties through reflection.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal partial class SettingsJson : JsonSerializerContext
{
}
