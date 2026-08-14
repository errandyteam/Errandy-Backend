using Errandy.Api.Extensions;
using Errandy.Api.Middleware;
using Errandy.Application;
using Errandy.Application.Common.Interfaces;
using Errandy.Infrastructure;
using Errandy.Infrastructure.Persistence;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ----- Layer registrations -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ----- Swagger with JWT support -----
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Errandy API",
        Version = "v1"
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token.",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    options.AddSecurityDefinition("Bearer", scheme);

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { scheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

// ----- Migrate + seed the database on startup -----
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<ErrandyDbContext>();
    var hasher = services.GetRequiredService<IPasswordHasher>();

    var adminEmail =
        builder.Configuration["Seed:AdminEmail"]
        ?? "admin@errandy.com";

    var adminPassword =
        builder.Configuration["Seed:AdminPassword"]
        ?? "Admin@12345";

    await DatabaseSeeder.SeedAsync(
        db,
        hasher,
        adminEmail,
        adminPassword);
}

// ----- Pipeline -----
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .ExcludeFromDescription();

app.Run();

// Exposed for integration testing.
public partial class Program { }