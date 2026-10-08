using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Loteadora.Crm.Tests.Infrastructure;

/// <summary>
/// Sobe a API real contra um banco PostgreSQL efêmero (criado e removido por execução).
/// Conexão base: variável CRM_TEST_CONNECTION ou CRM_DB_CONNECTION do arquivo .env.local da raiz.
/// </summary>
public sealed class CrmApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"crm_test_{Guid.NewGuid():N}";
    private string _connectionString = default!;
    private string _adminConnectionString = default!;

    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
    public ProvisionedOrganization OrgA { get; private set; } = default!;
    public ProvisionedOrganization OrgB { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        _adminConnectionString = ResolveBaseConnectionString();
        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName };
        _connectionString = builder.ConnectionString;

        await using (var conn = new NpgsqlConnection(_adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        var provisioning = Services.GetRequiredService<OrganizationProvisioning>();
        OrgA = await provisioning.ProvisionAsync("Org A", "org-a",
        [
            new ProvisionedUser("admin@a.test", "Admin A", SystemRoles.Admin, null),
            new ProvisionedUser("gestor.norte@a.test", "Gestor Norte", SystemRoles.Manager, "Norte"),
            new ProvisionedUser("corretor.norte1@a.test", "Corretor Norte 1", SystemRoles.Agent, "Norte"),
            new ProvisionedUser("corretor.norte2@a.test", "Corretor Norte 2", SystemRoles.Agent, "Norte"),
            new ProvisionedUser("corretor.sul@a.test", "Corretor Sul", SystemRoles.Agent, "Sul"),
            new ProvisionedUser("lockout@a.test", "Lockout", SystemRoles.Agent, "Sul"),
        ], Password, CancellationToken.None);

        OrgB = await provisioning.ProvisionAsync("Org B", "org-b",
        [
            new ProvisionedUser("admin@b.test", "Admin B", SystemRoles.Admin, null),
        ], Password, CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Auth:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "10000");
        builder.UseSetting("Auth:RefreshReuseGracePeriod", "00:00:00");
        builder.ConfigureServices(services =>
        {
            // O dispatcher do outbox é exercitado explicitamente nos testes.
            var hosted = services.Where(d => d.ImplementationType == typeof(Modules.Outbox.OutboxDispatcher)).ToList();
            foreach (var d in hosted)
                services.Remove(d);

            services.AddSingleton<Modules.BusinessCalendar.Application.IHolidayProvider, FakeHolidayProvider>();
        });
    }

    public async Task<HttpClient> LoginAsync(string email, string? organization = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, organization });
        response.EnsureSuccessStatusCode();
        var tokens = (await response.Content.ReadFromJsonAsync<TokenPair>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ResolveBaseConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CRM_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return WithDatabase(fromEnv, "postgres");

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, ".env.local");
            if (!File.Exists(file))
                continue;

            var line = File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith("CRM_DB_CONNECTION=", StringComparison.Ordinal));
            if (line is not null)
                return WithDatabase(line["CRM_DB_CONNECTION=".Length..].Trim(), "postgres");
        }

        throw new InvalidOperationException("Defina CRM_TEST_CONNECTION ou CRM_DB_CONNECTION em .env.local para rodar os testes de integração.");
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<CrmApiFactory>
{
    public const string Name = "api";
}
