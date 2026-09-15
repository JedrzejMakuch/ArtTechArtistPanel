using System.Net.Http.Json;
using ArtTechArtistPanel.Contracts;

namespace ArtTechArtistPanel.Api;

public sealed class IdentityApiClient(HttpClient http)
{
    public async Task RegisterAsync(Credentials credentials, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("register", credentials, cancellationToken);
        await ApiProblem.EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<TokenResponse> LoginAsync(Credentials credentials, CancellationToken cancellationToken = default) =>
        SendAsync("login?useCookies=false", credentials, cancellationToken);

    public Task<TokenResponse> RefreshAsync(string refreshToken) =>
        SendAsync("refresh", new { refreshToken });

    private async Task<TokenResponse> SendAsync<T>(string route, T body, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(route, body, cancellationToken);
        await ApiProblem.EnsureSuccessAsync(response, cancellationToken);
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (tokens is null || tokens.TokenType != "Bearer" || string.IsNullOrWhiteSpace(tokens.AccessToken)
            || string.IsNullOrWhiteSpace(tokens.RefreshToken) || tokens.ExpiresIn <= 0)
            throw new HttpRequestException("Invalid token response.");
        return tokens;
    }
}
