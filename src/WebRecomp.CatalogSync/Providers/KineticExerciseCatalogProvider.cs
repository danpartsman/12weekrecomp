using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using WebRecomp.WorkoutIntelligence.Catalog;

namespace WebRecomp.CatalogSync.Providers;

public sealed class KineticExerciseCatalogProvider(HttpClient httpClient)
    : IExerciseCatalogProvider
{
    public const string ExercisesUrl =
        "https://raw.githubusercontent.com/kinetic-place/exercises-db/main/en/exercises.json";

    public ExerciseCatalogProviderDescriptor Descriptor { get; } =
        new(
            CatalogSyncConstants.KineticProviderCode,
            "Kinetic Exercises Database",
            "MIT",
            true);

    public async Task<ExerciseCatalogFetchResult> FetchAsync(
        ExerciseCatalogFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, ExercisesUrl);

        if (!string.IsNullOrWhiteSpace(request.PreviousEtag) &&
            EntityTagHeaderValue.TryParse(request.PreviousEtag, out var priorEtag))
        {
            message.Headers.IfNoneMatch.Add(priorEtag);
        }

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (string.IsNullOrWhiteSpace(request.PreviousSha256))
                throw new InvalidOperationException(
                    "Kinetic returned 304 but no previous dataset SHA-256 is available.");

            return new ExerciseCatalogFetchResult(
                Changed: false,
                Etag: request.PreviousEtag,
                Sha256: request.PreviousSha256,
                SourceVersion: request.PreviousEtag,
                CapturedAtUtc: DateTime.UtcNow,
                Snapshot: null);
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var sha256 = ComputeSha256(json);
        var etag = response.Headers.ETag?.ToString();

        if (!string.IsNullOrWhiteSpace(request.PreviousSha256) &&
            string.Equals(request.PreviousSha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ExerciseCatalogFetchResult(
                Changed: false,
                Etag: etag ?? request.PreviousEtag,
                Sha256: sha256,
                SourceVersion: etag ?? sha256[..12],
                CapturedAtUtc: DateTime.UtcNow,
                Snapshot: null);
        }

        var reader = new KineticExerciseCatalogReader();
        var snapshot = reader.Read(json);

        if (snapshot.Exercises.Count < 899)
            throw new InvalidDataException(
                $"Kinetic returned only {snapshot.Exercises.Count} exercises; expected at least 899.");

        return new ExerciseCatalogFetchResult(
            Changed: true,
            Etag: etag,
            Sha256: sha256,
            SourceVersion: etag ?? sha256[..12],
            CapturedAtUtc: DateTime.UtcNow,
            Snapshot: snapshot);
    }

    private static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
