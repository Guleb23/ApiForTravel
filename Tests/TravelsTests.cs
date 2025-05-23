﻿using ApiForTravel.Db;
using ApiForTravel.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Tests
{
    public class TravelsTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly ApplicationDBContext _dbContext;
        private int _testUserId;
        private readonly ITestOutputHelper _output;
        public TravelsTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
        {
            _client = factory.CreateClient();
            _output = output;
            // Создаем отдельный scope для контекста
            var scope = factory.Services.CreateScope();
            _dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDBContext>();
        }

        public async Task InitializeAsync()
        {
            _testUserId = await CreateTestUserAsync();
        }

        public async Task DisposeAsync()
        {
            await _dbContext.Database.EnsureDeletedAsync();
            _dbContext.Dispose();
        }

        private async Task<int> CreateTestUserAsync()
        {
            var user = new UserModel
            {
                Email = "test@example.com",
                Username = "testuser",
                Password = "hashed_password",
                RefreshToken = Guid.NewGuid().ToString(),
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1)
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            return user.Id;
        }
        // Проверяет успешное создание маршрута с валидными данными.
        [Fact]
        public async Task CreateTravel_ValidRequest_ReturnsTravel()
        {
            // Arrange
            var request = new
            {
                Title = "Test Travel",
                Date = DateTime.UtcNow.ToString("o"),
                Points = new[] {
                new
                {
                    Name = "Point 1",
                    Address = "Address 1",
                    Coordinates = new { Lat = 55.7558, Lon = 37.6176 },
                    Type = "attraction",
                    DepartureTime = "10:00:00",
                    note = "testNote"
                }
            }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/users/{_testUserId}/travels", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var travel = await response.Content.ReadFromJsonAsync<TravelModel>();
            travel.Should().NotBeNull();
            travel.Title.Should().Be(request.Title);

            // Проверка, что точка была добавлена
            travel.Points.Should().HaveCount(1);
            travel.Points[0].Name.Should().Be("Point 1");
        }
        // Проверяет, что создание маршрута для несуществующего пользователя возвращает 404.
        [Fact]
        public async Task CreateTravel_UserNotFound_ReturnsNotFound()
        {
            // Arrange
            var nonExistentUserId = _testUserId + 999; // ID пользователя, которого нет в БД
            var request = new
            {
                Title = "Test Travel",
                Date = DateTime.UtcNow.ToString("o"),
                Points = new[] {
                new
                {
                    Name = "Point 1",
                    Address = "Address 1",
                    Coordinates = new { Lat = 55.7558, Lon = 37.6176 },
                    Type = "attraction",
                    DepartureTime = "10:00:00",
                    note = "testNote"
                }
            }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/users/{nonExistentUserId}/travels", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("User with id");
        }
        // Проверяет, что попытка создать маршрут без точек возвращает ошибку 400.
        [Fact]
        public async Task CreateTravel_NoPoints_ReturnsBadRequest()
        {
            // Arrange
            var request = new
            {
                Title = "Test Travel",
                Date = DateTime.UtcNow.ToString("o"),
                Points = new object[] { } // Нет точек маршрута
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/users/{_testUserId}/travels", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("At least one point is required");
        }
        // Проверяет, что при некорректном формате времени отправления возвращается ошибка 400.
        [Fact]
        public async Task CreateTravel_InvalidDepartureTime_ReturnsBadRequest()
        {
            // Arrange
            var request = new
            {
                Title = "Test Travel",
                Date = DateTime.UtcNow.ToString("o"),
                Points = new[] {
                new
                {
                    Name = "Point 1",
                    Address = "Address 1",
                    Coordinates = new { Lat = 55.7558, Lon = 37.6176 },
                    Type = "attraction",
                    DepartureTime = "invalid-time", // Неверный формат времени
                    note = "testNote"
                }
            }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/users/{_testUserId}/travels", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("Invalid departure time format");
        }
        // Проверяет, что можно получить список маршрутов пользователя по его ID.
        [Fact]
        public async Task GetRoutes_ByUserId_ReturnsRoutes()
        {
            // Arrange: добавим тестовый маршрут вручную
            var travel = new TravelModel
            {
                Title = "Sample Travel",
                Date = DateTime.UtcNow,
                UserId = _testUserId,
                Points = new List<TravelPoint>
        {
            new TravelPoint
            {
                Name = "Test Point",
                Address = "Test Address",
                Type = "attraction",
                Coordinates = new Coordinates { Lat = 10.1234, Lon = 20.5678 },
                Photos = new List<Photo>
                {
                    new Photo { FilePath = "uploads/test.jpg" }
                }
            }
        }
            };

            _dbContext.Travels.Add(travel);
            await _dbContext.SaveChangesAsync();

            // Act
            var response = await _client.GetAsync($"/api/routes/{_testUserId}");

            // Assert
            await AssertSuccessResponse(response);
            var json = await response.Content.ReadFromJsonAsync<JsonArray>();
            json.Should().NotBeNullOrEmpty();
            json[0]["Title"]!.ToString().Should().Be("Sample Travel");
        }
        // Проверяет, что можно получить маршрут по его ID.
        [Fact]
        public async Task GetTravel_ById_ReturnsTravel()
        {
            var travel = new TravelModel
            {
                Title = "Specific Travel",
                Date = DateTime.UtcNow,
                UserId = _testUserId
            };
            _dbContext.Travels.Add(travel);
            await _dbContext.SaveChangesAsync();

            // Act
            var response = await _client.GetAsync($"/api/travel/{travel.Id}");

            // Assert
            await AssertSuccessResponse(response);
            var json = await response.Content.ReadFromJsonAsync<JsonArray>();
            json.Should().ContainSingle();
            json[0]["Title"]!.ToString().Should().Be("Specific Travel");
        }
        // Проверяет, что можно получить точки маршрута по ID маршрута.
        [Fact]
        public async Task GetPoints_ByTravelId_ReturnsPoints()
        {
            var travel = new TravelModel
            {
                Title = "With Points",
                Date = DateTime.UtcNow,
                UserId = _testUserId,
                Points = new List<TravelPoint>
        {
            new TravelPoint
            {
                Name = "Point A",
                Address = "Somewhere",
                Coordinates = new Coordinates { Lat = 1.1, Lon = 2.2 },
                Type = "attraction",
                Photos = new List<Photo>()
            }
        }
            };
            _dbContext.Travels.Add(travel);
            await _dbContext.SaveChangesAsync();

            // Act
            var response = await _client.GetAsync($"/api/points/{travel.Id}");

            // Assert
            await AssertSuccessResponse(response);
            var points = await response.Content.ReadFromJsonAsync<JsonArray>();
            points.Should().ContainSingle();
            points[0]["Name"]!.ToString().Should().Be("Point A");
        }

        [Fact]
        public async Task CreateTravel_WithPhoto_SavesPhoto()
        {
            // Arrange
            var jpegBytes = new byte[]
            {
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xD9
            };
            var photoBytes = Convert.ToBase64String(jpegBytes);
            var base64Photo = $"data:image/jpeg;base64,{photoBytes}";

            var request = new
            {
                Title = "Travel with Photo",
                Date = DateTime.UtcNow.ToString("o"),
                Points = new[]
                {
            new
            {
                Name = "Photo Point",
                Address = "With Image",
                Coordinates = new { Lat = 50.0, Lon = 50.0 },
                Type = "attraction",
                DepartureTime = "12:00:00",
                note = "testNote",
                Photos = new[]
                {
                    new
                    {
                        FileName = "test.jpg",
                        Base64Content = base64Photo
                    }
                }
            }
        }
            };

            // Act
            var response = await _client.PostAsJsonAsync($"/api/users/{_testUserId}/travels", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var travel = await response.Content.ReadFromJsonAsync<TravelModel>();
            travel.Should().NotBeNull();
            travel.Points.Should().ContainSingle();

            var point = travel.Points.First();
            point.Photos.Should().ContainSingle();

            var savedPhoto = point.Photos.First();

            savedPhoto.FilePath.Should().MatchRegex(@"^uploads[\\/].+");

            // Построение полного пути к файлу
            var currentDir = Directory.GetCurrentDirectory();
            _output.WriteLine($"Current Directory: {currentDir}");

            // Поднимаемся 4 уровня вверх, чтобы выйти из bin\Debug\net8.0 к корню решения
            var solutionRoot = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", ".."));
            _output.WriteLine($"Solution Root: {solutionRoot}");

            // Путь к папке ApiForTravel в корне решения
            var apiProjectPath = Path.Combine(solutionRoot, "ApiForTravel");
            _output.WriteLine($"API Project Path: {apiProjectPath}");

            // Нормализуем путь файла
            var normalizedFilePath = savedPhoto.FilePath.Replace("/", Path.DirectorySeparatorChar.ToString())
                                                       .Replace("\\", Path.DirectorySeparatorChar.ToString());

            // Итоговый путь к сохранённому файлу
            var savedPath = Path.Combine(apiProjectPath, normalizedFilePath);
            _output.WriteLine($"Saved Photo Path: {savedPath}");

            // Проверяем наличие файла
            File.Exists(savedPath).Should().BeTrue();
        }


        // Проверяет, что HTTP-ответ успешен, иначе выбрасывает исключение с сообщением об ошибке.
        private async Task AssertSuccessResponse(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Request failed: {response.StatusCode}\n{errorContent}");
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}