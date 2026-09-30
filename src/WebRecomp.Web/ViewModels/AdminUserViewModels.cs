namespace WebRecomp.Web.ViewModels;

public sealed class AdminUsersIndexViewModel
{
    public string Search { get; init; } = string.Empty;
    public IReadOnlyList<AdminUserRowViewModel> Users { get; init; } = [];
}

public sealed class AdminUserRowViewModel
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public string RoleCode { get; init; } = string.Empty;
    public bool IsLocalAccount { get; init; }
    public bool IsCurrentUser { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? LastSignInAtUtc { get; init; }
}
