using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Shop.Api.Admin;

/// <summary>
/// Lets a request through only when it comes from this machine, or carries the key in the <c>X-Admin-Key</c> header
/// (set <c>Admin:Key</c> in the configuration). Without this, anyone who can reach the API could change prices or stop receipts.
/// </summary>
public sealed class AdminOnly(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext;
        var address = request.Connection.RemoteIpAddress;
        var fromThisMachine = address is null || IPAddress.IsLoopback(address);

        if (!fromThisMachine && !HasTheKey(request))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    private bool HasTheKey(HttpContext request)
    {
        var key = configuration["Admin:Key"];
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var given = Encoding.UTF8.GetBytes(request.Request.Headers["X-Admin-Key"].ToString());
        return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(key));
    }
}
