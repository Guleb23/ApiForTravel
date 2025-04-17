using ApiForTravel.Db;
using ApiForTravel.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    public class ShareTravelTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly HttpClient _client;
        private readonly ApplicationDBContext _db;
        private int _testUserId;

        public ShareTravelTests(CustomWebApplicationFactory factory)
        {
            _client = factory.CreateClient();

            // Создаем отдельный scope для контекста
            var scope = factory.Services.CreateScope();
            _db = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
        }


        [Fact]
        public async Task ShareTravel_InvalidId_ReturnsNotFound()
        {
            var request = new { Tags = new[] { "test" } };

            var response = await _client.PutAsJsonAsync("/api/travels/9/share", request);

            response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ShareTravel_DbFailure_Returns500()
        {
            // Создадим данные, которые вызовут ошибку в базе данных (например, пытаемся обновить запись, которой нет).
            var request = new { Tags = new[] { "test" } };

            // Попробуем обновить несуществующее путешествие (в реальной ситуации может быть ошибка в DB).
            var response = await _client.PutAsJsonAsync("/api/travels/99999/share", request);

            // Ожидаем 500 ошибку, если на сервере возникает исключение.
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
        }
    }
}
