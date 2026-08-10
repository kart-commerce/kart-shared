using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Kart.Shared.Configuration;

/// <summary>
/// Reads the single, shared GlobalConfig file every service points <c>GlobalConfig:Path</c> at
/// (per <see cref="GlobalConfigExtensions"/>) and layers in only the two sections that matter to
/// <see cref="ServiceName"/>: the top-level <c>Global</c> section (platform-wide defaults every
/// service inherits — RabbitMQ broker location, Redis/Mongo endpoints, the log directory root)
/// then <c>Services:&lt;ServiceName&gt;</c> on top of it (this service's own secrets — connection
/// strings, JWT keys, per-service RabbitMQ credentials). Overlaying at the individual leaf-key
/// level (not whole-section replace) means a service that only overrides e.g.
/// <c>RabbitMq:UserName</c> still inherits <c>RabbitMq:HostName</c>/<c>Port</c> from <c>Global</c>
/// — the same net result as today's per-service files layered against each service's own
/// appsettings.Development.json defaults, just sourced from one shared file.
/// </summary>
internal sealed class GlobalConfigConfigurationSource : FileConfigurationSource
{
    // Regular mutable property, not init-only: AddKartGlobalConfig sets it via
    // IConfigurationBuilder.Add<T>(configureSource)'s post-construction delegate, which runs as
    // a plain statement rather than an object initializer.
    public string ServiceName { get; set; } = "";

    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new GlobalConfigConfigurationProvider(this);
    }
}

internal sealed class GlobalConfigConfigurationProvider : FileConfigurationProvider
{
    public GlobalConfigConfigurationProvider(GlobalConfigConfigurationSource source) : base(source)
    {
    }

    public override void Load(Stream stream)
    {
        var source = (GlobalConfigConfigurationSource)Source;
        var root = JsonNode.Parse(stream);
        var global = root?["Global"];
        var service = root?["Services"]?[source.ServiceName];

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Flatten(global, prefix: "", data);
        // Overlaid last, so a leaf key this service sets wins over the same leaf key in Global.
        Flatten(service, prefix: "", data);

        // Every service's log directory is {Global:LogRoot}/{ServiceName} unless a service's own
        // Services:<name> block explicitly sets Observability:LogFile:Directory (escape hatch,
        // not expected to be used) — computed here instead of duplicated in every Services block,
        // which is what let 9 of the 10 services carrying this key drift onto a broken path.
        if (!data.ContainsKey("Observability:LogFile:Directory"))
        {
            var logRoot = global?["LogRoot"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(logRoot))
            {
                data["Observability:LogFile:Directory"] = $"{logRoot}/{source.ServiceName}";
            }
        }

        Data = data;
    }

    /// <summary>
    /// Walks a JSON subtree into colon-delimited keys, matching the shape ASP.NET Core's own
    /// JSON config parser produces (objects nest with <c>:</c>, arrays index with <c>:0</c>,
    /// <c>:1</c>, ...) so binding behavior for anything under this node (e.g.
    /// <c>ServicePrincipalSeeds</c>, an array of objects) is identical to today's plain
    /// <c>AddJsonFile</c>.
    /// </summary>
    private static void Flatten(JsonNode? node, string prefix, IDictionary<string, string?> data)
    {
        switch (node)
        {
            case null:
                return;
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    Flatten(value, prefix.Length == 0 ? key : $"{prefix}:{key}", data);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    Flatten(array[i], $"{prefix}:{i}", data);
                }
                break;
            case JsonValue value:
                data[prefix] = value.ToString();
                break;
        }
    }
}
