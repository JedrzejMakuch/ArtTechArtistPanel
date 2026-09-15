namespace ArtTechArtistPanel.Configuration;

public sealed class ApiOptions
{
    public Uri? BaseUri { get; }
    public string? Error { get; }

    public ApiOptions(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            Error = "Configure Api:BaseUrl with the backend HTTP(S) address before using the panel.";
            return;
        }
        BaseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
