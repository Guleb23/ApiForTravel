
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
                        builder.WithOrigins("https://guleb23-travelapp-25da.twc1.net", "http://localhost:5173")// ��������� ������� � ������ ������
                                .AllowAnyMethod() // ��������� ����� HTTP-������ (GET, POST � �.�.)
                               .AllowAnyHeader()
                               .AllowCredentials();  // ��������� ����� ���������
                    });
            });


            //����������� DBcontext � �������
            builder.Services.AddDbContext<ApplicationDBContext>(options =>
            {
                //������������� Postgress � ������ ������ ����������� �� appsetting.json
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
            });


            builder.Services.AddScoped<TokenService>();
            builder.Services.AddScoped<PasswordHasher>();
            builder.Services.AddScoped<AuthService>();

            var app = builder.Build();
            app.Urls.Add("http://*:5000");
            app.UseCors("AllowAllOrigins");

            // ���������� Swagger ��� ������� ������� � �������������� ������������ ����������
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
                // ��������, ���������� �� ������������ � ����� email
                var oldUser = ctx.Users.FirstOrDefault(u => u.Email == user.Email);
                if (oldUser != null)
                {
                    return Results.Conflict("������������ � ����� email ��� ���������������.");
                }

                // �������� ������ ������������
                var newUser = new UserModel()
                {
                    Email = user.Email,
                    RefreshToken = "0"
                     // ����������� ������
                };
                newUser.Password = hasher.HashPassword(newUser, user.Password);
                // ���������� ������������ � ���� ������
                ctx.Users.Add(newUser);
                
                await ctx.SaveChangesAsync(); // ��������� ������������, ����� �������� Id

                // ��������� �������
                var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, newUser.Email),
        new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()) // ���������� Id ����� ����������
    };
                var accessToken = tokenService.GenerateAccessToken(claims);
                var refreshToken = tokenService.GenerateRefreshToken();

                // ���������� refresh-������ � ���� ������
                newUser.RefreshToken = refreshToken;
                newUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(Convert.ToDouble(builder.Configuration["Jwt:RefreshExpireDays"]));
                await ctx.SaveChangesAsync(); // ��������� ������������ � refresh-�������

                // ��������� refresh-������ � httpOnly cookie
                context.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true, // ������ ��� HTTPS
                    SameSite = SameSiteMode.None, // ��������� �����-�������� �������
                    Expires = DateTime.UtcNow.AddDays(7)
                });

                // ������� accessToken � email
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

                        // ��������� refresh-����� � ���� ������ (��������, � ������� Users)
                        storedUser.RefreshToken = refreshToken;
                        storedUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(Convert.ToDouble(builder.Configuration["Jwt:RefreshExpireDays"]));
                        await ctx.SaveChangesAsync();
                        // ��������� refresh-������ � httpOnly cookie
                        context.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true, // ������ ��� HTTPS
                            SameSite = SameSiteMode.None, // ��������� �����-�������� �������
                            Expires = DateTime.UtcNow.AddDays(7)
                        });

                        // ���������� ������ �������
                        var JsonObject = new JsonObject()
                        {
                            ["AccessToken"] = accessToken,
                            ["Email"] = storedUser.Email
                        };

                        return Results.Ok(JsonObject);
                    }
                    else
                    {
                        return Results.Conflict("�������� ����� ��� ������");
                    }
                }
                else
                {
                    return Results.NotFound();
                }







            });
            app.MapPost("/api/refresh", async (HttpContext context, ApplicationDBContext ctx, TokenService tokenService) =>
            {
                // 1. ���������� refreshToken �� cookie
                var refreshToken = context.Request.Cookies["refreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                {
                    Console.WriteLine("Refresh-����� �����������.");
                    return Results.BadRequest("Refresh-����� �����������.");
                }

                // 2. ����� ������������ �� refresh-������
                var storedUser = ctx.Users.FirstOrDefault(u => u.RefreshToken == refreshToken);
                if (storedUser == null)
                {
                    Console.WriteLine("������������ � ����� refresh-������� �� ������.");
                    return Results.Unauthorized();
                }

                // 3. �������� ����� �������� refresh-������
                if (storedUser.RefreshTokenExpiryTime <= DateTime.UtcNow)
                {
                    Console.WriteLine("���� �������� refresh-������ ����.");
                    return Results.Unauthorized();
                }

                // 4. ��������� ������ access-������
                var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, storedUser.Email),
        new Claim(ClaimTypes.NameIdentifier, storedUser.Id.ToString())
    };
                var newAccessToken = tokenService.GenerateAccessToken(claims);

                // 5. ��������� ������ refresh-������
                var newRefreshToken = tokenService.GenerateRefreshToken();

                // 6. ���������� refresh-������ � ���� ������
                storedUser.RefreshToken = newRefreshToken;
                storedUser.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7); // ��������, 7 ����
                await ctx.SaveChangesAsync();

                // 7. ��������� ������ refresh-������ � httpOnly cookie
                context.Response.Cookies.Append("refreshToken", newRefreshToken, new CookieOptions
                {
                    HttpOnly = true, // ������ �� XSS
                    Secure = true, // ������ ��� HTTPS
                    SameSite = SameSiteMode.None, // ��������� �����-�������� �������
                    Expires = DateTime.UtcNow.AddDays(7) // ���� �������� cookie
                });

                Console.WriteLine("���� refreshToken �����������.");

                // 8. ������� ������ accessToken � ���� ������
                Console.WriteLine("����� access-����� ������� �����.");

                var test = new Test()
                {
                    AccessToken = newAccessToken,
                    Email = storedUser.Email
                };
                return Results.Ok(test);
            });
            app.MapPost("/api/validate-token", async (HttpContext context, ApplicationDBContext ctx) =>
            {
                // 1. �������� ����� �� ��������� Authorization
                var token = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

                // 2. ��������� ���������� ��������� ������
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    ValidateLifetime = true, // ��������� ���� �������� ������
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                };

                try
                {
                    // 3. ��������� ������
                    try
                    {
                        var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                        // ���������� ����� claims
                        foreach (var claim in principal.Claims)
                        {
                            Console.WriteLine($"Type: {claim.Type}, Value: {claim.Value}");
                        }

                        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (string.IsNullOrEmpty(userId))
                        {
                            return Results.Unauthorized();
                        }


                        // 5. ����� ������������ � ���� ������
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

                        // 6. ������� ������ ������������
                        return Results.Ok(JsonObject);
                    }
                    catch (SecurityTokenException)
                    {
                        return Results.Unauthorized();
                    }

                }
                catch (SecurityTokenException)
                {
                    // ����� ���������
                    return Results.Unauthorized();
                }
            });

            app.Run();
        }
    }
}
