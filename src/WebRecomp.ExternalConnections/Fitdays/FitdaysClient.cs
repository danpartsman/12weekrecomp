using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebRecomp.ExternalConnections.Fitdays;

// Protocol adapted from roquerodrigo/fitdays-api (MIT); see Documentation/FITDAYS_TEST.md.
public sealed class FitdaysClient(HttpClient http)
{
    public static string RegionHost(string region) => region switch
    {
        "us" => "https://online-us.fitdays.cn",
        "eu" => "https://online-eu.fitdays.cn",
        "cn" => "https://online.fitdays.cn",
        _ => throw new FitdaysPreviewException("Choose a valid account region.")
    };
    private static string Md5(string value) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    public static string HashPassword(string password) => Md5(Md5(password + "hx"));
    public static string Sign(IReadOnlyDictionary<string, string> values)
    {
        var raw = string.Join("&", values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}")) + "hxsign";
        return Md5(WebUtility.UrlEncode(raw)).ToLowerInvariant();
    }

    public Task<FitdaysSession> LoginAsync(string email, string password, string region, CancellationToken ct, string appVersion = "1.21.0")
        => LoginHashedAsync(email, HashPassword(password), region, ct, appVersion);

    public async Task<FitdaysSession> LoginHashedAsync(string email, string passwordHash, string region, CancellationToken ct, string appVersion = "1.21.0")
    {
        var session = new FitdaysSession { Host = RegionHost(region), Region = region, AppVersion = appVersion, ClientId = Md5(Guid.NewGuid().ToString()) };
        using var response = await RequestAsync(session, "api/users/login", new { email, password = passwordHash }, ct);
        var data = response.RootElement.GetProperty("data");
        var account = data.TryGetProperty("account", out var a) ? a : data;
        session.Token = Text(data, "token") ?? Text(account, "token") ?? "";
        session.Uid = Number(account, "uid") ?? Number(data, "uid") ?? 0;
        if (session.Token.Length == 0 || session.Uid <= 0) throw new FitdaysPreviewException("Fitdays did not return a usable session. Please reconnect.");
        return session;
    }

    public async Task<FitdaysSnapshot> ReadAsync(FitdaysSession session, string timezone, CancellationToken ct, int historyDays = 30, int newestOffsetHours = 0)
    {
        var now = DateTimeOffset.UtcNow;
        using var doc = await RequestAsync(session, "api/sync/syncFromServer", new { start_time = now.AddHours(Math.Clamp(newestOffsetHours,0,24)).ToUnixTimeSeconds(), end_time = now.AddDays(-Math.Clamp(historyDays, 1, 90)).ToUnixTimeSeconds() }, ct);
        var snapshot = Parse(doc.RootElement.GetProperty("data"), timezone);
        snapshot.Readings.RemoveAll(r => r.MeasuredAt < now.AddDays(-Math.Clamp(historyDays, 1, 90)) || r.MeasuredAt > now.AddMinutes(5));
        return snapshot;
    }

