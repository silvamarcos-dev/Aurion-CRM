using Loteadora.Crm.Modules.BusinessCalendar.Domain;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Inbox.Application;
using Loteadora.Crm.Modules.Inbox.Domain;
using Loteadora.Crm.Modules.Macros.Application;
using Loteadora.Crm.Modules.Scheduling.Application;
using Loteadora.Crm.Modules.Scheduling.Domain;
using Loteadora.Crm.Modules.Tasks.Application;
using Loteadora.Crm.Modules.Tasks.Domain;
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
            await SeedRoutineForExistingDemoAsync(ct);
        else
            await SeedFreshAsync(password, ct);

        await SeedInboxForDemoAsync(ct);
    }

    /// <summary>Fase 3: canal de WhatsApp simulado, templates e conversas de exemplo (uma única vez).</summary>
    private async Task SeedInboxForDemoAsync(CancellationToken ct)
    {
        var demoOrg = await db.Organizations.FirstOrDefaultAsync(o => o.Slug == "demo", ct);
        if (demoOrg is null || await db.Channels.IgnoreQueryFilters().AnyAsync(c => c.OrganizationId == demoOrg.Id, ct))
            return;

        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<RequestContext>().InitializeSystem(demoOrg.Id, Capabilities.All.ToDictionary(c => c, _ => CapabilityScope.Org));
        var scopedDb = sp.GetRequiredService<AppDbContext>();
        var owner = await (from m in scopedDb.Memberships
                           join u in scopedDb.Users on m.UserId equals u.Id
                           where u.Email == "corretor1@demo.local"
                           select (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (owner is null)
            return;

        var channels = sp.GetRequiredService<ChannelService>();
        var channelId = await channels.CreateAsync(new SaveChannelCommand(ChannelProvider.Simulator, "WhatsApp (simulado)", "11900000000", null, owner.Value, null), ct);
        await channels.CreateTemplateAsync(channelId, new SaveTemplateCommand("retomar_contato", "pt_BR",
            "Olá, {{1}}! Aqui é {{2}}, da Loteadora Demo. Podemos continuar nossa conversa sobre o seu lote?"), ct);
        await channels.CreateTemplateAsync(channelId, new SaveTemplateCommand("confirmacao_visita", "pt_BR",
            "Olá, {{1}}! Sua visita ao empreendimento está confirmada para {{2}}. Qualquer dúvida, é só responder."), ct);

        var simulator = sp.GetRequiredService<SimulatorService>();
        await simulator.InboundAsync(new SimulateInboundCommand(channelId, "11987650001", "João Pereira",
            "Oi! Vi o anúncio do Residencial Primavera. Ainda tem lote de esquina disponível?"), ct);
        await simulator.InboundAsync(new SimulateInboundCommand(channelId, "11912345678", "Patrícia Ramos",
            "Boa tarde! Quanto custa o lote de 300 m² e quais são as condições de pagamento?"), ct);

        logger.LogInformation("Atendimento (Fase 3): canal simulado e conversas de exemplo criados na organização 'demo'.");
    }

    private async Task SeedFreshAsync(string password, CancellationToken ct)
    {

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

        var leadIds = new Dictionary<string, Guid>();
        foreach (var s in samples)
            leadIds[s.Name] = await leads.CreateAsync(new CreateLeadCommand(s.Name, s.Phone, null, s.Source, "Lançamento 2026", s.Interest, demo.StageIds[s.Stage], s.Owner), ct);

        await SeedRoutineAsync(scope.ServiceProvider, demo, leadIds, ct);

        logger.LogInformation("Dados de desenvolvimento criados (organizações 'demo' e 'outra').");
    }

    private static readonly string[] RoutineLeadNames = ["Mariana Souza", "Paulo Lima", "Ricardo Gomes", "Beatriz Costa"];
    private static readonly string[] RoutineUserEmails = ["gestor@demo.local", "corretor1@demo.local", "corretor2@demo.local"];

    /// <summary>Bancos de desenvolvimento criados antes da Fase 2 recebem os dados de rotina uma única vez.</summary>
    private async Task SeedRoutineForExistingDemoAsync(CancellationToken ct)
    {
        var demoOrg = await db.Organizations.FirstOrDefaultAsync(o => o.Slug == "demo", ct);
        if (demoOrg is null || await db.AgentSchedules.IgnoreQueryFilters().AnyAsync(s => s.OrganizationId == demoOrg.Id, ct))
            return;

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().InitializeSystem(demoOrg.Id,
            Capabilities.All.ToDictionary(c => c, _ => CapabilityScope.Org));
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var users = await (from m in scopedDb.Memberships
                           join u in scopedDb.Users on m.UserId equals u.Id
                           select new { u.Email, u.Id }).ToDictionaryAsync(x => x.Email, x => x.Id, ct);
        var leads = await (from l in scopedDb.Leads
                           join c in scopedDb.Contacts on l.ContactId equals c.Id
                           select new { c.FullName, l.Id }).ToListAsync(ct);
        var leadIds = leads.GroupBy(l => l.FullName).ToDictionary(g => g.Key, g => g.First().Id);

        var demo = new ProvisionedOrganization(demoOrg.Id, users, Guid.Empty, []);
        if (RoutineLeadNames.All(leadIds.ContainsKey) && RoutineUserEmails.All(users.ContainsKey))
        {
            await SeedRoutineAsync(scope.ServiceProvider, demo, leadIds, ct);
            logger.LogInformation("Dados de rotina (Fase 2) adicionados à organização 'demo'.");
        }
    }

    /// <summary>Fase 2: jornadas, macros, tarefas e uma visita de exemplo.</summary>
    private static async Task SeedRoutineAsync(IServiceProvider sp, ProvisionedOrganization demo, Dictionary<string, Guid> leadIds, CancellationToken ct)
    {
        var scheduling = sp.GetRequiredService<SchedulingService>();
        var tasks = sp.GetRequiredService<TaskService>();
        var macros = sp.GetRequiredService<MacroService>();
        var clock = sp.GetRequiredService<TimeProvider>();

        var weekdays = Enumerable.Range(1, 5).SelectMany(d => new[]
        {
            new WeeklyInterval(d, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            new WeeklyInterval(d, new TimeOnly(13, 0), new TimeOnly(18, 0)),
        });
        var withSaturday = weekdays.Append(new WeeklyInterval(6, new TimeOnly(8, 0), new TimeOnly(12, 0))).ToList();

        foreach (var email in new[] { "gestor@demo.local", "corretor1@demo.local", "corretor2@demo.local" })
            await scheduling.UpdateScheduleAsync(demo.UserIdsByEmail[email], new UpdateAgentScheduleCommand(withSaturday, 60, 30, 120, 6, false), ct);

        var shared = new (string Shortcut, string Title, string Body)[]
        {
            ("/apresentacao", "Apresentação", "Olá, {{lead.first_name}}! Meu nome é {{agent.name}}, da {{org.name}}. Vi seu interesse e vou te ajudar. Posso te explicar como funciona?"),
            ("/localizacao", "Localização", "{{lead.first_name}}, o empreendimento fica em uma região de fácil acesso. Quer que eu envie a localização no mapa?"),
            ("/documentacao", "Documentação", "Para avançarmos, vou precisar de: RG, CPF, comprovante de residência e comprovante de renda. Pode me enviar por aqui mesmo."),
            ("/agendar", "Agendar visita", "{{lead.first_name}}, que tal conhecer o local pessoalmente? Vou verificar os horários disponíveis e te confirmo."),
            ("/followup", "Follow-up", "Oi, {{lead.first_name}}! Passando para saber se ficou alguma dúvida. Estou à disposição."),
        };
        foreach (var m in shared)
            await macros.CreateAsync(new SaveMacroCommand(m.Shortcut, m.Title, m.Body, Shared: true, DevelopmentId: null), ct);

        var now = clock.GetUtcNow();
        await tasks.CreateAsync(new CreateTaskCommand(null, leadIds["Mariana Souza"], TaskKind.FollowUp, "Retornar sobre condições de pagamento", null, now.AddHours(-20)), ct);
        await tasks.CreateAsync(new CreateTaskCommand(null, leadIds["Paulo Lima"], TaskKind.Call, "Ligar para entender prazo de compra", null, now.AddHours(2)), ct);
        await tasks.CreateAsync(new CreateTaskCommand(null, leadIds["Ricardo Gomes"], TaskKind.FollowUp, "Enviar tabela atualizada", null, now.AddDays(2)), ct);

        // Primeira janela livre REAL do corretor (nunca um horário inventado).
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var availability = await scheduling.GetAvailabilityAsync(demo.UserIdsByEmail["corretor1@demo.local"], today.AddDays(1), today.AddDays(8), null, ct);
        if (availability.Slots.Count > 0 && availability.Slots[0] is { } slot)
        {
            await scheduling.CreateAppointmentAsync(new CreateAppointmentCommand(null, leadIds["Beatriz Costa"], AppointmentKind.Visit, null, slot.Start, slot.End,
                "Stand de vendas — Residencial Primavera", null, ConfirmWarnings: false), ct);
        }
    }
}

public static class DatabaseStartup
{
    /// <summary>
    /// Inicialização do banco em qualquer ambiente:
    ///  - migrations: sempre em Development; em outros ambientes só com Database:MigrateOnStartup=true;
    ///  - dados FICTÍCIOS de demonstração: Development, ou Seed:DemoData=true (servidor de demonstração);
    ///  - bootstrap de produção: cria a organização e o primeiro administrador quando o banco está vazio
    ///    e Bootstrap:* está configurado (sem dados de exemplo).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var configuration = app.Configuration;
        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<DevelopmentSeeder>>();
        var db = sp.GetRequiredService<AppDbContext>();

        if (app.Environment.IsDevelopment() || configuration.GetValue("Database:MigrateOnStartup", false))
            await db.Database.MigrateAsync();

        var demoData = app.Environment.IsDevelopment() || configuration.GetValue("Seed:DemoData", false);
        var seedPassword = configuration["Seed:Password"];
        if (demoData)
        {
            if (string.IsNullOrWhiteSpace(seedPassword))
                logger.LogWarning("Seed:Password não configurada — dados de demonstração não serão criados.");
            else
                await sp.GetRequiredService<DevelopmentSeeder>().SeedAsync(seedPassword, CancellationToken.None);
        }

        await BootstrapAsync(sp, configuration, logger);
    }

    private static async Task BootstrapAsync(IServiceProvider sp, IConfiguration configuration, ILogger logger)
    {
        var section = configuration.GetSection("Bootstrap");
        var adminEmail = section["AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail))
            return;

        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.Organizations.AnyAsync())
            return;

        var password = section["AdminPassword"] ?? "";
        if (password.Length < 12)
            throw new InvalidOperationException("Bootstrap:AdminPassword deve ter pelo menos 12 caracteres.");

        await sp.GetRequiredService<OrganizationProvisioning>().ProvisionAsync(
            section["OrganizationName"] ?? "Minha Loteadora",
            section["OrganizationSlug"] ?? "principal",
            [new ProvisionedUser(adminEmail, section["AdminName"] ?? "Administrador", SystemRoles.Admin, null)],
            password,
            CancellationToken.None);

        logger.LogInformation("Organização inicial criada com o administrador configurado em Bootstrap:AdminEmail.");
    }
}
