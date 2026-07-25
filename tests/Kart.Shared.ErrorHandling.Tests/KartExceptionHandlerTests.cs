using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Kart.Shared.ErrorHandling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kart.Shared.ErrorHandling.Tests;

/// <summary>
/// WebApplicationFactory-style test: a real minimal-API host wired with
/// <c>AddKartErrorHandling</c>/<c>UseKartErrorHandling</c>, exercised end-to-end over
/// <c>TestServer</c>'s in-memory HTTP client — not a direct unit call into
/// <see cref="KartExceptionHandler"/>, so this also proves the DI registration and
/// middleware wiring actually work together, not just the handler logic in isolation.
/// </summary>
public class KartExceptionHandlerTests : IAsyncLifetime
{
    private sealed class CustomConflictException : Exception
    {
        public CustomConflictException(string message) : base(message)
        {
        }
    }

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddRouting();
        builder.Services.AddKartErrorHandling(options =>
            options.Map<CustomConflictException>(StatusCodes.Status409Conflict, "custom_conflict"));

        _app = builder.Build();
        _app.UseKartErrorHandling();

        _app.MapGet("/validation", () =>
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("Name", "Name is required"),
            });
        });

        _app.MapGet("/mapped", () => { throw new CustomConflictException("already exists"); });

        _app.MapGet("/boom", () => { throw new InvalidOperationException("unexpected failure"); });

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task ValidationException_Returns400_WithValidationErrorCode_AndPropertyDetails()
    {
        var response = await _client.GetAsync("/validation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await ParseAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("validation_error");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("details").GetProperty("Name")[0].GetString().Should().Be("Name is required");
    }

    [Fact]
    public async Task MappedException_ReturnsTheRegisteredStatusCodeAndErrorCode()
    {
        var response = await _client.GetAsync("/mapped");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await ParseAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("custom_conflict");
        body.GetProperty("detail").GetString().Should().Be("already exists");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UnmappedException_Returns500_WithGenericInternalErrorEnvelope_NeverLeakingTheMessage()
    {
        var response = await _client.GetAsync("/boom");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var body = await ParseAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be("internal_error");
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        body.GetProperty("detail").GetString().Should().NotContain("unexpected failure");
    }

    private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
