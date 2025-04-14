using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using FluentAssertions;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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

        [Fact]
        public async Task Login_ValidCredentials_ReturnsTokens()
        {
            // Arrange
            var registerRequest = new
            {
                Email = "login@example.com",
                Password = "Test123!",
                Username = "loginuser"
            };

            await _client.PostAsJsonAsync("/api/register", registerRequest);

            var loginRequest = new
            {
                Email = "login@example.com",
                Password = "Test123!"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/login", loginRequest);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadFromJsonAsync<JsonObject>();
            content.Should().NotBeNull();
            content["AccessToken"]?.GetValue<string>().Should().NotBeNullOrEmpty();
            content["Email"]?.GetValue<string>().Should().Be("login@example.com");
            content["Username"]?.GetValue<string>().Should().Be("loginuser");

            // Debug (если нужно)
            // Console.WriteLine($"AccessToken: {content["AccessToken"]}");
        }

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
    }
}
