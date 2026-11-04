using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

namespace BoxLine.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the database context and document tooling.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. Set it with dotnet user-secrets.");

        services.AddDbContext<BoxLineDbContext>(options => options.UseSqlServer(connectionString));

        QuestPDF.Settings.License = LicenseType.Community;

        return services;
    }
}
