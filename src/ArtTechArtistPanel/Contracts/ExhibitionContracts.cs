using System.ComponentModel.DataAnnotations;

namespace ArtTechArtistPanel.Contracts;

public sealed record OwnExhibitionDto(Guid Id, string ExhibitionCode, string Title,
    string Description, int SortOrder, string Status, DateTime CreatedAt);

public sealed class SaveExhibitionRequest
{
    private string title = "";
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get => title; set => title = value?.Trim() ?? ""; }
    [StringLength(2000)]
    public string? Description { get; set; }
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }
}
