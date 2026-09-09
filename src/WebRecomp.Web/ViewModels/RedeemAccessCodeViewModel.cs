using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class RedeemAccessCodeViewModel
{
    [Required]
    [StringLength(64)]
    [Display(Name = "Access code")]
    public string Code { get; set; } = "";
}