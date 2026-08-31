using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NimbusFlow.Infrastructure.Persistence;

namespace NimbusFlow.IntegrationTests;

public sealed class NimbusFlowApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var databaseName = $"nimbusflow-tests-{Guid.NewGuid():N}";
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:NimbusFlow",
            "Server=(localdb)\\mssqllocaldb;Database=NimbusFlowTests;Trusted_Connection=True;");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<DbContextOptions<NimbusFlowDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<NimbusFlowDbContext>>();
            services.AddDbContext<NimbusFlowDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
        });
    }
}
