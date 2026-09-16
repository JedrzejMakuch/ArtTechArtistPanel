using System.Net.Http.Json;
using System.Globalization;
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
    public async Task<OwnArtworkDto> SaveAsync(Guid parent, Guid? id, SaveArtworkRequest metadata, ArtworkUpload? image,
        CancellationToken token = default)
    {
        using var content = Multipart(metadata, image);
        using var response = id is null
            ? await authenticated.PostAsync(Route(parent), content, token)
            : await authenticated.PutAsync($"{Route(parent)}/{id}", content, token);
        return await Read<OwnArtworkDto>(response, token);
    }

    private static MultipartFormDataContent Multipart(SaveArtworkRequest metadata, ArtworkUpload? image)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(metadata.Title), "title");
        content.Add(new StringContent(metadata.Description ?? string.Empty), "description");
        content.Add(new StringContent(metadata.CreationYear.ToString(CultureInfo.InvariantCulture)), "creationYear");
        content.Add(new StringContent(metadata.WidthCm.ToString(CultureInfo.InvariantCulture)), "widthCm");
        content.Add(new StringContent(metadata.HeightCm.ToString(CultureInfo.InvariantCulture)), "heightCm");
        content.Add(new StringContent(metadata.SortOrder.ToString(CultureInfo.InvariantCulture)), "sortOrder");
        if (image is not null)
        {
            var file = new ByteArrayContent(image.Content);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(image.ContentType);
            content.Add(file, "image", image.FileName);
        }
        return content;
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
