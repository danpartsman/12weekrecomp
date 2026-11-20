using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachTaskProof(string ContentType, byte[] Image);

public sealed class CoachTaskProofStore(NotificationFiles files)
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public async Task<Guid> SaveAsync(Stream upload, long length, CancellationToken ct)
    {
        if (length is < 16 or > MaxBytes)
            throw new ArgumentException("Choose a JPEG, PNG, or WebP picture up to 8 MB.");
        var bytes = new byte[(int)length];
        await upload.ReadExactlyAsync(bytes, ct);
        var contentType = DetectType(bytes)
            ?? throw new ArgumentException("Choose a JPEG, PNG, or WebP picture.");
        var id = Guid.NewGuid();
        files.Write(Key(id), new CoachTaskProof(contentType, bytes));
        return id;
    }

    public CoachTaskProof? Read(Guid id) => files.Read<CoachTaskProof>(Key(id));

    internal void Delete(Guid id) => File.Delete(Path.Combine(files.Root, Key(id) + ".json"));

    private static string Key(Guid id) => $"users/task-proof-{id:N}";

    private static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            return "image/png";
        if (bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
