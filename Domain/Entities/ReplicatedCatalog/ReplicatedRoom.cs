using Studium_Booking.Domain.Common;

namespace Studium_Booking.Domain.Entities.ReplicatedCatalog;

public class ReplicatedRoom
{
    private readonly List<ReplicatedMaintenanceBlock> _maintenanceBlocks = [];
    private readonly List<ReplicatedOperatingHours> _operatingHours = [];

    public Guid Id { get; init; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime LastSyncedAtUtc { get; private set; }

    public IReadOnlyCollection<ReplicatedMaintenanceBlock> MaintenanceBlocks => _maintenanceBlocks.AsReadOnly();
    public IReadOnlyCollection<ReplicatedOperatingHours> OperatingHours => _operatingHours.AsReadOnly();

    private ReplicatedRoom() { }

    public static ReplicatedRoom Create(
        Guid id,
        string code,
        string name,
        bool isActive,
        DateTime nowUtc)
    {
        var normalizedNow = UtcDateTime.Normalize(nowUtc);
        return new ReplicatedRoom
        {
            Id = id,
            Code = code,
            Name = name,
            IsActive = isActive,
            LastSyncedAtUtc = normalizedNow
        };
    }

    public void UpdateStatus(bool isActive, DateTime nowUtc)
    {
        IsActive = isActive;
        LastSyncedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public void UpdateDetails(string code, string name, DateTime nowUtc)
    {
        Code = code;
        Name = name;
        LastSyncedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public void ScheduleMaintenance(Guid blockId, DateTime startsAtUtc, DateTime endsAtUtc, string reason, DateTime nowUtc)
    {
        _maintenanceBlocks.RemoveAll(b => b.Id == blockId);
        _maintenanceBlocks.Add(new ReplicatedMaintenanceBlock(blockId, Id, startsAtUtc, endsAtUtc, reason));
        LastSyncedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public void CancelMaintenance(Guid blockId, DateTime nowUtc)
    {
        _maintenanceBlocks.RemoveAll(b => b.Id == blockId);
        LastSyncedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public void SetOperatingHours(IEnumerable<ReplicatedOperatingHours> hours, DateTime nowUtc)
    {
        _operatingHours.Clear();
        _operatingHours.AddRange(hours);
        LastSyncedAtUtc = UtcDateTime.Normalize(nowUtc);
    }

    public bool IsUnderMaintenance(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var start = UtcDateTime.Normalize(startsAtUtc);
        var end = UtcDateTime.Normalize(endsAtUtc);
        return _maintenanceBlocks.Any(m => m.Overlaps(start, end));
    }

    public bool IsWithinOperatingHours(DateTime startsAtUtc, DateTime endsAtUtc, string timeZoneId = "America/Sao_Paulo")
    {
        if (_operatingHours.Count == 0)
            return true; // No restrictions configured

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(UtcDateTime.Normalize(startsAtUtc), timeZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(UtcDateTime.Normalize(endsAtUtc), timeZone);

        if (localStart.Date != localEnd.Date)
            return false; // Multi-day or crossing midnight

        var dayOfWeek = localStart.DayOfWeek;
        var startTo = TimeOnly.FromTimeSpan(localStart.TimeOfDay);
        var endTo = TimeOnly.FromTimeSpan(localEnd.TimeOfDay);

        var matchingSchedules = _operatingHours.Where(s => s.DayOfWeek == dayOfWeek).ToList();
        if (matchingSchedules.Count == 0)
            return false; // Closed on this day

        return matchingSchedules.Any(s => startTo >= s.OpensAt && endTo <= s.ClosesAt);
    }
}
