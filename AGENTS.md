# Studium-Booking — Project State & Agent Context (`AGENTS.md`)

> **MANDATORY AGENT DIRECTIVE:**
> After **every user prompt**, always evaluate whether we reached a useful architectural conclusion, resolved a design question, or made code/configuration changes. If so, **update both the relevant sections and Section 3 (`Recent Changes History`) in this `./AGENTS.md` file before finishing your turn** so any future chat session immediately has full context of the project state.

---

## 1. Architectural Decisions & Conclusions Log

### 1.1 Microservice Autonomy & Read Model Replication
- **Clean CQRS Isolation:** `Studium-Booking` maintains its own read models for availability (`ReplicatedRooms`, `ReplicatedMaintenanceBlocks`, `ReplicatedOperatingHours`) and eligibility (`BlockedUsers`), completely isolating it from direct database links to `Studium-Catalog` or `Studium-Users`.
- **Domain Cleanliness:** Leaked user policy options (`MaxNoShowsBeforeSuspension`, `DefaultSuspensionDays`) have been removed from `BookingOptions.cs`, keeping `Booking` strictly focused on space reservation, time windows, and waitlist management.

### 1.2 Concurrency & Double-Booking Elimination
- **PostgreSQL GiST Exclusion Constraint:** Enforced at the PostgreSQL storage engine level via `CK_Bookings_NoDoubleBooking` using `tsrange("StartsAtUtc", "EndsAtUtc") WITH &&` and `RoomId WITH =` where `Status IN (0, 1)`.
- **Exclusion Violation Handling:** `CreateBooking` catches `DbUpdateException` with `PostgresErrorCodes.ExclusionViolation` (`23P01`) and throws `BookingConflictException`, returning a clean `409 Conflict`.
- **Composite Index:** Created composite index `IX_Bookings_Room_Status_TimeSlot` on `("RoomId", "Status", "StartsAtUtc", "EndsAtUtc")` for instant conflict scans.

### 1.3 Cascading Invalidation & Cancellation
- **Room Maintenance Cascading:** When a `RoomMaintenanceEvent` (scheduled) is received in `SpaceAvailabilitySyncHandler`, active overlapping bookings are automatically cancelled with reason `"Room maintenance scheduled"`, audit logged, and notifications dispatched to affected students.
- **User Suspension Cascading:** When a `UserStatusChangedEvent` (user blocked) is received in `UserEligibilitySyncHandler`, active upcoming reservations for that student are automatically cancelled, notifying the student and immediately promoting the next candidate in the waitlist.

### 1.4 Waitlist Claim Grace Window & Auto-Progression (UC016)
- **Configurable Grace Window:** `WaitlistClaimGraceMinutes` (default 15 minutes, configurable via `appsettings.json` and `Booking__WaitlistClaimGraceMinutes`).
- **Delayed Message Bus Scheduling:** Promoted candidates receive a delayed message `CheckWaitlistClaimDeadlineMessage`. If unclaimed after grace expires, the candidate transitions to `Expired` and the next student in line is notified.

### 1.5 Transactional Inbox Pattern & EventId Propagation
- **Transactional Inbox Pattern:** Implemented in consumer handlers (`UserEligibilitySyncHandler` and `SpaceAvailabilitySyncHandler`) via `InboxMessage` entity (`Id: Guid`, `HandlerName: string`, `ReceivedAtUtc: DateTime`) with unique primary key index and B-Tree index on `ReceivedAtUtc`.
- **Zero-Overhead Lazy Pruning:** Consumer handlers perform throttled cleanup (every 6 hours) via `ExecuteDeleteAsync` for inbox messages older than 7 days, avoiding extra background jobs or cron services.
- **Outbound EventId Propagation:** All events published by `Studium-Booking` (`UserActivityIntegrationEvent`, `AuditIntegrationEvent`) now populate a deterministic `EventId: Guid.NewGuid()` for end-to-end distributed idempotency.

### 1.6 Database Resilience, Health Checks & CORS
- **Npgsql Retry Strategy:** Configured `EnableRetryOnFailure(3)` inside `UseNpgsql`.
- **Health Segregation:** Shallow liveness `/health` and deep readiness `/health/ready` via zero-dependency `DatabaseHealthCheck`.
- **Dynamic CORS:** Configured via `CorsExtensions.cs` reading `Cors:AllowedOrigins` with wildcard fallback.

---

