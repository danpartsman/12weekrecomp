using Microsoft.AspNetCore.DataProtection;
using WebRecomp.Web.Services;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class LocalPasswordResetTokenServiceTests
{
    [Fact]
    public void Token_RoundTripsBeforeExpiry()
    {
        var service =
            new LocalPasswordResetTokenService(
                new EphemeralDataProtectionProvider());

        var now =
            new DateTimeOffset(
                2026, 10, 6, 8, 0, 0,
                TimeSpan.Zero);

        var userId =
            Guid.NewGuid();

        var token =
            service.Create(
                userId,
                "person@example.com",
                new string('a', 64),
                now,
                TimeSpan.FromHours(1));

        var valid =
            service.TryRead(
                token,
                now.AddMinutes(30),
                out var payload);

        Assert.True(valid);
        Assert.NotNull(payload);
        Assert.Equal(userId, payload!.UserId);
        Assert.Equal("person@example.com", payload.NormalizedEmail);
    }

    [Fact]
    public void Token_Expires()
    {
        var service =
            new LocalPasswordResetTokenService(
                new EphemeralDataProtectionProvider());

        var now =
            new DateTimeOffset(
                2026, 10, 6, 8, 0, 0,
                TimeSpan.Zero);

        var token =
            service.Create(
                Guid.NewGuid(),
                "person@example.com",
                new string('b', 64),
                now,
                TimeSpan.FromHours(1));

        var valid =
            service.TryRead(
                token,
                now.AddMinutes(61),
                out _);

        Assert.False(valid);
    }

    [Fact]
    public void TamperedToken_IsRejected()
    {
        var service =
            new LocalPasswordResetTokenService(
                new EphemeralDataProtectionProvider());

        var now =
            DateTimeOffset.UtcNow;

        var token =
            service.Create(
                Guid.NewGuid(),
                "person@example.com",
                new string('c', 64),
                now,
                TimeSpan.FromHours(1));

        var last =
            token[^1] == 'A'
                ? 'B'
                : 'A';

        var tampered =
            token[..^1]
            + last;

        Assert.False(
            service.TryRead(
                tampered,
                now,
                out _));
    }
}
