using BoxLine.Core.Interfaces;
using BoxLine.Core.Reports;
using BoxLine.Infrastructure.Documents;
using BoxLine.Infrastructure.Persistence;
using BoxLine.Infrastructure.Reports;
using BoxLine.Infrastructure.Repositories;
using BoxLine.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

namespace BoxLine.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the database context, repositories, seeder and document tooling.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. Set it with dotnet user-secrets.");

        services.AddDbContext<BoxLineDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IPortRepository, PortRepository>();
        services.AddScoped<IContainerTypeRepository, ContainerTypeRepository>();
        services.AddScoped<IContainerRepository, ContainerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IMovementRepository, MovementRepository>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IVesselRepository, VesselRepository>();
        services.AddScoped<IVoyageRepository, VoyageRepository>();
        services.AddScoped<IDetentionTariffRepository, DetentionTariffRepository>();
        services.AddScoped<IDetentionSqlCalculator, SqlDetentionCalculator>();
        services.AddScoped<IUtilisationReport, SqlUtilisationReport>();
        services.AddSingleton<IBillOfLadingRenderer, QuestPdfBillOfLadingRenderer>();
        services.AddSingleton<IBookingSpreadsheetWriter, MiniExcelBookingSpreadsheetWriter>();
        services.AddScoped<DatabaseSeeder>();

        QuestPDF.Settings.License = LicenseType.Community;

        return services;
    }
}
