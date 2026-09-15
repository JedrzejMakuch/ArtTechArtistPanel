using System.Net;
using System.Net.Http.Json;
using ArtTechArtistPanel.Contracts;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace ArtTechArtistPanel.Api;

public sealed class ArtistProfileApiClient(HttpClient authenticated, HttpClient anonymous)
{
    public async Task<OwnArtistProfileDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await authenticated.GetAsync("api/artist/profile", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await ApiProblem.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<OwnArtistProfileDto>(cancellationToken)
            ?? throw new InvalidOperationException("Empty profile response.");
    }

    public async Task<OwnArtistProfileDto> SaveAsync(SaveArtistProfileRequest profile, bool create,
        CancellationToken cancellationToken = default)
    {
        using var response = create
            ? await authenticated.PostAsJsonAsync("api/artist/profile", profile, cancellationToken)
            : await authenticated.PutAsJsonAsync("api/artist/profile", profile, cancellationToken);
        await ApiProblem.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<OwnArtistProfileDto>(cancellationToken)
            ?? throw new InvalidOperationException("Empty profile response.");
    }

    public async Task<PublicArtistProfileDto> GetPublicAsync(string code, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/profiles/" + Uri.EscapeDataString(code));
        request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
        using var response = await anonymous.SendAsync(request, cancellationToken);
        await ApiProblem.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PublicArtistProfileDto>(cancellationToken)
            ?? throw new InvalidOperationException("Empty public profile response.");
    }
}
