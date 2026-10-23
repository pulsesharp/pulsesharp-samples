using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PulseSharp;

namespace Shop.Tests;

/// <summary>
/// The whole Shop.Api in the test process, on a throwaway PostgreSQL database (<c>shop_tests</c>) in the compose container.
/// When PostgreSQL isn't running, <see cref="Available"/> is false and the tests skip themselves.
/// </summary>
public sealed class ShopCloud : IAsyncLifetime
{
    private const string DatabaseName = "shop_tests";

    private readonly string _server = Environment.GetEnvironmentVariable("SHOP_TESTS_POSTGRES")
        ?? "Host=localhost;Port=55433;Username=shop;Password=shop";

    private WebApplicationFactory<Program>? _factory;

    public bool Available { get; private set; }

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services
    {
        get
        {
            return _factory!.Services;
        }
    }

    public IPulse Pulse
    {
        get
        {
            return _factory!.Services.GetRequiredService<IPulse>();
        }
    }

    public IPulseControl Control
    {
        get
        {
            return _factory!.Services.GetRequiredService<IPulseControl>();
        }
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var admin = new NpgsqlConnection(_server + ";Database=postgres;Timeout=3");
            await admin.OpenAsync();
            await Run(admin, $"DROP DATABASE IF EXISTS {DatabaseName} WITH (FORCE)");
            await Run(admin, $"CREATE DATABASE {DatabaseName}");
        }
        catch (NpgsqlException)
        {
            return;
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Shop", $"{_server};Database={DatabaseName}");
        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
        Available = true;

        await Eventually(async () => (await Client.GetFromJsonAsync<List<object>>("/menu"))!.Count == 6);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Shop", null);
    }

    public async Task<long> Count(string sql)
    {
        await using var connection = new NpgsqlConnection($"{_server};Database={DatabaseName}");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public static async Task Eventually(Func<Task<bool>> condition, string? because = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Timed out waiting for: " + because);
    }

    private static async Task Run(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
