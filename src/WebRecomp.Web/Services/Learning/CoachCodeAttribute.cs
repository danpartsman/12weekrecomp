using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WebRecomp.Web.Services.Learning;

/// <summary>Keep form validation in sync with the coaches actually offered.</summary>
public sealed class CoachCodeAttribute : ValidationAttribute, IClientModelValidator
{
    public CoachCodeAttribute() => ErrorMessage = "Choose one of the available coaches.";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => value is string code && CoachCatalog.IsValid(code)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-regex", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        context.Attributes.TryAdd("data-val-regex-pattern",
            "^(" + string.Join("|", CoachCatalog.All.Select(coach => Regex.Escape(coach.Code))) + ")$");
    }
}
