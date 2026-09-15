using System.Net.Http.Json;
using ArtTechArtistPanel.Contracts;

namespace ArtTechArtistPanel.Api;

public sealed class ArtworkApiClient(HttpClient authenticated)
{
    private static string Route(Guid parent) => $"api/artist/exhibitions/{parent}/artworks";
    public async Task<OwnArtworkDto[]> ListAsync(Guid parent, CancellationToken token = default)
    {
        using var response = await authenticated.GetAsync(Route(parent), token);
        return await Read<OwnArtworkDto[]>(response, token);
    }
    public async Task<OwnArtworkDto> GetAsync(Guid parent, Guid id, CancellationToken token = default)
    {
        using var response = await authenticated.GetAsync($"{Route(parent)}/{id}", token);
        return await Read<OwnArtworkDto>(response, token);
    }
    public async Task<OwnArtworkDto> SaveAsync(Guid parent, Guid? id, SaveArtworkRequest metadata, CancellationToken token = default)
    {
        using var response = id is null
            ? await authenticated.PostAsJsonAsync(Route(parent), metadata, token)
            : await authenticated.PutAsJsonAsync($"{Route(parent)}/{id}", metadata, token);
        return await Read<OwnArtworkDto>(response, token);
    }
    public async Task DeleteAsync(Guid parent, Guid id, CancellationToken token = default)
    {
        using var response = await authenticated.DeleteAsync($"{Route(parent)}/{id}", token);
        await ApiProblem.EnsureSuccessAsync(response, token);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken token)
    {
        await ApiProblem.EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<T>(token) ?? throw new InvalidOperationException("Empty artwork response.");
    }
}
