namespace Kart.Shared.Messaging;

/// <summary>
/// Plain connection settings a service maps its own <c>RabbitMqOptions</c> onto — deliberately
/// not a bound options type itself, since each service's own defaults/validation for these values
/// (e.g. whether credentials are required, what port tests use) are that service's call, not
/// this shared package's.
/// </summary>
/// <param name="UserName">Left null to fall back to RabbitMQ.Client's own guest/guest default (loopback-only brokers).</param>
public sealed record RabbitMqConnectionSettings(string HostName, int Port = 5672, string? UserName = null, string? Password = null);
