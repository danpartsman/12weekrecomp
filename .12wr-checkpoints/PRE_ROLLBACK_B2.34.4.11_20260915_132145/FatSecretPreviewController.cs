using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Services;

namespace WebRecomp.Web.Controllers;

/// <summary>
/// B2.34.4.11 FATSECRET BASIC PREVIEW
///
/// Search/display only. This controller intentionally has no dependency on
/// INutritionFoodLogStore and never writes FatSecret Basic content to SQL.
/// </summary>
public sealed class FatSecretPreviewController(
    AppUserContextService userContext) : Controller
{
    private const string FatSecretUrl =
        "https://platform.fatsecret.com/rest/server.api";

    private static readonly HttpClient Http =
        new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

    [HttpGet]
    public async Task<IActionResult> Search(
        string? query,
        CancellationToken cancellationToken)
    {
        var context =
            await userContext.GetAsync(
                HttpContext,
                cancellationToken);

        if (context.Client is null
            || context.Program is null)
        {
            return Unauthorized(
                new
                {
                    success = false,
                    message =
                        "Client context is unavailable."
                });
        }

        if (!context.Program.NutritionFoodLogEnabled)
            return Forbid();

        var normalizedQuery =
            string.IsNullOrWhiteSpace(query)
                ? ""
                : query.Trim();

        if (normalizedQuery.Length < 2)
        {
            return Json(
                new
                {
                    success = true,
                    previewOnly = true,
                    foods =
                        Array.Empty<object>()
                });
        }

        try
        {
            var consumerKey =
                Environment.GetEnvironmentVariable(
                    "FATSECRET_OAUTH1_CONSUMER_KEY");

            var sharedSecret =
                Environment.GetEnvironmentVariable(
                    "FATSECRET_OAUTH1_SHARED_SECRET");

            if (string.IsNullOrWhiteSpace(
                    consumerKey)
                || string.IsNullOrWhiteSpace(
                    sharedSecret))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message =
                            "FatSecret OAuth1 credentials are not configured in this environment."
                    });
            }

            var parameters =
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["method"] =
                        "foods.search",

                    ["search_expression"] =
                        normalizedQuery,

                    ["page_number"] =
                        "0",

                    ["max_results"] =
                        "10",

                    ["format"] =
                        "json",

                    ["oauth_consumer_key"] =
                        consumerKey,

                    ["oauth_signature_method"] =
                        "HMAC-SHA1",

                    ["oauth_timestamp"] =
                        DateTimeOffset.UtcNow
                            .ToUnixTimeSeconds()
                            .ToString(
                                CultureInfo.InvariantCulture),

                    ["oauth_nonce"] =
                        Guid.NewGuid()
                            .ToString("N"),

                    ["oauth_version"] =
                        "1.0"
                };

            parameters["oauth_signature"] =
                BuildSignature(
                    HttpMethod.Post.Method,
                    FatSecretUrl,
                    parameters,
                    sharedSecret);

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    FatSecretUrl)
                {
                    Content =
                        new FormUrlEncodedContent(
                            parameters)
                };

            using var response =
                await Http.SendAsync(
                    request,
                    cancellationToken);

            var json =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);

            using var document =
                JsonDocument.Parse(
                    json);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)response.StatusCode,
                    new
                    {
                        success = false,
                        message =
                            ReadFatSecretError(
                                document.RootElement)
                            ?? "FatSecret Basic search failed."
                    });
            }

            if (TryGet(
                    document.RootElement,
                    "error",
                    out _))
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message =
                            ReadFatSecretError(
                                document.RootElement)
                            ?? "FatSecret Basic search failed."
                    });
            }

            var results =
                new List<object>();

            if (TryGet(
                    document.RootElement,
                    "foods",
                    out var foodsNode)
                && TryGet(
                    foodsNode,
                    "food",
                    out var foodNode))
            {
                foreach (
                    var food in
                    EnumerateObjectOrArray(
                        foodNode))
                {
                    var foodId =
                        ReadString(
                            food,
                            "food_id");

                    var foodName =
                        ReadString(
                            food,
                            "food_name");

                    if (string.IsNullOrWhiteSpace(
                            foodId)
                        || string.IsNullOrWhiteSpace(
                            foodName))
                    {
                        continue;
                    }

                    results.Add(
                        new
                        {
                            foodId,
                            foodName,
                            brandName =
                                ReadString(
                                    food,
                                    "brand_name"),
                            foodType =
                                ReadString(
                                    food,
                                    "food_type"),
                            description =
                                ReadString(
                                    food,
                                    "food_description")
                        });
                }
            }

            return Json(
                new
                {
                    success = true,
                    previewOnly = true,
                    source =
                        "FatSecret Basic",
                    foods =
                        results
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return BadRequest(
                new
                {
                    success = false,
                    message =
                        "FatSecret Basic preview could not be loaded."
                });
        }
    }

    private static string BuildSignature(
        string httpMethod,
        string baseUrl,
        IReadOnlyDictionary<string, string> parameters,
        string sharedSecret)
    {
        var normalized =
            string.Join(
                "&",
                parameters
                    .Select(
                        x => new
                        {
                            Key =
                                PercentEncode(
                                    x.Key),
                            Value =
                                PercentEncode(
                                    x.Value)
                        })
                    .OrderBy(
                        x => x.Key,
                        StringComparer.Ordinal)
                    .ThenBy(
                        x => x.Value,
                        StringComparer.Ordinal)
                    .Select(
                        x =>
                            $"{x.Key}={x.Value}"));

        var baseString =
            string.Concat(
                httpMethod.ToUpperInvariant(),
                "&",
                PercentEncode(
                    baseUrl),
                "&",
                PercentEncode(
                    normalized));

        var signingKey =
            string.Concat(
                PercentEncode(
                    sharedSecret),
                "&");

        using var hmac =
            new HMACSHA1(
                Encoding.ASCII.GetBytes(
                    signingKey));

        var digest =
            hmac.ComputeHash(
                Encoding.ASCII.GetBytes(
                    baseString));

        return Convert.ToBase64String(
            digest);
    }

    private static string PercentEncode(
        string value)
        => Uri.EscapeDataString(
                value ?? "")
            .Replace(
                "!",
                "%21",
                StringComparison.Ordinal)
            .Replace(
                "'",
                "%27",
                StringComparison.Ordinal)
            .Replace(
                "(",
                "%28",
                StringComparison.Ordinal)
            .Replace(
                ")",
                "%29",
                StringComparison.Ordinal)
            .Replace(
                "*",
                "%2A",
                StringComparison.Ordinal);

    private static string? ReadFatSecretError(
        JsonElement root)
    {
        if (!TryGet(
                root,
                "error",
                out var errorNode))
        {
            return null;
        }

        var code =
            ReadString(
                errorNode,
                "code");

        var message =
            ReadString(
                errorNode,
                "message");

        if (string.IsNullOrWhiteSpace(
                message))
        {
            return "FatSecret returned an API error.";
        }

        return string.IsNullOrWhiteSpace(
                code)
            ? message
            : $"FatSecret error {code}: {message}";
    }

    private static IEnumerable<JsonElement>
        EnumerateObjectOrArray(
            JsonElement node)
    {
        if (node.ValueKind
            == JsonValueKind.Array)
        {
            foreach (
                var item in
                node.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (node.ValueKind
            == JsonValueKind.Object)
        {
            yield return node;
        }
    }

    private static bool TryGet(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind
            == JsonValueKind.Object)
        {
            foreach (
                var property in
                element.EnumerateObject())
            {
                if (property.Name.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value =
                        property.Value;

                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(
        JsonElement element,
        string name)
    {
        if (!TryGet(
                element,
                name,
                out var value))
        {
            return null;
        }

        if (value.ValueKind
            == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind
            == JsonValueKind.Number)
        {
            return value.GetRawText();
        }

        return null;
    }
}