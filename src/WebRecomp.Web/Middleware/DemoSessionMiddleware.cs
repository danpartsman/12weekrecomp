using WebRecomp.Web.Services;

namespace WebRecomp.Web.Middleware;

public sealed class DemoSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        DemoSessionService demoSessions)
    {
        // Permanent Entra/OIDC identity always wins.
        // Demo authentication is considered only when normal
        // customer authentication left the request anonymous.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var demoPrincipal =
                await demoSessions.TryRestorePrincipalAsync(
                    context,
                    context.RequestAborted);

            if (demoPrincipal is not null)
                context.User = demoPrincipal;
        }

        await next(context);

        // Record only successful HTML GET requests.
        // Static files, API/AJAX calls, redirects and POSTs do not
        // count as demo page views.
        if (DemoSessionService.IsDemoPrincipal(context.User))
        {
            await demoSessions.TrackPageViewAsync(
                context,
                context.RequestAborted);
        }
    }
}
