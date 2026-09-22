using WebRecomp.Web.Services;

namespace WebRecomp.Web.Middleware;

public sealed class TestPersonaMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        TestPersonaSessionService sessions)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var principal =
                await sessions.TryRestorePrincipalAsync(
                    context,
                    context.RequestAborted);

            if (principal is not null)
                context.User = principal;
        }

        await next(context);
    }
}
