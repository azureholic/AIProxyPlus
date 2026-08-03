using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AiProxy.Api.Authentication;

public sealed class SessionAntiforgeryEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!HttpMethods.IsGet(httpContext.Request.Method) &&
            !HttpMethods.IsHead(httpContext.Request.Method))
        {
            var cookieAuthentication = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (cookieAuthentication.Succeeded)
            {
                try
                {
                    await httpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(httpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest(new { error = "A valid antiforgery token is required." });
                }
            }
        }

        return await next(context);
    }
}