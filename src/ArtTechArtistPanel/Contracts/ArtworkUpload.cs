namespace ArtTechArtistPanel.Contracts;

public sealed record ArtworkUpload(string FileName, string ContentType, byte[] Content)
{
    public const long MaximumBytes = 10 * 1024 * 1024;
}
