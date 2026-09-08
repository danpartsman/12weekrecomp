using System.Net;

namespace TwelveWeekRecomp.Live.Services;

public static class EmailTemplates
{
    public static string Welcome(
        string name,
        string interestCode,
        string unsubscribeUrl)
    {
        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name.Trim());
        var safeUrl = WebUtility.HtmlEncode(unsubscribeUrl);
        var interest = interestCode == "FOUNDERS_RATE"
            ? "We've recorded your interest in the Founders Rate. We'll send the final launch details before it opens."
            : "We'll let you know when the free-trial launch opens and send important 12 Week Recomp updates.";

        return Wrap($"""
            <h1 style="margin:0 0 18px;font-size:26px;">You're on the 12 Week Recomp list.</h1>
            <p>Hi {safeName},</p>
            <p>Thanks for signing up. {WebUtility.HtmlEncode(interest)}</p>
            <p>12 Week Recomp is built around a simple idea: use individualized training and nutrition guidance to help you lose fat while building or preserving muscle, then teach you the habits so you do not need to stay trapped in an ongoing subscription.</p>
            <p>The full program is planned at <strong>$20 for 12 weeks</strong>.</p>
            <p>We'll keep you posted as launch gets closer.</p>
            <p style="margin-top:28px;">— 12 Week Recomp</p>
            <p style="margin-top:30px;font-size:12px;color:#8793a7;">Don't want launch updates? <a href="{safeUrl}" style="color:#72d6ff;">Unsubscribe here</a>.</p>
            """);
    }

    public static string Broadcast(
        string messageText,
        string unsubscribeUrl)
    {
        var safeMessage = WebUtility.HtmlEncode(messageText ?? "")
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>");
        var safeUrl = WebUtility.HtmlEncode(unsubscribeUrl);

        return Wrap($"""
            <div style="font-size:16px;line-height:1.65;">{safeMessage}</div>
            <p style="margin-top:32px;font-size:12px;color:#8793a7;">You received this because you signed up for 12 Week Recomp updates. <a href="{safeUrl}" style="color:#72d6ff;">Unsubscribe</a>.</p>
            """);
    }

    private static string Wrap(string body) => $"""
        <!doctype html>
        <html>
        <body style="margin:0;padding:0;background:#07111f;color:#eef6ff;font-family:Arial,Helvetica,sans-serif;">
          <div style="max-width:640px;margin:0 auto;padding:36px 24px;">
            <div style="font-size:14px;font-weight:700;letter-spacing:.08em;color:#72d6ff;margin-bottom:24px;">12 WEEK RECOMP</div>
            {body}
          </div>
        </body>
        </html>
        """;
}
