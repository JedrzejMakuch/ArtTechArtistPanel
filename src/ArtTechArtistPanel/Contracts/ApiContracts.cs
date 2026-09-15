using System.ComponentModel.DataAnnotations;

namespace ArtTechArtistPanel.Contracts;

public sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);

public sealed class Credentials
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
    [Required]
    public string Password { get; set; } = "";
}

public sealed class SaveArtistProfileRequest
{
    private string displayName = "";
    [Required, StringLength(200, MinimumLength = 1)]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? ""; }
    [StringLength(2000)]
    public string? Bio { get; set; }
}

public sealed record OwnArtistProfileDto(Guid Id, string ProfileCode, string DisplayName,
    string Bio, string ProfileImageUrl, bool IsActive, DateTime CreatedAt);

// Only the public fields this slice displays; other response fields are intentionally ignored.
public sealed record PublicArtistProfileDto(string ProfileCode, string DisplayName, string Bio);
