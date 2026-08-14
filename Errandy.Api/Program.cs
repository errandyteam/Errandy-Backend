using Errandy.Api.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Errandy.Application.Interfaces;
using FluentValidation;
using MediatR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

// MediatR — scans Errandy.Application for all handlers
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
    typeof(Errandy.Application.Features.Errands.CreateErrand.CreateErrandCommand).Assembly));

// FluentValidation — scans same assembly for all validators
builder.Services.AddValidatorsFromAssembly(
    typeof(Errandy.Application.Features.Errands.CreateErrand.CreateErrandCommand).Assembly);

// TEMPORARY fakes — swap these out once A/B ship the real implementations
builder.Services.AddScoped<IEscrowService, FakeEscrowService>();
builder.Services.AddScoped<ICurrentUserService, FakeCurrentUserService>();
builder.Services.AddScoped<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddDbContext<InMemoryApplicationDbContext>(options =>
    options.UseInMemoryDatabase("ErrandyLocalTest"));
builder.Services.AddScoped<IApplicationDbContext>(sp =>
    sp.GetRequiredService<InMemoryApplicationDbContext>());

builder.Services.AddSingleton<IBackgroundJobScheduler, FakeInProcessBackgroundJobScheduler>();

// TEMPORARY — replace with real JWT auth once Engineer A ships i
builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(
        TestAuthHandler.SchemeName, options => { });

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();