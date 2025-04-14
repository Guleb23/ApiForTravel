using ApiForTravel;
using ApiForTravel.Db;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // 1. Создаем тестовый хост с чистой конфигурацией
        var host = builder.ConfigureServices(services =>
        {
            // Удаляем ВСЕ сервисы Entity Framework
            RemoveAllEfServices(services);

            // Регистрируем чистый DbContext
            services.AddDbContext<ApplicationDBContext>(options =>
            {
                options.UseInMemoryDatabase("TestDB");
                options.EnableDetailedErrors();
                options.EnableSensitiveDataLogging();
            });
        }).Build();

        // 2. Инициализируем базу
        using (var scope = host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
            context.Database.EnsureCreated();
        }

        return host;
    }

    private void RemoveAllEfServices(IServiceCollection services)
    {
        var efServices = services
            .Where(s => s.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                       s.ImplementationType?.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true)
            .ToList();

        foreach (var service in efServices)
        {
            services.Remove(service);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Не требуется очистка, так как каждый тест получает новый хост
        base.Dispose(disposing);
    }
}