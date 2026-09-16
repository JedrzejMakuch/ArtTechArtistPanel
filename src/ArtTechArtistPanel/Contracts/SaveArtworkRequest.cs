using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ArtTechArtistPanel.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveArtworkRequest : IValidatableObject
{
    private string title = string.Empty;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get => title; set => title = value?.Trim() ?? string.Empty; }
    [StringLength(2000)]
    public string? Description { get; set; }
    [Display(Name = "Creation year"), Range(1, 9999)]
    public int CreationYear { get; set; }
    [Display(Name = "Width (cm)"), Range(typeof(decimal), "1", "1000")]
    public decimal WidthCm { get; set; }
    [Display(Name = "Height (cm)"), Range(typeof(decimal), "1", "1000")]
    public decimal HeightCm { get; set; }
    [Display(Name = "Display order"), Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CreationYear > DateTime.UtcNow.Year)
            yield return new("Creation year cannot be in the future.", [nameof(CreationYear)]);
        if (decimal.Round(WidthCm, 2) != WidthCm)
            yield return new("Width must have at most two decimal places in centimeters.", [nameof(WidthCm)]);
        if (decimal.Round(HeightCm, 2) != HeightCm)
            yield return new("Height must have at most two decimal places in centimeters.", [nameof(HeightCm)]);
    }
}
