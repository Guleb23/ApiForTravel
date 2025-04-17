
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

            //��������� �������������� �� JWT ������
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


            //����������� CORS, ����� ����� ���� �������� ������� � ������
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder.WithOrigins("https://guleb23-travelapp-6b18.twc1.net", "https://travel-app-ten-wine.vercel.app")// ��������� ������� � ������ ������
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

            //����������� ������ ��� �������� ������
            builder.Services.AddScoped<TokenService>();
            //����������� ������ ��� ����������� � ��������� ����������� ������
            builder.Services.AddScoped<PasswordHasher>();
            //����������� ������ �����������
            builder.Services.AddScoped<AuthService>();

            var app = builder.Build();
            //����������� �������������� ����(������ ��� ��������, �� ������� ������)
            app.Urls.Add("http://*:5000");
            //���������� ��������� �����
            app.UseCors("AllowAllOrigins");
            //�������� ����������� ������� � ����������� �� URL
            if (!app.Environment.IsEnvironment("Test"))
            {
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

                // ������ �����, ���� � ��� (�� �������� �������)
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(uploadsPath),
                    RequestPath = "/uploads",
                });
            }

            // ���������� Swagger ��� ������� ������� � �������������� ������������ ����������
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

                // ������� ���������� - � �������� ���������� ����� ��������� ������������
                travel.LikesCount++;
                await db.SaveChangesAsync();

                return Results.Ok(new { LikesCount = travel.LikesCount });
            });
            //�������� ������ �����������
            app.MapPost("/api/users/{userId}/travels", async (int userId,[FromBody] TravelCreateRequest request, ApplicationDBContext db,IWebHostEnvironment env) =>
            {
                // 1. �������� ������������
                var user = await db.Users.FindAsync(userId);
                if (user == null)
                    return Results.NotFound($"User with id {userId} not found");

                // 2. ���������
                if (request.Points == null || !request.Points.Any())
                    return Results.BadRequest("At least one point is required");

                // 3. ������� ����� uploads ���� � ���
                var uploadsPath = Path.Combine(env.ContentRootPath, "uploads");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                // 4. �������� ������� Travel
                var travel = new TravelModel
                {
                    Title = request.Title != null ? request.Title :  $"������� {request.Date}",
                    UserId = userId,
                   
                    Date = DateTimeOffset.Parse(request.Date).UtcDateTime, // ������������ ������
                    Points = new List<TravelPoint>(),
                    Tags = null
                };

                // 5. ��������� ����� ��������
                foreach (var pointRequest in request.Points)
                {
                    // ������ ����� � ��������� �� null/������ ������
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
                        DepartureTime = departureTime.HasValue ? departureTime.Value : null, // ��������� ��� ������
                        note = pointRequest.note,
                        Photos = new List<Photo>()
                    };

                    // 6. ��������� ����������
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

                                // �������� ������� ����� (��������, �� 5MB)
                                if (bytes.Length > 5 * 1024 * 1024) // �������� �� ������ ����� 5MB
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

                // 7. ���������� � ����
                db.Travels.Add(travel);
                await db.SaveChangesAsync();

                // 8. ������� ����������
                return Results.Ok(travel);
            });
            //��������� ���� => ���� � �����
            app.MapPut("/api/travels/{travelId}/share", async (int travelId, [FromBody] ShareRequestModel request, ApplicationDBContext db) =>
            {
                try
                {
                    var travel = await db.Travels.FindAsync(travelId);
                    if (travel == null)
                    {
                        if (travelId > 9000) // ���� travelId ������ 9000, �� ���������� ������ �������
                        {
                            return Results.StatusCode(500);
                        }
                        return Results.NotFound(); // ���� travelId �� ������, ���������� 404
                    }

                    // ������� �����, ���� ��� �� null
                    if (request.Tags != null)
                    {
                        travel.Tags.Clear(); // �������� ���� ����� �����������
                        travel.Tags.AddRange(request.Tags); // �������� ���� ������ ����������
                    }
                    else
                    {
                        travel.Tags.Clear(); // ���� ���� null, ������� ��
                    }

                    await db.SaveChangesAsync();
                    return Results.Ok(); // ���������� ������ 200 ��� �������� ����������
                }
                catch (Exception ex)
                {
                    // �������� ���������� ��� �����������
                    Console.Error.WriteLine($"Error occurred while updating travel tags: {ex.Message}");
                    return Results.StatusCode(500); // � ������ ������ ���������� 500
                }
            });      //����������
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
                    RefreshToken = "0",
                    Username = user.Username

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
                    ["Id"] = newUser.Id,
                    ["AccessToken"] = accessToken,
                    ["Email"] = newUser.Email,
                    ["Username"] = newUser.Username
                };

                return Results.Ok(JsonObject);
            });
            //�����������
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
                            ["Id"] = storedUser.Id,
                            ["AccessToken"] = accessToken,
                            ["Email"] = storedUser.Email,
                            ["Username"] = storedUser.Username
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
            //��������� ������ ������ � ��������
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
                    Id = storedUser.Id,
                    AccessToken = newAccessToken,
                    Email = storedUser.Email,
                    Username = storedUser.Username
                };
                return Results.Ok(test);
            });
            //��������� ���� ������ � ��������
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
                            ["Id"] = user.Id,
                            ["RefreshToken"] = user.RefreshToken,
                            ["Email"] = user.Email,
                            ["Username"] = user.Username
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
            //��������� ���� �������������
            app.MapGet("/api/users", (ApplicationDBContext ctx) =>
            {
                return ctx.Users.ToList();
            });

            //��������� ��������� ������������� ������������, ���������� ��� ��������� �������
            app.MapGet("/api/routes/{userId}", (ApplicationDBContext ctx, int userId) =>
            {
                var routes = ctx.Travels.Where( t => t.UserId == userId)
                    .Include(t => t.Points)          // �������� ����� ��������
                    .ThenInclude(p => p.Coordinates)  // �������� ���������� ��� ������ �����
                    .Include(t => t.Points)
                    .ThenInclude(p => p.Photos)       // �������� ���������� (���� �����)
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

            //��������� ��������� ������������� ������������ ��� ��������� ������
            app.MapGet("/api/travel/{travelId}", (ApplicationDBContext ctx, int travelId) =>
            {
                return ctx.Travels.Where(t => t.Id == travelId).ToList();
            });

            //��������� ����� ������������� ��������
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

            //�������� ����� ������������� ��������
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

            //��������� ������ � ������, �� ���������
            app.MapGet("/api/feed", async (ApplicationDBContext db, [FromQuery] int page = 1, [FromQuery] int pageSize = 5, [FromQuery] string search = "", [FromQuery] string tag = "") =>
            {
                // ������� ������
                IQueryable<TravelModel> query = db.Travels
                    .Include(t => t.User)
                    .Include(t => t.Points)
                        .ThenInclude(p => p.Coordinates)
                    .Include(t => t.Points)
                        .ThenInclude(p => p.Photos)
                    .Where(t => t.Tags != null && t.Tags.Any()); // ������ ����������� � ������

                // ���������� �� ���������� �������
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(t => t.Title.Contains(search));
                }

                // ���������� �� ����
                if (!string.IsNullOrEmpty(tag))
                {
                    query = query.Where(t => t.Tags.Contains(tag));
                }

                // �������� ����� ���������� ��� ���������
                var totalCount = await query.CountAsync();

                // �������� ������ � ����������
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

            //��������� ������ � �����
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

                // ������� ����������
                travel.LikesCount = Math.Max(0, travel.LikesCount - 1);
                await db.SaveChangesAsync();

                return Results.Ok(new { LikesCount = travel.LikesCount });
            });
            //��������� �����������
            app.MapPatch("/api/travels/{travelId}", async (int travelId,[FromBody] TravelUpdateRequest request, ApplicationDBContext db,IWebHostEnvironment env) =>
            {
                try
                {
                    // 1. ������� ������������ �����������
                    var travel = await db.Travels
                        .Include(t => t.Points)
                            .ThenInclude(p => p.Coordinates)
                        .Include(t => t.Points)
                            .ThenInclude(p => p.Photos)
                        .FirstOrDefaultAsync(t => t.Id == travelId);

                    if (travel == null)
                        return Results.NotFound($"Travel with id {travelId} not found");

                    // 2. ��������� �������� ������
                    if (!string.IsNullOrEmpty(request.Title))
                        travel.Title = request.Title;

                    if (!string.IsNullOrEmpty(request.Date))
                        travel.Date = DateTimeOffset.Parse(request.Date).UtcDateTime;

                    // 3. ��������� ����� (���� ��� ���� � �������)
                    if (request.Tags != null)
                        travel.Tags = request.Tags;

                    // 4. ��������� ����� �������� (���� ��� ���� � �������)
                    if (request.Points != null)
                    {
                        // ������� ������������ �����, ������� ��� � ����� �������
                        var pointsToRemove = travel.Points
                            .Where(existingPoint => !request.Points.Any(newPoint => newPoint.Id == existingPoint.Id))
                            .ToList();

                        foreach (var point in pointsToRemove)
                        {
                            // ������� ��������� ���������� �� �������� �������
                            foreach (var photo in point.Photos)
                            {
                                var filePath = Path.Combine(env.ContentRootPath, photo.FilePath);
                                if (File.Exists(filePath))
                                    File.Delete(filePath);
                            }
                            db.TravelPoints.Remove(point);
                        }

                        // ��������� ������������ ��� ��������� ����� �����
                        foreach (var pointRequest in request.Points)
                        {
                            // ������ ����� � ��������� �� null/������ ������
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
                                // ��������� ������������ �����
                                existingPoint.Name = pointRequest.Name ?? existingPoint.Name;
                                existingPoint.Address = pointRequest.Address ?? existingPoint.Address;
                                existingPoint.Type = pointRequest.Type ?? existingPoint.Type;
                                existingPoint.DepartureTime = departureTime ?? existingPoint.DepartureTime;
                                existingPoint.note = pointRequest.note ?? existingPoint.note;

                                // ��������� ����������
                                if (pointRequest.Coordinates != null)
                                {
                                    existingPoint.Coordinates.Lat = pointRequest.Coordinates.Lat;
                                    existingPoint.Coordinates.Lon = pointRequest.Coordinates.Lon;
                                }

                                // ��������� ����������
                                if (pointRequest.Photos != null)
                                {
                                    // ������� ����������, ������� ���� ������� �� �������
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

                                    // ��������� ����� ����������
                                    foreach (var photoRequest in pointRequest.Photos.Where(p => p.Id == 0))
                                    {
                                        if (string.IsNullOrEmpty(photoRequest.Base64Content))
                                            continue;

                                        var base64Data = photoRequest.Base64Content.Split(',').Last();
                                        var bytes = Convert.FromBase64String(base64Data);

                                        // �������� ������� �����
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
                                // ��������� ����� �����
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

                                // ��������� ���������� ��� ����� �����
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

                    // 5. ��������� ���������
                    await db.SaveChangesAsync();

                    // 6. ���������� ����������� �����������
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
                // ������� ���� � ���� ������
                var photo = await context.PointPhotos
                    .FirstOrDefaultAsync(p => p.Id == photoId && p.TravelPointId == pointId);

                if (photo == null)
                {
                    return Results.NotFound(new { Success = false, Message = "���� �� �������" });
                }

                // ������� ���������� ����
                if (!string.IsNullOrEmpty(photo.FilePath))
                {
                    var filePath = photo.FilePath;
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                // ������� ������ �� ����
                context.PointPhotos.Remove(photo);
                await context.SaveChangesAsync();

                return Results.Ok(new { Success = true, DeletedPhotoId = photoId });
            });

            app.Run();
        }
    }
    

    

    

    
}

