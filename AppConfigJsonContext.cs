using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AltRunSharp
{
    /// <summary>
    /// Source-generated JSON serializer context for PublishSingleFile / Trim compatibility.
    /// </summary>
    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(LaunchItem))]
    [JsonSerializable(typeof(ScriptItem))]
    [JsonSerializable(typeof(ScheduledTask))]
    [JsonSerializable(typeof(List<LaunchItem>))]
    [JsonSerializable(typeof(List<ScriptItem>))]
    [JsonSerializable(typeof(List<ScheduledTask>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
    internal partial class AppConfigJsonContext : JsonSerializerContext
    {
    }
}
