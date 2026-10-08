using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Pipeline.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Loteadora.Crm.Modules.Iam.Application;

public sealed record ProvisionedUser(string Email, string DisplayName, string RoleName, string? TeamName);

public sealed record ProvisionedOrganization(Guid OrganizationId, IReadOnlyDictionary<string, Guid> UserIdsByEmail, Guid DefaultPipelineId, IReadOnlyList<Guid> StageIds);

/// <summary>Cria uma organização com papéis padrão, times, usuários e pipeline inicial.</summary>
public sealed class OrganizationProvisioning(IServiceScopeFactory scopes)
{
    public static readonly (string Name, StageKind Kind, string Color)[] DefaultStages =
    [
        ("Novo lead", StageKind.Open, "#64748b"),
        ("Primeiro contato", StageKind.Open, "#0ea5e9"),
        ("Em atendimento", StageKind.Open, "#6366f1"),
        ("Qualificado", StageKind.Open, "#8b5cf6"),
        ("Visita", StageKind.Open, "#f59e0b"),
        ("Proposta", StageKind.Open, "#f97316"),
        ("Negociação", StageKind.Open, "#ec4899"),
        ("Venda", StageKind.Won, "#16a34a"),
        ("Perdido", StageKind.Lost, "#dc2626"),
    ];

    public async Task<ProvisionedOrganization> ProvisionAsync(string name, string slug, IReadOnlyList<ProvisionedUser> users, string initialPassword, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();

        var organization = new Organization(name, slug);
        sp.GetRequiredService<RequestContext>().InitializeSystem(organization.Id);
        db.Organizations.Add(organization);

        var roles = SystemRoles.CreateFor(organization.Id).ToDictionary(r => r.Name);
        db.Roles.AddRange(roles.Values);

        var teams = users.Where(u => u.TeamName is not null).Select(u => u.TeamName!).Distinct()
            .ToDictionary(t => t, t => new Team(organization.Id, t));
        db.Teams.AddRange(teams.Values);

        var userIds = new Dictionary<string, Guid>();
        foreach (var spec in users)
        {
            var user = new User(spec.Email, spec.DisplayName);
            user.SetPasswordHash(hasher.HashPassword(user, initialPassword));
            db.Users.Add(user);
            db.Memberships.Add(new Membership(organization.Id, user.Id, roles[spec.RoleName].Id, spec.TeamName is null ? null : teams[spec.TeamName].Id));
            userIds[user.Email] = user.Id;
        }

        db.CalendarSettings.Add(BusinessCalendar.Domain.CalendarSettings.CreateDefault(organization.Id));

        var pipeline = new SalesPipeline(organization.Id, "Funil comercial", isDefault: true);
        var stageIds = DefaultStages.Select(s => pipeline.AddStage(s.Name, s.Kind, s.Color).Id).ToList();
        db.Pipelines.Add(pipeline);

        await db.SaveChangesAsync(ct);
        return new ProvisionedOrganization(organization.Id, userIds, pipeline.Id, stageIds);
    }
}