## 2. Project Structure & File Map
- [`Directory.Build.props`](./Directory.Build.props) & [`Directory.Packages.props`](./Directory.Packages.props) — CPM locking dependencies.
- [`Studium-Booking.slnx`](./Studium-Booking.slnx) — Solution root.
- `Domain/`:
  - `Entities/Bookings/Booking.cs`, `BookingStatus.cs`, `ReservationTimeSlot.cs`, `BookingAuditLog.cs`.
  - `Entities/Waitlist/WaitlistEntry.cs`, `WaitlistStatus.cs`.
  - `Entities/ReplicatedCatalog/ReplicatedRoom.cs`.
  - `Entities/Users/BlockedUser.cs`.
  - `Entities/Inbox/InboxMessage.cs`.
- `Data/`:
  - `BookingDbContext.cs`, `Configurations/BookingConfig.cs`, `Configurations/InboxMessageConfig.cs`.
  - `Migrations/` — Includes GiST exclusion constraint, composite indexes, and `Add_InboxMessages`.
- `Services/`:
  - `Handlers/SpaceAvailabilitySyncHandler.cs` — Maintenance synchronization & cascading cancellation with Inbox deduplication.
  - `Handlers/UserEligibilitySyncHandler.cs` — User block synchronization & cascading cancellation with Inbox deduplication.
  - `Handlers/CheckReservationNoShowHandler.cs` — 15-minute no-show automated evaluator.
  - `Handlers/CheckWaitlistClaimDeadlineHandler.cs` — 15-minute claim deadline evaluator.
  - `Options/BookingOptions.cs` — Grace periods, default timezone.
- `Api/`:
  - `Controllers/BookingsController.cs`, `WaitlistController.cs`, `OccupancyController.cs`.
  - `Extensions/MessagingExtensions.cs`, `AuthenticationExtensions.cs`, `DatabaseHealthCheck.cs`, `CorsExtensions.cs`.
- `Tests/` — 28 unit tests passing.
- [`Dockerfile`](./Dockerfile) & [`k8s/deployment.yaml`](./k8s/deployment.yaml) — Production containers and Kubernetes manifests.

---

## 3. Recent Changes History
- **2026-11-10 (Reject Past Reservations & Cancel In-Progress Bookings on Suspension):**
  - `CreateBooking` now rejects a start time more than 5 minutes in the past (`START_IN_PAST`, 400). Before, a reservation for an hour that had already passed was accepted.
  - `UserEligibilitySyncHandler` now cancels every `Confirmed` booking that has not yet ended (`EndsAtUtc > OccurredAtUtc`), not only those that start after the block. Before, an in-progress reservation was skipped, so neither the cancellation nor the REST notification happened.
  - Verified 28/28 unit tests pass.
- **2026-11-09 (Transactional Inbox Pattern & EventId Propagation):**
  - Added `InboxMessage` entity and configuration with B-Tree index on `ReceivedAtUtc`.
  - Scaffolded EF Core migration `20261109222820_Add_InboxMessages`.
  - Implemented inbox deduplication check and 6-hour lazy pruning in `UserEligibilitySyncHandler` and `SpaceAvailabilitySyncHandler`.
  - Added `EventId: Guid.NewGuid()` propagation across all published events.
  - Regenerated Wolverine handlers and verified 28/28 unit tests pass.
- **2026-11-09 (Cascading Invalidation, Double-Booking Handling, Composite Index & Domain Cleanliness):**
  - Added exclusion violation catch (`PostgresErrorCodes.ExclusionViolation`) in `CreateBooking`.
  - Added composite index `IX_Bookings_Room_Status_TimeSlot` via migration `20261109214025_Add_Booking_Composite_Index`.
  - Implemented cascading cancellation in `SpaceAvailabilitySyncHandler` for scheduled maintenance blocks.
  - Implemented cascading cancellation and waitlist promotion in `UserEligibilitySyncHandler` for suspended users.
  - Removed leaked user options (`MaxNoShowsBeforeSuspension`, `DefaultSuspensionDays`) from `BookingOptions.cs` and manifests.
  - Added `CorsExtensions.cs` for environment-driven CORS configuration.
- **2026-11-09 (Waitlist Auto-Expiration, DB Resilience & Deep Readiness Probe):**
  - Implemented `WaitlistClaimGraceMinutes = 15` and `CheckWaitlistClaimDeadlineHandler`.
  - Added `EnableRetryOnFailure(3)` and `/health/ready` probe.
  - Added Wolverine retry policies and error queue routing.
