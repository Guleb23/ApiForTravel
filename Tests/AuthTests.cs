using System.Net.Http.Json;
using System.Net;
using FluentAssertions;
using System.Text.Json.Nodes;
using ApiForTravel.Db;
using Microsoft.Extensions.DependencyInjection;

namespace Tests
{
    public class AuthTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly ApplicationDBContext _dbContext;

        public AuthTests(CustomWebApplicationFactory factory)
        {
            _client = factory.CreateClient();
            var scope = factory.Services.CreateScope();
            _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            await _dbContext.Database.EnsureDeletedAsync();
            _dbContext.Dispose();
        }
        // Проверяет успешную регистрацию нового пользователя и получение access-токена.
        [Fact]
        public async Task Register_ValidUser_ReturnsOk()
        {
            // Arrange
            var request = new
            {
                Email = "test@example.com",
                Password = "Test123!",
                Username = "testuser"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/register", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var data = await response.Content.ReadFromJsonAsync<JsonObject>();
            data.Should().NotBeNull();
            data["AccessToken"].Should().NotBeNull();
            data["Email"]!.ToString().Should().Be(request.Email);
        }
        // Проверяет, что повторная регистрация пользователя с теми же данными возвращает 409 Conflict.
        [Fact]
        public async Task Register_ExistingUser_ReturnsConflict()
        {
            // Arrange — сначала регистрируем пользователя
            var request = new
            {
                Email = "existing@example.com",
                Password = "Password123!",
                Username = "existinguser"
            };
            await _client.PostAsJsonAsync("/api/register", request);

            // Act — повторная регистрация
            var response = await _client.PostAsJsonAsync("/api/register", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        // Проверяет успешный вход пользователя с корректными учетными данными.
        [Fact]
        public async Task Login_ValidCredentials_ReturnsTokens()
        {
            // Убедимся, что база создана
            await _dbContext.Database.EnsureCreatedAsync();

            // Arrange — сначала регистрируем пользователя
            var registerRequest = new
            {
                Email = "login@example.com",
                Password = "Test12312",
                Username = "loginuser"
            };

            var registerResponse = await _client.PostAsJsonAsync("/api/register", registerRequest);

            // Проверяем, что регистрация прошла успешно
            registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var registerContent = await registerResponse.Content.ReadFromJsonAsync<JsonObject>();
            registerContent.Should().NotBeNull();
            registerContent["AccessToken"].Should().NotBeNull();

            // Подождать немного, если база async (например, SQLite in-memory может лагать)
            await _dbContext.SaveChangesAsync(); // Гарантируем сохранение
            await Task.Delay(50); // небольшой запас времени

            var loginRequest = new
            {
                Email = "login@example.com",
                Password = "Test12312"
            };

            // Act — логинимся
            var response = await _client.PostAsJsonAsync("/api/login", loginRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadFromJsonAsync<JsonObject>();
            content.Should().NotBeNull();
            content["AccessToken"]?.GetValue<string>().Should().NotBeNullOrEmpty();
            content["Email"]?.GetValue<string>().Should().Be("login@example.com");
            content["Username"]?.GetValue<string>().Should().Be("loginuser");
        }

        // Проверяет, что вход с неверным паролем возвращает 409 Conflict.
        [Fact]
        public async Task Login_WrongPassword_ReturnsConflict()
        {
            // Arrange — регистрируем пользователя
            var registerRequest = new
            {
                Email = "wrongpass@example.com",
                Password = "RightPass123!",
                Username = "wrongpassuser"
            };
            await _client.PostAsJsonAsync("/api/register", registerRequest);

            // Act — пытаемся войти с неправильным паролем
            var loginRequest = new
            {
                Email = "wrongpass@example.com",
                Password = "WrongPassword!"
            };

            var response = await _client.PostAsJsonAsync("/api/login", loginRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }


        // Проверяет, что попытка входа с несуществующим email возвращает 404 Not Found.
        [Fact]
        public async Task Login_NonExistingUser_ReturnsNotFound()
        {
            // Arrange
            var loginRequest = new
            {
                Email = "nonexistent@example.com",
                Password = "DoesntMatter123!"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/login", loginRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        //система возвращает ошибку 400 Bad Request, если email не соответствует правильному формату
        [Fact]
        public async Task Register_InvalidEmail_ReturnsBadRequest()
        {
            // Arrange — попытка зарегистрировать пользователя с некорректным email
            var request = new
            {
                Email = "invalid-email",  // Некорректный email
                Password = "Test123!",
                Username = "invaliduser"
            };

            // Act — отправляем запрос на регистрацию
            var response = await _client.PostAsJsonAsync("/api/register", request);

            // Assert — проверяем, что статус код BadRequest
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // Получаем содержимое ответа как строку (предполагаем, что сервер вернет сообщение об ошибке в виде строки)
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("Неверный формат email."); // Предположим, что сообщение об ошибке будет содержать эту строку
        }
    }
}
