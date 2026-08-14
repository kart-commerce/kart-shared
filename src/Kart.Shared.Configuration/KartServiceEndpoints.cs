namespace Kart.Shared.Configuration;

/// <summary>
/// Canonical local-dev port/URL registry for every kart-*-service — the single source of truth
/// every C# default that needs to reach another service's local dev address should reference
/// instead of a literal (kart-cart-service's <c>GrpcOptions</c>, kart-wishlist-service's
/// <c>ProductServiceOptions</c>, etc.). Mirrors kart-devops/ports.env and its docker-compose.yml
/// (the non-.NET equivalent source of truth for the same values, since JSON/YAML config files
/// have no way to reference a compiled constant) — change a port in exactly one place: here for
/// anything compiled, ports.env for anything else, and update the other to match.
/// </summary>
public static class KartServiceEndpoints
{
    public const int IdentityPort = 8081;
    public const int UserPort = 8082;
    public const int ProductPort = 8083;
    public const int CategoryPort = 8084;
    public const int SearchPort = 8085;
    public const int InventoryPort = 8086;
    public const int CartPort = 8087;
    public const int OrderPort = 8088;
    public const int PaymentPort = 8089;
    public const int OfferPort = 8090;
    public const int WishlistPort = 8091;
    public const int NotificationPort = 8092;
    public const int DeliveryTrackingPort = 8093;
    public const int AdminPort = 8094;
    public const int GatewayPort = 8100;

    public const string IdentityLocalBaseUrl = "http://localhost:8081";
    public const string IdentityLocalJwksUri = "http://localhost:8081/.well-known/jwks.json";
    public const string UserLocalBaseUrl = "http://localhost:8082";
    public const string ProductLocalBaseUrl = "http://localhost:8083";
    public const string CategoryLocalBaseUrl = "http://localhost:8084";
    public const string SearchLocalBaseUrl = "http://localhost:8085";
    public const string InventoryLocalBaseUrl = "http://localhost:8086";
    public const string CartLocalBaseUrl = "http://localhost:8087";
    public const string OrderLocalBaseUrl = "http://localhost:8088";
    public const string PaymentLocalBaseUrl = "http://localhost:8089";
    public const string OfferLocalBaseUrl = "http://localhost:8090";
    public const string WishlistLocalBaseUrl = "http://localhost:8091";
    public const string NotificationLocalBaseUrl = "http://localhost:8092";
    public const string DeliveryTrackingLocalBaseUrl = "http://localhost:8093";
    public const string AdminLocalBaseUrl = "http://localhost:8094";
    public const string GatewayLocalBaseUrl = "http://localhost:8100";
}
