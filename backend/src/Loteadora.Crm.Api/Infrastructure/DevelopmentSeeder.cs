using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Loteadora.Crm.Api.Infrastructure;

/// <summary>
/// Dados FICTÍCIOS para desenvolvimento local. Nunca executa fora de Development.
/// A senha inicial vem de configuração local (user-secrets/.env.local), nunca do repositório.
/// </summary>
public sealed class DevelopmentSeeder(AppDbContext db, OrganizationProvisioning provisioning, IServiceScopeFactory scopes, ILogger<DevelopmentSeeder> logger)
{
    public async Task SeedAsync(string password, CancellationToken ct)
    {
        if (await db.Organizations.AnyAsync(ct))
            return;

        var demo = await provisioning.ProvisionAsync("Loteadora Demo", "demo",
        [
            new ProvisionedUser("admin@demo.local", "Ana Administradora", SystemRoles.Admin, null),
            new ProvisionedUser("gestor@demo.local", "Gustavo Gestor", SystemRoles.Manager, "Time Norte"),
            new ProvisionedUser("corretor1@demo.local", "Marcos Corretor", SystemRoles.Agent, "Time Norte"),
            new ProvisionedUser("corretor2@demo.local", "Carla Corretora", SystemRoles.Agent, "Time Sul"),
        ], password, ct);

        // Segunda organização: útil para verificar manualmente o isolamento entre tenants.
        await provisioning.ProvisionAsync("Outra Loteadora", "outra",
        [
            new ProvisionedUser("admin@outra.local", "Otávio Admin", SystemRoles.Admin, null),
        ], password, ct);

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().InitializeSystem(demo.OrganizationId,
            Capabilities.All.ToDictionary(c => c, _ => CapabilityScope.Org));
        var leads = scope.ServiceProvider.GetRequiredService<LeadService>();

        var marcos = demo.UserIdsByEmail["corretor1@demo.local"];
        var carla = demo.UserIdsByEmail["corretor2@demo.local"];
        var samples = new (string Name, string Phone, string Source, string Interest, Guid Owner, int Stage)[]
        {
            ("João Pereira", "11987650001", "Meta Ads", "Lote de esquina no Residencial Primavera", marcos, 0),
            ("Mariana Souza", "11987650002", "Site", "Lote até R$ 150 mil", marcos, 1),
            ("Paulo Lima", "11987650003", "Indicação", "Condomínio fechado", marcos, 2),
            ("Fernanda Alves", "11987650004", "Google Ads", "Investimento", carla, 0),
            ("Ricardo Gomes", "11987650005", "Plantão", "Lote 360 m²", carla, 3),
            ("Beatriz Costa", "11987650006", "Meta Ads", "Visita no sábado", marcos, 4),
            ("Lucas Martins", "11987650007", "Site", "Proposta parcelada", carla, 5),
        };

        foreach (var s in samples)
        {
            var id = await leads.CreateAsync(new CreateLeadCommand(s.Name, s.Phone, null, s.Source, "Lançamento 2026", s.Interest, demo.StageIds[s.Stage], s.Owner), ct);
            _ = id;
        }

        logger.LogInformation("Dados de desenvolvimento criados (organizações 'demo' e 'outra').");
    }
}

public static class DatabaseStartup
{
    public static async Task MigrateAndSeedDevelopmentAsync(this IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var password = configuration["Seed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            scope.ServiceProvider.GetRequiredService<ILogger<DevelopmentSeeder>>()
                .LogWarning("Seed:Password não configurada — dados de desenvolvimento não serão criados.");
            return;
        }

        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(password, CancellationToken.None);
    }
}
