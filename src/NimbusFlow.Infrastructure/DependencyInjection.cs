using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Infrastructure.Messaging;
using NimbusFlow.Infrastructure.Persistence;

namespace NimbusFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNimbusFlowInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NimbusFlow");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:NimbusFlow is required.");
        }

        services.AddDbContext<NimbusFlowDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

        services.AddScoped<JobStore>();
        services.AddScoped<IJobStore>(provider => provider.GetRequiredService<JobStore>());
        services.AddScoped<IOutboxStore>(provider => provider.GetRequiredService<JobStore>());

        services.AddOptions<ServiceBusSettings>()
            .Bind(configuration.GetSection(ServiceBusSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.QueueName), "ServiceBus:QueueName is required.")
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceBusSettings>>().Value;
            if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                return new ServiceBusClient(settings.ConnectionString);
            }

            if (!string.IsNullOrWhiteSpace(settings.FullyQualifiedNamespace))
            {
                var credentialOptions = new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = settings.ManagedIdentityClientId
                };
                return new ServiceBusClient(settings.FullyQualifiedNamespace, new DefaultAzureCredential(credentialOptions));
            }

            throw new InvalidOperationException(
                "Configure ServiceBus:ConnectionString for local development or ServiceBus:FullyQualifiedNamespace for Azure managed identity.");
        });

        services.AddScoped<IQueuePublisher, ServiceBusQueuePublisher>();
        return services;
    }

    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services)
    {
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }

    public static IServiceCollection AddDatabaseMigration(this IServiceCollection services)
    {
        services.AddHostedService<DatabaseMigrationService>();
        return services;
    }

    public static IServiceCollection AddServiceBusJobReceiver(this IServiceCollection services)
    {
        services.AddHostedService<ServiceBusJobReceiver>();
        return services;
    }
}
