using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/tips")]
public sealed class AdminRecompTipsController(
    IWebHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    ILogger<AdminRecompTipsController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var posts = await RecompTipsStore.LoadAsync(environment, cancellationToken);
        return View(posts
            .OrderByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ToList());
    }

    [HttpGet("new")]
    public IActionResult Create() =>
        View("Edit", new RecompTipEditViewModel
        {
            IsPublished = true
        });

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var post = (await RecompTipsStore.LoadAsync(environment, cancellationToken))
            .SingleOrDefault(x => x.Id == id);

        if (post is null)
        {
            return NotFound();
        }

        return View(new RecompTipEditViewModel
        {
            Id = post.Id,
            Title = post.Title,
            Summary = post.Summary,
            Body = post.Body,
            SourceUrl = post.SourceUrl,
            ImageUrl = post.ImageUrl,
            Tags = post.Tags,
            IsPublished = post.IsPublished
        });
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        RecompTipEditViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var posts = await RecompTipsStore.LoadAsync(environment, cancellationToken);
        var existing = model.Id.HasValue
            ? posts.SingleOrDefault(x => x.Id == model.Id.Value)
            : null;

        if (model.Id.HasValue && existing is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var sourceUrl = Clean(model.SourceUrl);
        var imageUrl = Clean(model.ImageUrl);
        var autoImageFound = false;

        if (string.IsNullOrWhiteSpace(imageUrl)
            && !string.IsNullOrWhiteSpace(sourceUrl))
        {
            imageUrl = await TryDiscoverArticleImageAsync(
                sourceUrl,
                cancellationToken);
            autoImageFound = !string.IsNullOrWhiteSpace(imageUrl);
        }

        var post = new RecompTipPost
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Title = model.Title.Trim(),
            Summary = model.Summary.Trim(),
            Body = model.Body?.Trim() ?? string.Empty,
            SourceUrl = sourceUrl,
            ImageUrl = imageUrl,
            Tags = Clean(model.Tags),
            IsPublished = model.IsPublished,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
            PublishedAtUtc = model.IsPublished
                ? existing?.PublishedAtUtc ?? now
                : null
        };

        await RecompTipsStore.SaveAsync(environment, post, cancellationToken);

        TempData["StatusMessage"] = autoImageFound
            ? "Post saved. The article image was found automatically."
            : model.IsPublished
                ? "Post saved and published."
                : "Draft saved.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublished(
        Guid id,
        CancellationToken cancellationToken)
    {
        var post = (await RecompTipsStore.LoadAsync(environment, cancellationToken))
            .SingleOrDefault(x => x.Id == id);

        if (post is null)
        {
            return NotFound();
        }

        post.IsPublished = !post.IsPublished;
        post.UpdatedAtUtc = DateTime.UtcNow;
        post.PublishedAtUtc = post.IsPublished
            ? post.PublishedAtUtc ?? DateTime.UtcNow
            : null;

        await RecompTipsStore.SaveAsync(environment, post, cancellationToken);
        TempData["StatusMessage"] = post.IsPublished
            ? "Post published."
            : "Post moved to draft.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await RecompTipsStore.DeleteAsync(
            environment,
            id,
            cancellationToken);

        TempData["StatusMessage"] = deleted
            ? "Post deleted."
            : "Post was already gone.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> TryDiscoverArticleImageAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
                || !await IsSafeExternalUriAsync(sourceUri, cancellationToken))
            {
                return null;
            }

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("12WeekRecomp-Tips/1.0");

            using var response = await client.GetAsync(
                sourceUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType)
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > 2_000_000)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (html.Length > 1_000_000)
            {
                html = html[..1_000_000];
            }

            var image = FindMetaImage(html);
            if (string.IsNullOrWhiteSpace(image))
            {
                return null;
            }

            image = WebUtility.HtmlDecode(image.Trim());
            if (!Uri.TryCreate(sourceUri, image, out var imageUri)
                || !await IsSafeExternalUriAsync(imageUri, cancellationToken))
            {
                return null;
            }

            return imageUri.ToString();
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or SocketException)
        {
            logger.LogInformation(
                ex,
                "Could not discover an image for recomp tip source {SourceUrl}.",
                sourceUrl);
            return null;
        }
    }

    private static string? FindMetaImage(string html)
    {
        var patterns = new[]
        {
            "<meta[^>]+(?:property|name)\\s*=\\s*[\"'](?:og:image|twitter:image(?::src)?)[\"'][^>]+content\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]*>",
            "<meta[^>]+content\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]+(?:property|name)\\s*=\\s*[\"'](?:og:image|twitter:image(?::src)?)[\"'][^>]*>"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                html,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromSeconds(1));

            if (match.Success)
            {
                return match.Groups["url"].Value;
            }
        }

        return null;
    }

    private static async Task<bool> IsSafeExternalUriAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.IsLoopback)
        {
            return false;
        }

        var addresses = await Dns.GetHostAddressesAsync(
            uri.DnsSafeHost,
            cancellationToken);

        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && !address.IsIPv6SiteLocal
                && (bytes[0] & 0xFE) != 0xFC;
        }

        return false;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
