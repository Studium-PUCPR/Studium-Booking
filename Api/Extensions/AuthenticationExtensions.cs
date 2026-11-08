using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Studium_Booking.Domain.Exceptions;

namespace Studium_Booking.Api.Extensions;

public static class AuthenticationExtensions
{
    public const string AdministratorRole = "Administrator";

    public static void AddStudiumAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.Authority = builder.Configuration["AWS:Cognito:Authority"];
                options.Audience = builder.Configuration["AWS:Cognito:ClientId"];

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "sub",
                    RoleClaimType = "cognito:groups"
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy =>
                policy.RequireRole(AdministratorRole));
    }

    public static string GetUserId(this ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("UNAUTHENTICATED_USER", "Authenticated user ID claim is missing.");

        return userId;
    }

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.IsInRole(AdministratorRole);
}
