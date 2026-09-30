using System.ComponentModel.DataAnnotations;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.ViewModels;

public sealed class RecompTipsIndexViewModel
{
    public string Search { get; init; } = string.Empty;
    public IReadOnlyList<RecompTipPost> Posts { get; init; } = Array.Empty<RecompTipPost>();
}

public sealed class RecompTipEditViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(600)]
    public string Summary { get; set; } = string.Empty;

    [StringLength(16000)]
    public string Body { get; set; } = string.Empty;

    [Url]
    [StringLength(1200)]
    public string? SourceUrl { get; set; }

    [Url]
    [StringLength(1200)]
    public string? ImageUrl { get; set; }

    [StringLength(400)]
    public string? Tags { get; set; }

    public bool IsPublished { get; set; }
}
