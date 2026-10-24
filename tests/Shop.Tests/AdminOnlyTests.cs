using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Shop.Api.Admin;

namespace Shop.Tests;

public class AdminOnlyTests
{
    [Theory]
    [InlineData("127.0.0.1", null, null, true)]
    [InlineData("::1", null, null, true)]
    [InlineData("203.0.113.9", null, null, false)]
    [InlineData("203.0.113.9", null, "anything", false)]
    [InlineData("203.0.113.9", "secret", null, false)]
    [InlineData("203.0.113.9", "secret", "wrong", false)]
    [InlineData("203.0.113.9", "secret", "secret", true)]
    public async Task AnAdminRequestIsLetThroughFromThisMachineOrWithTheKey(string from, string? configuredKey, string? givenKey, bool allowed)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuredKey is null ? [] : new Dictionary<string, string?> { ["Admin:Key"] = configuredKey })
            .Build();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(from);
        if (givenKey is not null)
        {
            http.Request.Headers["X-Admin-Key"] = givenKey;
        }

        var result = await new AdminOnly(configuration).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http),
            _ => ValueTask.FromResult<object?>("let through")
        );

        if (allowed)
        {
            Assert.Equal("let through", result);
        }
        else
        {
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        }
    }
}
