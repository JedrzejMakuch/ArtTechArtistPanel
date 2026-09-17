namespace ArtTechArtistPanel.Sharing;

public enum PublicContentKind { Profile, Exhibition }

public sealed class ShareLinkService
{
    public const string AndroidScheme = "arttechgallery";
    private readonly Uri? publicBaseUri;
    public bool IsConfigured => publicBaseUri is not null;

    public ShareLinkService(string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl)) return;
        if (!Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("ShareLinks:PublicBaseUrl must be an absolute HTTP(S) URL without credentials, query or fragment.");
        publicBaseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public string CreatePublicLink(PublicContentKind kind, string code) =>
        new Uri(publicBaseUri ?? throw new InvalidOperationException("Public share-link origin is not configured."),
            $"view/{Segment(kind)}/{Uri.EscapeDataString(ValidateCode(code))}").AbsoluteUri;

    public static string CreateAndroidLink(PublicContentKind kind, string code) =>
        $"{AndroidScheme}://{Segment(kind)}/{Uri.EscapeDataString(ValidateCode(code))}";

    private static string Segment(PublicContentKind kind) => kind switch
    { PublicContentKind.Profile => "profile", PublicContentKind.Exhibition => "exhibition", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };

    private static string ValidateCode(string code)
    {
        var value = code?.Trim() ?? "";
        if (value.Length is < 1 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid public code.", nameof(code));
        return value;
    }
}
