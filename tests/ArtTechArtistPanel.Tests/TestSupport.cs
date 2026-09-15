using System.Net;
using System.Net.Http.Json;
using ArtTechArtistPanel.Api;
using ArtTechArtistPanel.Auth;
using ArtTechArtistPanel.Contracts;

namespace ArtTechArtistPanel.Tests;

internal sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    public static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body) };
    public static HttpResponseMessage Tokens(string access = "access", string refresh = "refresh", int expires = 3600) =>
        Json(new TokenResponse("Bearer", access, expires, refresh));
    public static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send)) { BaseAddress = new Uri("https://api.example.test/") };
}

internal sealed class MemoryStore : ISessionTokenStore
{
    public string? Value;
    public bool IsAvailable { get; set; } = true;
    public Task<string?> ReadAsync() => Task.FromResult(Value);
    public Task WriteAsync(string? refreshToken) { Value = refreshToken; return Task.CompletedTask; }
}

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class SessionFixture : IDisposable
{
    public MemoryStore Store { get; } = new();
    public TestClock Clock { get; } = new();
    public HttpClient IdentityHttp { get; }
    public IdentityApiClient Identity { get; }
    public AuthSession Session { get; }
    public SessionFixture(Func<HttpRequestMessage, Task<HttpResponseMessage>>? send = null)
    {
        IdentityHttp = StubHandler.Client(send ?? (_ => Task.FromResult(StubHandler.Tokens())));
        Identity = new(IdentityHttp);
        Session = new(Identity, Store, Clock);
    }
    public Task Login() => Session.LoginAsync(new() { Email = "artist@example.test", Password = "Test-password-123!" });
    public HttpClient Authenticated(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new BearerTokenHandler(Session, IdentityHttp.BaseAddress!) { InnerHandler = new StubHandler(send) })
        { BaseAddress = IdentityHttp.BaseAddress };
    public void Dispose() => IdentityHttp.Dispose();
}
