using Microsoft.EntityFrameworkCore;
using Studium_Booking.Data;

namespace Studium_Booking.Api.HostedServices;

/// <summary>
/// Applies pending EF Core migrations when the host starts. Runs only on host startup,
/// so CLI commands such as Wolverine's codegen write never touch the database.
/// </summary>
public class DatabaseMigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