    private async Task<JsonDocument> RequestAsync(FitdaysSession session, string path, object body, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var values = new Dictionary<string, string>
            {
                ["app_ver"] = session.AppVersion, ["client_id"] = session.ClientId,
                ["country"] = session.Region == "cn" ? "CN" : "US", ["language"] = session.Region == "cn" ? "zh" : "en",
                ["device_model"] = "AndroidSDKbuiltforarm64-6.0", ["os_type"] = "0", ["source"] = "0",
                ["request_id"] = Md5(Guid.NewGuid().ToString()).ToLowerInvariant(), ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["token"] = session.Token, ["uid"] = session.Uid.ToString(CultureInfo.InvariantCulture)
            };
            var query = string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{session.Host}/{path}?{query}&sign={Sign(values)}&capp_ver={Uri.EscapeDataString(session.AppVersion)}");
            request.Headers.UserAgent.ParseAdd("okhttp/4.9.3");
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new FitdaysPreviewException("Fitdays is unavailable or rejected the request. Check your account details and try again later.") { SessionRejected = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden };
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + count > 2_000_000) throw new FitdaysPreviewException("Fitdays returned too much data for this preview.");
                buffer.Write(chunk, 0, count);
            }
            buffer.Position = 0;
            var doc = await JsonDocument.ParseAsync(buffer, cancellationToken: ct);
            var code = Number(doc.RootElement, "code");
            if (code == 302)
            {
                var target = doc.RootElement.TryGetProperty("data", out var d) ? Text(d, "domain") : null;
                doc.Dispose();
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !new[] { "online.fitdays.cn", "online-us.fitdays.cn", "online-eu.fitdays.cn" }.Contains(uri.Host))
                    throw new FitdaysPreviewException("Fitdays returned an unsupported server region.");
                session.Host = uri.GetLeftPart(UriPartial.Authority);
                continue;
            }
            if (code is not (0 or 200))
            {
                doc.Dispose();
                throw new FitdaysPreviewException($"Fitdays API rejected {path} (code {code?.ToString(CultureInfo.InvariantCulture) ?? "missing"}). No automatic re-login was attempted; check Fitdays account status and API response.") { SessionRejected = code is 401 or 403 or 1001 };
            }
            return doc;
        }
        throw new FitdaysPreviewException("Fitdays could not resolve your account region.");
    }

    public static FitdaysSnapshot Parse(JsonElement data, string timezone)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { throw new FitdaysPreviewException("Your Recomp profile needs a valid time zone before previewing measurements."); }
        catch (InvalidTimeZoneException) { throw new FitdaysPreviewException("Your Recomp profile time zone is invalid."); }
        var profiles = new List<FitdaysPerson>();
        void AddProfiles(JsonElement parent)
        {
            if (!parent.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array) return;
            foreach (var u in users.EnumerateArray())
            {
                var id = Number(u, "suid");
                if (id > 0 && Number(u, "is_deleted") != 1 && !profiles.Any(x => x.Id == id))
                    profiles.Add(new(id.Value, Text(u, "nickname") ?? $"Profile {id}"));
            }
        }
        AddProfiles(data);
        if (data.TryGetProperty("account", out var account)) AddProfiles(account);
        var readings = new List<FitdaysReading>();
        if (data.TryGetProperty("weight_list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var w in list.EnumerateArray().Take(1000))
            {
                var id = Number(w, "suid");
                var timestamp = Number(w, "measured_time");
                if (Number(w, "is_deleted") == 1 || id is null || timestamp is null || !profiles.Any(p => p.Id == id)) continue;
                DateTimeOffset measured;
                try { measured = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(timestamp.Value), zone); }
                catch (ArgumentOutOfRangeException) { continue; }
                var grams = Decimal(w, "weight_g");
                var kg = Decimal(w, "weight_kg");
                var lb = grams > 0 ? grams / 1000m * 2.20462262185m : kg > 0 ? kg * 2.20462262185m : Decimal(w, "weight_lb");
                if (lb is null or <= 0 or > 1500) continue;
                readings.Add(new(id.Value, measured, lb.Value, Text(w, "data_id") ?? Number(w, "id")?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable", Decimal(w, "bfr"), Decimal(w, "rom"), Decimal(w, "rosm"), Decimal(w, "vwc"), Decimal(w, "sfr"), Decimal(w, "uvi")));
            }
        }
        var rawDates = new Dictionary<long, List<long>>();
        var rawCount=0;var malformed=0;var deleted=0;long? latestRaw=null;
        if (data.TryGetProperty("weight_list", out var rawList) && rawList.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rawList.EnumerateArray())
            {
                rawCount++;
                if (Number(row, "is_deleted") == 1) {deleted++;continue;}
                var rawPerson = Number(row, "suid"); var rawTime = Number(row, "measured_time");
                if (rawPerson is null || rawTime is null) {malformed++;continue;}
                latestRaw=latestRaw is null?rawTime.Value:Math.Max(latestRaw.Value,rawTime.Value);
                if (!rawDates.TryGetValue(rawPerson.Value, out var dates)) rawDates[rawPerson.Value] = dates = [];
                dates.Add(rawTime.Value);
            }
        }
        return new(profiles, readings.OrderByDescending(x => x.MeasuredAt).ToList(), DateTimeOffset.UtcNow, timezone) { RawMeasurementTimes = rawDates, RawRecordCount=rawCount, RawMalformedCount=malformed, RawDeletedCount=deleted, RawLatestTimestamp=latestRaw, AccountApiHost=data.TryGetProperty("account",out var accountInfo) && Uri.TryCreate(Text(accountInfo,"api_url"),UriKind.Absolute,out var accountUri) ? accountUri.Host : null };
    }
    private static string? Text(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static decimal? Decimal(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var n)) return n;
        return p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : null;
    }
    private static long? Number(JsonElement e, string name) { var n = Decimal(e, name); return n.HasValue && n >= long.MinValue && n <= long.MaxValue && decimal.Truncate(n.Value) == n ? (long)n.Value : null; }
}
public sealed class FitdaysPreviewException(string message) : Exception(message) { public bool SessionRejected { get; init; } }
public sealed class FitdaysSession
{
    public string AppVersion { get; set; } = "1.21.0";
    public string Host { get; set; } = "";
    public string Region { get; set; } = "us";
    public string ClientId { get; set; } = "";
    public string Token { get; set; } = "";
    public long Uid { get; set; }
}
public sealed record FitdaysPerson(long Id, string Name);
public sealed record FitdaysReading(long PersonId, DateTimeOffset MeasuredAt, decimal WeightLb, string RecordId, decimal? Bfr, decimal? Rom, decimal? Rosm, decimal? Vwc, decimal? Sfr, decimal? Uvi);
public sealed record FitdaysSnapshot(List<FitdaysPerson> People, List<FitdaysReading> Readings, DateTimeOffset FetchedAt, string Timezone)
{
    public Dictionary<long, List<long>> RawMeasurementTimes { get; init; } = new();
    public int RawRecordCount { get; init; }
    public int RawMalformedCount { get; init; }
    public int RawDeletedCount { get; init; }
    public long? RawLatestTimestamp { get; init; }
    public string? AccountApiHost { get; init; }
}
