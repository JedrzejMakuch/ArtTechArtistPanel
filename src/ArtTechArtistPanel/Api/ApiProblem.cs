using System.Net;
using System.Text.Json;

namespace ArtTechArtistPanel.Api;

public sealed class ApiException(HttpStatusCode status, ApiProblem problem) : Exception(problem.Title)
{
    public HttpStatusCode Status { get; } = status;
    public ApiProblem Problem { get; } = problem;
}

public sealed class ApiProblem
{
    public string Title { get; set; } = "The request could not be completed.";
    public string? Detail { get; set; }
    public Dictionary<string, string[]> Errors { get; set; } = [];

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode) return;
        ApiProblem? problem = null;
        try
        {
            problem = JsonSerializer.Deserialize<ApiProblem>(await response.Content.ReadAsStringAsync(cancellationToken),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException) { }
        problem ??= new ApiProblem { Title = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please log in again.",
            HttpStatusCode.Forbidden => "Access denied. This account or profile cannot perform this action.",
            HttpStatusCode.NotFound => "The requested resource was not found.",
            _ => "The request could not be completed."
        } };
        if ((int)response.StatusCode >= 500)
            problem = new ApiProblem { Title = "The service is unavailable. Please try again." };
        throw new ApiException(response.StatusCode, problem);
    }

    public static ApiProblem FromException(Exception exception) => exception switch
    {
        ApiException api => api.Problem,
        HttpRequestException => new() { Title = "Cannot reach the service. Check your connection and try again." },
        OperationCanceledException => new() { Title = "The request was interrupted or timed out. Please try again." },
        _ => new() { Title = "The service returned an unexpected response. Please try again." }
    };
}
