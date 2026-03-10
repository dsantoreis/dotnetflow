using DotnetFlow.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetFlow.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove everything EF/DbContext related
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true ||
                    d.ServiceType.FullName?.Contains("DbContext") == true ||
                    d.ImplementationType?.FullName?.Contains("EntityFrameworkCore") == true ||
                    d.ImplementationType?.FullName?.Contains("DbContext") == true)
                .ToList();
            foreach (var d in efDescriptors) services.Remove(d);

            // Remove health check registrations to avoid duplicates
            var healthDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("HealthCheck") == true)
                .ToList();
            foreach (var d in healthDescriptors) services.Remove(d);

            // Re-add with InMemory
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            services.AddHealthChecks()
                .AddDbContextCheck<AppDbContext>();
        });

        builder.UseEnvironment("Testing");
    }
}
