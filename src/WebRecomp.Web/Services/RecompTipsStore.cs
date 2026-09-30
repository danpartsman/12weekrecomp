using System.Text;
using System.Text.Json;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public static class RecompTipsStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public static async Task<IReadOnlyList<RecompTipPost>> LoadAsync(
        IWebHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnlockedAsync(environment, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task SaveAsync(
        IWebHostEnvironment environment,
        RecompTipPost post,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var posts = await LoadUnlockedAsync(environment, cancellationToken);
            var existingIndex = posts.FindIndex(x => x.Id == post.Id);

            if (existingIndex >= 0)
            {
                posts[existingIndex] = post;
            }
            else
            {
                posts.Add(post);
            }

            await WriteUnlockedAsync(environment, posts, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<bool> DeleteAsync(
        IWebHostEnvironment environment,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var posts = await LoadUnlockedAsync(environment, cancellationToken);
            var removed = posts.RemoveAll(x => x.Id == id) > 0;

            if (removed)
            {
                await WriteUnlockedAsync(environment, posts, cancellationToken);
            }

            return removed;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<List<RecompTipPost>> LoadUnlockedAsync(
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var path = GetDataPath(environment);
        if (!File.Exists(path))
        {
            return new List<RecompTipPost>();
        }

        await using var stream = File.OpenRead(path);
        var posts = await JsonSerializer.DeserializeAsync<List<RecompTipPost>>(
            stream,
            JsonOptions,
            cancellationToken);

        return posts ?? new List<RecompTipPost>();
    }

    private static async Task WriteUnlockedAsync(
        IWebHostEnvironment environment,
        List<RecompTipPost> posts,
        CancellationToken cancellationToken)
    {
        var path = GetDataPath(environment);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var ordered = posts
            .OrderByDescending(x => x.PublishedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ToList();

        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await File.WriteAllTextAsync(
            tempPath,
            json,
            Encoding.UTF8,
            cancellationToken);

        File.Move(tempPath, path, overwrite: true);
    }

    private static string GetDataPath(IWebHostEnvironment environment)
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        var directory = !string.IsNullOrWhiteSpace(home)
            ? Path.Combine(home, "data", "12weekrecomp")
            : Path.Combine(environment.ContentRootPath, "App_Data");

        return Path.Combine(directory, "recomp-tips.json");
    }
}
