using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Studium_Booking.Api.Extensions;
using Studium_Booking.Api.HostedServices;
using Studium_Booking.Api.Middleware;
using Studium_Booking.Data;
using Studium_Booking.Services.DTOs;
using Studium_Booking.Services.Options;

var builder = WebApplication.CreateBuilder(args);

BookingMappingConfig.Configure();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=studium_booking;Username=postgres;Password=postgres";

builder.Services.Configure<BookingOptions>(
    builder.Configuration.GetSection(BookingOptions.SectionName));

builder.Services.AddDbContext<BookingDbContext>(
    options => options.UseNpgsql(connectionString, npgsql =>
        npgsql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)),
    optionsLifetime: ServiceLifetime.Singleton);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddHostedService<DatabaseMigrationHostedService>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.CritterStackDefaults(options =>
{
    options.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;
    options.Production.GeneratedCodeMode = TypeLoadMode.Static;
    options.Production.AssertAllPreGeneratedTypesExist = true;
});

builder.AddStudiumMessaging();
builder.AddStudiumAuthentication();
builder.Services.AddNotificationsClient(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddStudiumCors(builder.Configuration);

var app = builder.Build();

app.UseCors("StudiumCors");
app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "Studium-Booking",
    pod = Environment.MachineName
}));
app.MapHealthChecks("/health/ready");

return await app.RunJasperFxCommands(args);
