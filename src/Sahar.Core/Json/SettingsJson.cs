using System.Text.Json.Serialization;
using Sahar.Core.Settings;

namespace Sahar.Core.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UseStringEnumConverter = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(SaharSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
