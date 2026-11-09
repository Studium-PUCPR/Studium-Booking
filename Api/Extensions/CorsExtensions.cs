namespace Studium_Booking.Api.Extensions;

/// <summary>
/// Configures CORS policies with environment variable support for changing deployment domains.
/// </summary>
public static class CorsExtensions
{
    public const string PolicyName = "StudiumCors";

    public static IServiceCollection AddStudiumCors(this IServiceCollection services, IConfiguration configuration)
    {
        var rawOrigins = configuration["Cors:AllowedOrigins"]
            ?? configuration["CORS_ALLOWED_ORIGINS"];

        var allowedOrigins = rawOrigins?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        return services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (allowedOrigins.Length == 0 || allowedOrigins.Contains("*"))
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                }
                else
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
            });
        });
    }
}
