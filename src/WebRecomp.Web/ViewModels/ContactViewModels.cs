using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class ContactFormViewModel
{
    [Required]
    [StringLength(120)]
    [Display(Name = "Your name")]
    public string Name { get; set; } = "";

    [Required]
    [EmailAddress]
    [StringLength(320)]
    [Display(Name = "Your email")]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(200)]
    public string Subject { get; set; } = "";

    [Required]
    [StringLength(5000)]
    public string Message { get; set; } = "";
}
