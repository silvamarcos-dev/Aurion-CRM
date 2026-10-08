using Loteadora.Crm.Modules.Audit;
using Loteadora.Crm.Modules.Crm.Application;
using Loteadora.Crm.Modules.Iam.Application;
using Loteadora.Crm.Modules.Iam.Domain;
using Loteadora.Crm.Modules.Outbox;
using Loteadora.Crm.Modules.Persistence;
using Loteadora.Crm.Modules.Pipeline.Application;
using Loteadora.Crm.SharedKernel.Security;
using Loteadora.Crm.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Loteadora.Crm.Modules;

public static class ModulesRegistration
{
    public static IServiceCollection AddCrmModules(this IServiceCollection services, IConfiguration configuration, bool runOutboxDispatcher = true)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "public"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<RequestContext>();
        services.AddScoped<ICurrentActor>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<ActorLoader>();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<AuthService>();
        services.AddScoped<DirectoryService>();
        services.AddSingleton<OrganizationProvisioning>();

        services.AddSingleton<ICorrelationIdAccessor, ActivityCorrelationIdAccessor>();
        services.AddScoped<IAuditTrail, AuditTrail>();

        services.AddScoped<LeadService>();
        services.AddScoped<PipelineService>();

        if (runOutboxDispatcher)
            services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
