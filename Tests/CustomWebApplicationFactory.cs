using ApiForTravel;
using ApiForTravel.Db;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Удаляем все зарегистрированные контексты EF Core, если есть
            RemoveAllEfServices(services);

            // Регистрируем новый DbContext с уникальным именем базы
            services.AddDbContext<ApplicationDBContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            });
        });

        var host = base.CreateHost(builder);

        // Создаем базу данных до запуска приложения
        using (var scope = host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            context.Database.EnsureDeleted();  // удаляем старую (если осталась)
            context.Database.EnsureCreated();  // создаем новую
        }

        return host;
    }

    private void RemoveAllEfServices(IServiceCollection services)
    {
        var efServices = services
            .Where(s =>
                s.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                s.ImplementationType?.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true)
            .ToList();

        foreach (var service in efServices)
        {
            services.Remove(service);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
