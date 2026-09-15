using System.Net.Http.Json;
using ArtTechArtistPanel.Contracts;

namespace ArtTechArtistPanel.Api;

public sealed class ExhibitionApiClient(HttpClient authenticated)
{
    private const string Route = "api/artist/exhibitions";

    public async Task<OwnExhibitionDto[]> ListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await authenticated.GetAsync(Route, cancellationToken);
        return await ReadAsync<OwnExhibitionDto[]>(response, cancellationToken);
    }

    public async Task<OwnExhibitionDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await authenticated.GetAsync($"{Route}/{id}", cancellationToken);
        return await ReadAsync<OwnExhibitionDto>(response, cancellationToken);
    }

    public async Task<OwnExhibitionDto> SaveAsync(SaveExhibitionRequest metadata, Guid? id, CancellationToken cancellationToken = default)
    {
        using var response = id is null
            ? await authenticated.PostAsJsonAsync(Route, metadata, cancellationToken)
            : await authenticated.PutAsJsonAsync($"{Route}/{id}", metadata, cancellationToken);
        return await ReadAsync<OwnExhibitionDto>(response, cancellationToken);
    }

    public async Task<OwnExhibitionDto> TransitionAsync(Guid id, string action, CancellationToken cancellationToken = default)
    {
        if (action is not ("publish" or "deactivate")) throw new ArgumentException("Unknown exhibition action.", nameof(action));
        using var response = await authenticated.PostAsJsonAsync($"{Route}/{id}/{action}", new { }, cancellationToken);
        return await ReadAsync<OwnExhibitionDto>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await ApiProblem.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException("Empty exhibition response.");
    }
}
