using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
[Route("tips")]
public sealed class RecompTipsController(IWebHostEnvironment environment) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? q,
        CancellationToken cancellationToken)
    {
        ConfigureShell();

        var search = (q ?? string.Empty).Trim();
        var now = DateTime.UtcNow;
        var posts = await RecompTipsStore.LoadAsync(environment, cancellationToken);

        var published = posts
            .Where(x => x.IsPublished)
            .Where(x => !x.PublishedAtUtc.HasValue || x.PublishedAtUtc <= now)
            .Where(x => MatchesSearch(x, search))
            .OrderByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ToList();

        return View(new RecompTipsIndexViewModel
        {
            Search = search,
            Posts = published
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(
        Guid id,
        CancellationToken cancellationToken)
    {
        ConfigureShell();

        var now = DateTime.UtcNow;
        var post = (await RecompTipsStore.LoadAsync(environment, cancellationToken))
            .SingleOrDefault(x =>
                x.Id == id
                && x.IsPublished
                && (!x.PublishedAtUtc.HasValue || x.PublishedAtUtc <= now));

        return post is null ? NotFound() : View(post);
    }

    private void ConfigureShell()
    {
        ViewData["PublicPage"] = !(User.Identity?.IsAuthenticated ?? false);
    }

    private static bool MatchesSearch(
        Models.RecompTipPost post,
        string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return Contains(post.Title, search)
            || Contains(post.Summary, search)
            || Contains(post.Body, search)
            || Contains(post.Tags, search);
    }

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
