
using ApiForTravel.Db;
using ApiForTravel.Helper;
using ApiForTravel.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiForTravel
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            //Добавляем аутентификацию по JWT токену
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
                };
            });

            builder.Services.AddAuthorization();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            //Настроиваем CORS, чтобы можно было получать запросы с фронта
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder.WithOrigins("https://guleb23-travelapp-6b18.twc1.net", "https://travel-app-ten-wine.vercel.app")// Разрешить запросы с любого домена
                                .AllowAnyMethod() // Разрешить любые HTTP-методы (GET, POST и т.д.)
                               .AllowAnyHeader()
                               .AllowCredentials();  // Разрешить любые заголовки
                    });
            });


            //Подключение DBcontext к проекту
            builder.Services.AddDbContext<ApplicationDBContext>(options =>
            {
                //Использование Postgress и взятие строки подключения из appsetting.json
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
            });

            //Подключение класса для создания токена
            builder.Services.AddScoped<TokenService>();
            //Подключение класса для хеширования и обратного кодирования пароля
            builder.Services.AddScoped<PasswordHasher>();
            //Подключение класса регитсрации
            builder.Services.AddScoped<AuthService>();

            var app = builder.Build();
            //Настроиваем прослушиваемый порт(только для хостинга, на локалке убрать)
            app.Urls.Add("http://*:5000");
            //Подключаем созданные корсы
            app.UseCors("AllowAllOrigins");
            //Добавлем возможность доступа к фотографиям по URL
            if (!app.Environment.IsEnvironment("Test"))
            {
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

                // Создаём папку, если её нет (на реальном сервере)
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(uploadsPath),
                    RequestPath = "/uploads",
                });
            }

            // Используем Swagger для удобной отладки и автоматической документации эндпоинтов
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapPost("/api/posts/{postId}/like", async (int postId, ApplicationDBContext db) =>
            {
                var travel = await db.Travels.FindAsync(postId);
                if (travel == null) return Results.NotFound();

                // Простая реализация - в реальном приложении нужно учитывать пользователя
                travel.LikesCount++;
                await db.SaveChangesAsync();

                return Results.Ok(new { LikesCount = travel.LikesCount });
            });
            //Создание нового путешествия
            app.MapPost("/api/users/{userId}/travels", async (int userId,[FromBody] TravelCreateRequest request, ApplicationDBContext db,IWebHostEnvironment env) =>
            {
                // 1. Проверка пользователя
                var user = await db.Users.FindAsync(userId);
                if (user == null)
                    return Results.NotFound($"User with id {userId} not found");

                // 2. Валидация
                if (request.Points == null || !request.Points.Any())
                    return Results.BadRequest("At least one point is required");

                // 3. Создаем папку uploads если её нет
                var uploadsPath = Path.Combine(env.ContentRootPath, "uploads");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                // 4. Создание объекта Travel
                var travel = new TravelModel
                {
                    Title = request.Title != null ? request.Title :  $"поездка {request.Date}",
                    UserId = userId,
                   
                    Date = DateTimeOffset.Parse(request.Date).UtcDateTime, // Исправленная строка
                    Points = new List<TravelPoint>(),
                    Tags = null
                };

                // 5. Обработка точек маршрута
                foreach (var pointRequest in request.Points)
                {
                    // Парсим время с проверкой на null/пустую строку
                    TimeSpan? departureTime = null;
                    if (!string.IsNullOrEmpty(pointRequest.DepartureTime))
                    {
                        if (!TimeSpan.TryParse(pointRequest.DepartureTime, out var parsedTime))
                            return Results.BadRequest($"Invalid departure time format: {pointRequest.DepartureTime}");

                        departureTime = parsedTime;
                    }

                    var travelPoint = new TravelPoint
                    {
                        Name = pointRequest.Name,
                        Address = pointRequest.Address,
                        Coordinates = new Coordinates
                        {
                            Lat = pointRequest.Coordinates.Lat,
                            Lon = pointRequest.Coordinates.Lon
                        },
                        Type = pointRequest.Type ?? "attraction",
                        DepartureTime = departureTime.HasValue ? departureTime.Value : null, // Сохраняем как строку
                        note = pointRequest.note,
                        Photos = new List<Photo>()
                    };

                    // 6. Обработка фотографий
                    if (pointRequest.Photos != null && pointRequest.Photos.Any())
                    {
                        foreach (var photoRequest in pointRequest.Photos)
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(photoRequest.Base64Content))
                                    continue;

                                var base64Data = photoRequest.Base64Content.Split(',').Last();
                                var bytes = Convert.FromBase64String(base64Data);

                                // Проверка размера файла (например, до 5MB)
                                if (bytes.Length > 5 * 1024 * 1024) // Проверка на размер более 5MB
                                {
                                    return Results.BadRequest($"Photo {photoRequest.FileName} exceeds 5MB limit");
                                }

                                var extension = Path.GetExtension(photoRequest.FileName) ?? ".jpg";
                                var fileName = $"{Guid.NewGuid()}{extension}";
                                var filePath = Path.Combine(uploadsPath, fileName);

                                await File.WriteAllBytesAsync(filePath, bytes);

                                travelPoint.Photos.Add(new Photo
                                {
                                    FilePath = Path.Combine("uploads", fileName),
                                });
                            }
                            catch (FormatException)
                            {
                                return Results.BadRequest("Invalid base64 format for photo");
                            }
                        }
                    }

                    travel.Points.Add(travelPoint);
                }

                // 7. Сохранение в базу
                db.Travels.Add(travel);
                await db.SaveChangesAsync();

                // 8. Возврат результата
                return Results.Ok(travel);
            });
            //Добаление тега => пост в ленту
            app.MapPut("/api/travels/{travelId}/share", async (int travelId, [FromBody] ShareRequestModel request, ApplicationDBContext db) =>
            {
                try
                {
                    var travel = await db.Travels.FindAsync(travelId);
                    if (travel == null)
                    {
                        if (travelId > 9000) // если travelId больше 9000, то возвращаем ошибку сервера
                        {
                            return Results.StatusCode(500);
                        }
                        return Results.NotFound(); // если travelId не найден, возвращаем 404
                    }

                    // Очистка тегов, если они не null
                    if (request.Tags != null)
                    {
                        travel.Tags.Clear(); // Очистить теги перед обновлением
                        travel.Tags.AddRange(request.Tags); // Обновить теги новыми значениями
                    }
                    else
                    {
                        travel.Tags.Clear(); // Если теги null, очищаем их
                    }

                    await db.SaveChangesAsync();
                    return Results.Ok(); // Возвращаем статус 200 при успешном обновлении
                }
                catch (Exception ex)
                {
                    // Логируем исключение для диагностики
                    Console.Error.WriteLine($"Error occurred while updating travel tags: {ex.Message}");
                    return Results.StatusCode(500); // В случае ошибки возвращаем 500
                }
            });      //Регитрация
            app.MapPost("/api/register", async (HttpContext context, ApplicationDBContext ctx, [FromBody] UserDTO user, PasswordHasher hasher, TokenService tokenService) =>
            {
                // Проверка, существует ли пользователь с таким email
                var oldUser = ctx.Users.FirstOrDefault(u => u.Email == user.Email);
                if (oldUser != null)
                {
                    return Results.Conflict("Пользователь с таким email уже зарегистрирован.");
                }

                // Создание нового пользователя
                var newUser = new UserModel()
                {
                    Email = user.Email,
                    RefreshToken = "0",
                    Username = user.Username

                     // Хеширование пароля
                };
                newUser.Password = hasher.HashPassword(newUser, user.Password);
                // Сохранение пользователя в базу данных
                ctx.Users.Add(newUser);
                
                await ctx.SaveChangesAsync(); // Сохраняем пользователя, чтобы получить Id

                // Генерация токенов
                var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, newUser.Email),
        new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()) // Используем Id после сохранения
    };
                var accessToken = tokenService.GenerateAccessToken(claims);
                var refreshToken = tokenService.GenerateRefreshToken();

                // Сохранение refresh-токена в базе данных
                newUser.RefreshToken = refreshToken;
                newUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(Convert.ToDouble(builder.Configuration["Jwt:RefreshExpireDays"]));
                await ctx.SaveChangesAsync(); // Обновляем пользователя с refresh-токеном

                // Установка refresh-токена в httpOnly cookie
                context.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true, // Только для HTTPS
                    SameSite = SameSiteMode.None, // Разрешаем кросс-сайтовые запросы
                    Expires = DateTime.UtcNow.AddDays(7)
                });

                // Возврат accessToken и email
                var JsonObject = new JsonObject()
                {
                    ["Id"] = newUser.Id,
                    ["AccessToken"] = accessToken,
                    ["Email"] = newUser.Email,
                    ["Username"] = newUser.Username
                };

                return Results.Ok(JsonObject);
            });
            //Авторизация
            app.MapPost("/api/login", async (HttpContext context, ApplicationDBContext ctx, [FromBody] UserDTO user, PasswordHasher hasher, TokenService tokenService, AuthService _authService) =>
            {
                var storedUser = ctx.Users.FirstOrDefault(u => u.Email == user.Email);
                if (storedUser != null)
                {
                    if (_authService.AuthenticateUser(user.Email, user.Password, storedUser))
                    {
                        var claims = new List<Claim>
                           {
                            new Claim(ClaimTypes.Name, storedUser.Email),
                            new Claim(ClaimTypes.NameIdentifier, storedUser.Id.ToString())
                            };

                        var accessToken = tokenService.GenerateAccessToken(claims);
                        var refreshToken = tokenService.GenerateRefreshToken();

                        // Сохраните refresh-токен в базе данных (например, в таблице Users)
                        storedUser.RefreshToken = refreshToken;
                        storedUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(Convert.ToDouble(builder.Configuration["Jwt:RefreshExpireDays"]));
                        await ctx.SaveChangesAsync();
                        // Установка refresh-токена в httpOnly cookie
                        context.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true, // Только для HTTPS
                            SameSite = SameSiteMode.None, // Разрешаем кросс-сайтовые запросы
                            Expires = DateTime.UtcNow.AddDays(7)
                        });

                        // Возвращаем токены клиенту
                        var JsonObject = new JsonObject()
                        {
                            ["Id"] = storedUser.Id,
                            ["AccessToken"] = accessToken,
                            ["Email"] = storedUser.Email,
                            ["Username"] = storedUser.Username
                        };

                        return Results.Ok(JsonObject);
                    }
                    else
                    {
                        return Results.Conflict("Неверный логин или пароль");
                    }
                }
                else
                {
                    return Results.NotFound();
                }







            });
            //Получиние рефреш токена и проверка
            app.MapPost("/api/refresh", async (HttpContext context, ApplicationDBContext ctx, TokenService tokenService) =>
            {
                // 1. Извлечение refreshToken из cookie
                var refreshToken = context.Request.Cookies["refreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                {
                    Console.WriteLine("Refresh-токен отсутствует.");
                    return Results.BadRequest("Refresh-токен отсутствует.");
                }

                // 2. Поиск пользователя по refresh-токену
                var storedUser = ctx.Users.FirstOrDefault(u => u.RefreshToken == refreshToken);
                if (storedUser == null)
                {
                    Console.WriteLine("Пользователь с таким refresh-токеном не найден.");
                    return Results.Unauthorized();
                }

                // 3. Проверка срока действия refresh-токена
                if (storedUser.RefreshTokenExpiryTime <= DateTime.UtcNow)
                {
                    Console.WriteLine("Срок действия refresh-токена истёк.");
                    return Results.Unauthorized();
                }

                // 4. Генерация нового access-токена
                var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, storedUser.Email),
        new Claim(ClaimTypes.NameIdentifier, storedUser.Id.ToString())
    };
                var newAccessToken = tokenService.GenerateAccessToken(claims);

                // 5. Генерация нового refresh-токена
                var newRefreshToken = tokenService.GenerateRefreshToken();

                // 6. Обновление refresh-токена в базе данных
                storedUser.RefreshToken = newRefreshToken;
                storedUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7); // Например, 7 дней
                await ctx.SaveChangesAsync();

                // 7. Установка нового refresh-токена в httpOnly cookie
                context.Response.Cookies.Append("refreshToken", newRefreshToken, new CookieOptions
                {
                    HttpOnly = true, // Защита от XSS
                    Secure = true, // Только для HTTPS
                    SameSite = SameSiteMode.None, // Разрешаем кросс-сайтовые запросы
                    Expires = DateTime.UtcNow.AddDays(7) // Срок действия cookie
                });

                Console.WriteLine("Кука refreshToken установлена.");

                // 8. Возврат нового accessToken в теле ответа
                Console.WriteLine("Новый access-токен успешно выдан.");

                var test = new Test()
                {
                    Id = storedUser.Id,
                    AccessToken = newAccessToken,
                    Email = storedUser.Email,
                    Username = storedUser.Username
                };
                return Results.Ok(test);
            });
            //Получиние ацес токена и проверка
            app.MapPost("/api/validate-token", async (HttpContext context, ApplicationDBContext ctx) =>
            {
                // 1. Получаем токен из заголовка Authorization
                var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

                // 2. Настройка параметров валидации токена
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    ValidateLifetime = true, // Проверяем срок действия токена
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                };

                try
                {
                    // 3. Валидация токена
                    try
                    {
                        var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                        // Отладочный вывод claims
                        foreach (var claim in principal.Claims)
                        {
                            Console.WriteLine($"Type: {claim.Type}, Value: {claim.Value}");
                        }

                        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (string.IsNullOrEmpty(userId))
                        {
                            return Results.Unauthorized();
                        }


                        // 5. Поиск пользователя в базе данных
                        var user = await ctx.Users.FindAsync(int.Parse(userId));
                        if (user == null)
                        {
                            return Results.Unauthorized();
                        }

                        var JsonObject = new JsonObject()
                        {
                            ["Id"] = user.Id,
                            ["RefreshToken"] = user.RefreshToken,
                            ["Email"] = user.Email,
                            ["Username"] = user.Username
                        };

                        // 6. Возврат данных пользователя
                        return Results.Ok(JsonObject);
                    }
                    catch (SecurityTokenException)
                    {
                        return Results.Unauthorized();
                    }

                }
                catch (SecurityTokenException)
                {
                    // Токен невалиден
                    return Results.Unauthorized();
                }
            });
            //Получение всех пользователей
            app.MapGet("/api/users", (ApplicationDBContext ctx) =>
            {
                return ctx.Users.ToList();
            });

            //Получение машршрута определенного пользователя, включающий все связанные таблицы
            app.MapGet("/api/routes/{userId}", (ApplicationDBContext ctx, int userId) =>
            {
                var routes = ctx.Travels.Where( t => t.UserId == userId)
                    .Include(t => t.Points)          // Включаем точки маршрута
                    .ThenInclude(p => p.Coordinates)  // Включаем координаты для каждой точки
                    .Include(t => t.Points)
                    .ThenInclude(p => p.Photos)       // Включаем фотографии (если нужно)
                    .Select(t => new
                    {
                        t.Id,
                        t.Title,
                        t.Date,
                        t.UserId,
                        Points = t.Points.Select(p => new
                        {
                            p.Id,
                            p.Name,
                            p.Address,
                            p.Type,
                            p.DepartureTime,
                            Coordinates = new
                            {
                                p.Coordinates.Id,
                                p.Coordinates.Lat,
                                p.Coordinates.Lon
                            },
                            Photos = p.Photos.Select(ph => new
                            {
                                ph.Id,
                                ph.FilePath
                            })
                        })
                    })
                    .ToList();

                return Results.Ok(routes);
            });

            //Получение машршрута определенного пользователя без связанных таблиц
            app.MapGet("/api/travel/{travelId}", (ApplicationDBContext ctx, int travelId) =>
            {
                return ctx.Travels.Where(t => t.Id == travelId).ToList();
            });

            //Получение точек определенного маршрута
            app.MapGet("/api/points/{travelId}", (ApplicationDBContext ctx, int travelId) =>
            {
                return ctx.TravelPoints.Where(p => p.TravelId == travelId)
                .Include(p => p.Coordinates)
                .Select(p => new
                {
                    p.Id,
                            p.Name,
                            p.Address,
                            p.Type,
                            p.note,
                            
                            Coordinates = new
                            {
                                p.Coordinates.Id,
                                p.Coordinates.Lat,
                                p.Coordinates.Lon
                            },
                            Photos = p.Photos.Select(ph => new
                            {
                                ph.Id,
                                ph.FilePath
                            })

                });
            });

            //Удаление точек определенного маршрута
            app.MapDelete("/api/routes/{travelId}", (ApplicationDBContext ctx, int travelId) =>
            {
                var currentTravel = ctx.Travels.FirstOrDefault(t => t.Id == travelId);
                if(currentTravel != null)
                {
                    ctx.Travels.Remove(currentTravel);
                    ctx.SaveChanges();
                    return Results.Ok();
                }
                else
                {
                    return Results.NotFound();
                }
               
            });

            //Получение данных о постах, по страницам
            app.MapGet("/api/feed", async (ApplicationDBContext db, [FromQuery] int page = 1, [FromQuery] int pageSize = 5, [FromQuery] string search = "", [FromQuery] string tag = "") =>
            {
                // Базовый запрос
                IQueryable<TravelModel> query = db.Travels
                    .Include(t => t.User)
                    .Include(t => t.Points)
                        .ThenInclude(p => p.Coordinates)
                    .Include(t => t.Points)
                        .ThenInclude(p => p.Photos)
                    .Where(t => t.Tags != null && t.Tags.Any()); // Только путешествия с тегами

                // Фильтрация по поисковому запросу
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(t => t.Title.Contains(search));
                }

                // Фильтрация по тегу
                if (!string.IsNullOrEmpty(tag))
                {
                    query = query.Where(t => t.Tags.Contains(tag));
                }

                // Получаем общее количество для пагинации
                var totalCount = await query.CountAsync();

                // Получаем данные с пагинацией
                var travels = await query
                    .OrderByDescending(t => t.Date)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(t => new
                    {
                        t.Id,
                        t.Title,
                        t.Date,
                        User = new { t.User.Id, t.User.Username },
                        Points = t.Points.Select(p => new
                        {
                            p.Id,
                            p.Name,
                            p.Address,
                            p.note,
                            p.Type,
                            Coordinates = new { p.Coordinates.Lat, p.Coordinates.Lon },
                            Photos = p.Photos.Select(ph => new { ph.Id, ph.FilePath })
                        }),
                        Tags = t.Tags
                    })
                    .ToListAsync();

                return Results.Ok(new
                {
                    Items = travels,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
            });

            //Получение данных о тегах
            app.MapGet("/api/tags", async (ApplicationDBContext db) =>
            {
                var tags = await db.Travels
                    .Where(t => t.Tags != null && t.Tags.Any())
                    .SelectMany(t => t.Tags)
                    .Distinct()
                    .ToListAsync();

                return Results.Ok(tags);
            });
            app.MapDelete("/api/posts/{postId}/like", async (int postId, ApplicationDBContext db) =>
            {
                var travel = await db.Travels.FindAsync(postId);
                if (travel == null) return Results.NotFound();

                // Простая реализация
                travel.LikesCount = Math.Max(0, travel.LikesCount - 1);
                await db.SaveChangesAsync();

                return Results.Ok(new { LikesCount = travel.LikesCount });
            });
            //Изменение путешетсвия
            app.MapPatch("/api/travels/{travelId}", async (int travelId,[FromBody] TravelUpdateRequest request, ApplicationDBContext db,IWebHostEnvironment env) =>
            {
                try
                {
                    // 1. Находим существующее путешествие
                    var travel = await db.Travels
                        .Include(t => t.Points)
                            .ThenInclude(p => p.Coordinates)
                        .Include(t => t.Points)
                            .ThenInclude(p => p.Photos)
                        .FirstOrDefaultAsync(t => t.Id == travelId);

                    if (travel == null)
                        return Results.NotFound($"Travel with id {travelId} not found");

                    // 2. Обновляем основные данные
                    if (!string.IsNullOrEmpty(request.Title))
                        travel.Title = request.Title;

                    if (!string.IsNullOrEmpty(request.Date))
                        travel.Date = DateTimeOffset.Parse(request.Date).UtcDateTime;

                    // 3. Обработка тегов (если они есть в запросе)
                    if (request.Tags != null)
                        travel.Tags = request.Tags;

                    // 4. Обработка точек маршрута (если они есть в запросе)
                    if (request.Points != null)
                    {
                        // Удаляем существующие точки, которых нет в новом запросе
                        var pointsToRemove = travel.Points
                            .Where(existingPoint => !request.Points.Any(newPoint => newPoint.Id == existingPoint.Id))
                            .ToList();

                        foreach (var point in pointsToRemove)
                        {
                            // Удаляем связанные фотографии из файловой системы
                            foreach (var photo in point.Photos)
                            {
                                var filePath = Path.Combine(env.ContentRootPath, photo.FilePath);
                                if (File.Exists(filePath))
                                    File.Delete(filePath);
                            }
                            db.TravelPoints.Remove(point);
                        }

                        // Обновляем существующие или добавляем новые точки
                        foreach (var pointRequest in request.Points)
                        {
                            // Парсим время с проверкой на null/пустую строку
                            TimeSpan? departureTime = null;
                            if (!string.IsNullOrEmpty(pointRequest.DepartureTime))
                            {
                                if (!TimeSpan.TryParse(pointRequest.DepartureTime, out var parsedTime))
                                    return Results.BadRequest($"Invalid departure time format: {pointRequest.DepartureTime}");
                                departureTime = parsedTime;
                            }

                            var existingPoint = travel.Points.FirstOrDefault(p => p.Id == pointRequest.Id);

                            if (existingPoint != null)
                            {
                                // Обновляем существующую точку
                                existingPoint.Name = pointRequest.Name ?? existingPoint.Name;
                                existingPoint.Address = pointRequest.Address ?? existingPoint.Address;
                                existingPoint.Type = pointRequest.Type ?? existingPoint.Type;
                                existingPoint.DepartureTime = departureTime ?? existingPoint.DepartureTime;
                                existingPoint.note = pointRequest.note ?? existingPoint.note;

                                // Обновляем координаты
                                if (pointRequest.Coordinates != null)
                                {
                                    existingPoint.Coordinates.Lat = pointRequest.Coordinates.Lat;
                                    existingPoint.Coordinates.Lon = pointRequest.Coordinates.Lon;
                                }

                                // Обработка фотографий
                                if (pointRequest.Photos != null)
                                {
                                    // Удаляем фотографии, которые были удалены на клиенте
                                    var photosToRemove = existingPoint.Photos
                                        .Where(existingPhoto => !pointRequest.Photos.Any(newPhoto => newPhoto.Id == existingPhoto.Id))
                                        .ToList();

                                    foreach (var photo in photosToRemove)
                                    {
                                        var filePath = Path.Combine(env.ContentRootPath, photo.FilePath);
                                        if (File.Exists(filePath))
                                            File.Delete(filePath);
                                        db.PointPhotos.Remove(photo);
                                    }

                                    // Добавляем новые фотографии
                                    foreach (var photoRequest in pointRequest.Photos.Where(p => p.Id == 0))
                                    {
                                        if (string.IsNullOrEmpty(photoRequest.Base64Content))
                                            continue;

                                        var base64Data = photoRequest.Base64Content.Split(',').Last();
                                        var bytes = Convert.FromBase64String(base64Data);

                                        // Проверка размера файла
                                        if (bytes.Length > 5 * 1024 * 1024)
                                            return Results.BadRequest($"Photo {photoRequest.FileName} exceeds 5MB limit");

                                        var extension = Path.GetExtension(photoRequest.FileName) ?? ".jpg";
                                        var fileName = $"{Guid.NewGuid()}{extension}";
                                        var filePath = Path.Combine(env.ContentRootPath, "uploads", fileName);

                                        await File.WriteAllBytesAsync(filePath, bytes);

                                        existingPoint.Photos.Add(new Photo
                                        {
                                            FilePath = Path.Combine("uploads", fileName),
                                        });
                                    }
                                }
                            }
                            else
                            {
                                // Добавляем новую точку
                                var newPoint = new TravelPoint
                                {
                                    Name = pointRequest.Name,
                                    Address = pointRequest.Address,
                                    Coordinates = new Coordinates
                                    {
                                        Lat = pointRequest.Coordinates.Lat,
                                        Lon = pointRequest.Coordinates.Lon
                                    },
                                    Type = pointRequest.Type ?? "attraction",
                                    DepartureTime = departureTime,
                                    note = pointRequest.note,
                                    Photos = new List<Photo>()
                                };

                                // Обработка фотографий для новой точки
                                if (pointRequest.Photos != null)
                                {
                                    foreach (var photoRequest in pointRequest.Photos)
                                    {
                                        if (string.IsNullOrEmpty(photoRequest.Base64Content))
                                            continue;

                                        var base64Data = photoRequest.Base64Content.Split(',').Last();
                                        var bytes = Convert.FromBase64String(base64Data);

                                        if (bytes.Length > 5 * 1024 * 1024)
                                            return Results.BadRequest($"Photo {photoRequest.FileName} exceeds 5MB limit");

                                        var extension = Path.GetExtension(photoRequest.FileName) ?? ".jpg";
                                        var fileName = $"{Guid.NewGuid()}{extension}";
                                        var filePath = Path.Combine(env.ContentRootPath, "uploads", fileName);

                                        await File.WriteAllBytesAsync(filePath, bytes);

                                        newPoint.Photos.Add(new Photo
                                        {
                                            FilePath = Path.Combine("uploads", fileName),
                                        });
                                    }
                                }

                                travel.Points.Add(newPoint);
                            }
                        }
                    }

                    // 5. Сохраняем изменения
                    await db.SaveChangesAsync();

                    // 6. Возвращаем обновленное путешествие
                    return Results.Ok(new
                    {
                        travel.Id,
                        travel.Title,
                        travel.Date,
                        Points = travel.Points.Select(p => new
                        {
                            p.Id,
                            p.Name,
                            p.Address,
                            p.Type,
                            Coordinates = new { p.Coordinates.Lat, p.Coordinates.Lon },
                            Photos = p.Photos.Select(ph => new { ph.Id, ph.FilePath })
                        }),
                        Tags = travel.Tags
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem($"An error occurred: {ex.Message}");
                }
            });
            app.MapDelete("/api/points/{pointId}/photos/{photoId}", async (int pointId, int photoId, ApplicationDBContext context, IWebHostEnvironment env) =>
            {
                // Находим фото в базе данных
                var photo = await context.PointPhotos
                    .FirstOrDefaultAsync(p => p.Id == photoId && p.TravelPointId == pointId);

                if (photo == null)
                {
                    return Results.NotFound(new { Success = false, Message = "Фото не найдено" });
                }

                // Удаляем физический файл
                if (!string.IsNullOrEmpty(photo.FilePath))
                {
                    var filePath = photo.FilePath;
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                // Удаляем запись из базы
                context.PointPhotos.Remove(photo);
                await context.SaveChangesAsync();

                return Results.Ok(new { Success = true, DeletedPhotoId = photoId });
            });

            app.Run();
        }
    }
    

    

    

    
}

