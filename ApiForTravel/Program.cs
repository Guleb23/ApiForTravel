
using ApiForTravel.Db;
using ApiForTravel.Helper;
using ApiForTravel.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;

namespace ApiForTravel
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
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



            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder.WithOrigins("https://guleb23-travelapp-25da.twc1.net", "http://localhost:5173")// Разрешить запросы с любого домена
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


            builder.Services.AddScoped<TokenService>();
            builder.Services.AddScoped<PasswordHasher>();
            builder.Services.AddScoped<AuthService>();

            var app = builder.Build();
            app.Urls.Add("http://*:5000");
            app.UseCors("AllowAllOrigins");

            // Используем Swagger для удобной отладки и автоматической документации эндпоинтов
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();



            app.MapGet("/api/users", (ApplicationDBContext ctx) =>
            {
                return ctx.Users.ToList();
            });


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
                    RefreshToken = "0"
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
                    ["AccessToken"] = accessToken,
                    ["Email"] = newUser.Email
                };

                return Results.Ok(JsonObject);
            });
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
                            ["AccessToken"] = accessToken,
                            ["Email"] = storedUser.Email
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
                    AccessToken = newAccessToken,
                    Email = storedUser.Email
                };
                return Results.Ok(test);
            });
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
                            ["RefreshToken"] = user.RefreshToken,
                            ["Email"] = user.Email
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

            app.Run();
        }
    }
}
