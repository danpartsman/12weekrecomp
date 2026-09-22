using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class TestPersonaLoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
