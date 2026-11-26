using System.ComponentModel.DataAnnotations;

namespace CrmLeadManagement.Models;

public class ProductInputModel : IValidatableObject
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(4000)]
    public string Description { get; set; } = "";

    [Required]
    [StringLength(4000)]
    public string Features { get; set; } = "";

    [Required]
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; set; }

    [Url]
    [StringLength(2048)]
    public string? Url { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Url) &&
            (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            yield return new ValidationResult("URL must be a valid HTTP or HTTPS URL.", new[] { nameof(Url) });
        }
    }
}
