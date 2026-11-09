# Studium-Booking — Project State & Agent Context (`AGENTS.md`)

> **MANDATORY AGENT DIRECTIVE:**
> After **every user prompt**, always evaluate whether we reached a useful architectural conclusion, resolved a design question, or made code/configuration changes. If so, **update both the relevant sections and Section 3 (`Recent Changes History`) in this `./AGENTS.md` file before finishing your turn** so any future chat session immediately has full context of the project state.

---

## 1. Architectural Decisions & Conclusions Log

### 1.1 Target Framework (`.NET 10.0`) & Clean Architecture
- **Decision:** Build `Studium-Booking` matching the `Studium-Users` and `Studium-Catalog` gold standard with .NET 10 (`net10.0`), Central Package Management (`Directory.Packages.props`), and five Clean Architecture projects:
  - `Studium-Booking.Domain` (zero external dependencies; rich entities and invariants)
  - `Studium-Booking.Data` (EF Core 10, Npgsql PostgreSQL)
  - `Studium-Booking.Services` (DTOs, Wolverine handlers, Mapster, `INotificationsClient`)
  - `Studium-Booking.Api` (Thin composition root, Scalar, OpenAPI, Health check)
  - `Studium-Booking.Tests` (xUnit test suite with 27 passing unit tests)

### 1.2 Hybrid Communication: RabbitMQ (Async) + INotificationsClient (Sync)
- **Problem Solved:** While inter-service state propagation and audit tracking must remain strictly asynchronous (RabbitMQ), immediate user alerts (such as missed check-in deadlines and booking confirmations) are dispatched synchronously to `Studium-Notifications`.
- **Chosen Solution:**
  - Inbound (`studium-booking-events-queue`): Receives catalog space availability events (`RoomStatusChangedEvent`, `RoomMaintenanceEvent`, etc.) and `UserStatusChangedEvent`.
  - Outbound (`studium-users-events-queue`): Publishes `UserActivityIntegrationEvent` (`[MessageIdentity("studium.users.user-activity")]`) to update activity counters and infraction tracking in `Studium-Users`.
  - Outbound (`studium-audit-events-queue`): Publishes `AuditIntegrationEvent` (`[MessageIdentity("studium.audit.integration-event")]`) on booking creation, check-in, check-out, cancellation, and automated no-show.
  - Synchronous HTTP (`INotificationsClient`): Dispatches `POST /api/notifications` directly to `Studium-Notifications` with a 3-attempt exponential backoff retry policy (200ms, 500ms, 1000ms).
  - Outbox Middleware: Configured `opts.Policies.AutoApplyTransactions()` to guarantee transactional message publishing.
  - Poison Message & Dead-Letter Handling: Configured Wolverine transient exception retries (`NpgsqlException`, `TimeoutException`, `HttpRequestException`) with exponential cooldowns and unhandled errors routing directly to `.error` queue.

### 1.3 Waitlist Expiration & Auto-Progression (UC016)
- **Context:** When a booking was cancelled, the first candidate was notified, but if they failed to claim the reservation, the slot sat abandoned indefinitely.
- **Decision:**
  - Added configurable claim grace period (`WaitlistClaimGraceMinutes = 15`, default 15 min).
  - Dispatches delayed message `CheckWaitlistClaimDeadlineMessage(Guid WaitlistId)`.
  - Implemented `CheckWaitlistClaimDeadlineHandler` that marks unclaimed entries as `Expired` and automatically promotes and notifies the next candidate in line.
  - Promotes active waitlist entries upon booking creation.

### 1.4 Database Resilience & Health Check Segregation
- Configured Npgsql's transient execution strategy (`EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)`) to survive RDS multi-AZ failovers and network drops.
- Segregated Kubernetes probes: shallow liveness at `/health` (process responsiveness) and deep readiness at `/health/ready` via `DatabaseHealthCheck` (`BookingDbContext.Database.CanConnectAsync()`).

---

## 2. Directory Structure & Key Files
- `Domain/`:
  - `Entities/Bookings/Booking.cs` — Aggregate root enforcing business invariants.
  - `Entities/Waitlist/WaitlistEntry.cs` — Queuing entity for fully booked space promotion.
  - `Entities/ReplicatedCatalog/ReplicatedRoom.cs`, `ReplicatedMaintenanceBlock.cs`, `ReplicatedOperatingHours.cs` — Autonomous local read models.
  - `Entities/Users/BlockedUser.cs` — Read model synchronized from `Studium-Users`.
- `Data/`:
  - `BookingDbContext.cs` — EF Core context with GiST exclusion constraint and btree_gist extension.
  - `Configurations/` — Fluent API configurations.
  - `Migrations/` — Auto-applied migrations.
- `Services/`:
  - `Clients/` — `INotificationsClient.cs` and `NotificationsClient.cs` with exponential backoff retry.
  - `Handlers/` — Wolverine event handlers (`SpaceAvailabilitySyncHandler`, `UserEligibilitySyncHandler`, `CheckReservationNoShowHandler`, `CheckWaitlistClaimDeadlineHandler`).
  - `DTOs/` — Request, response, and event contracts (`AuditIntegrationEvent`, `UserActivityIntegrationEvent`, `CheckWaitlistClaimDeadlineMessage`).
- `Api/`:
  - `Controllers/BookingsController.cs`, `WaitlistController.cs` — REST endpoints.
  - `Extensions/` — WebApplicationBuilder extensions (`MessagingExtensions`, `AuthenticationExtensions`, `HttpClientExtensions`, `DatabaseHealthCheck`).
  - `HostedServices/DatabaseMigrationHostedService.cs` — Startup migration runner.
- `Tests/` — 27 unit tests covering domain rules, waitlist expiration, and mapping.

---

## 3. Recent Changes History
- **2026-11-09 (Waitlist Expiration Timer, Database Resilience & DLQ Policies):**
  - Implemented `CheckWaitlistClaimDeadlineHandler` with configurable `WaitlistClaimGraceMinutes = 15`.
  - Scheduled deadline checks on waitlist promotion across cancellation and no-show triggers; auto-promotes next in queue upon expiration.
  - Configured `EnableRetryOnFailure(3)` on PostgreSQL connection.
  - Separated shallow `/health` liveness probe and deep `/health/ready` probe (`DatabaseHealthCheck`).
  - Configured Wolverine exception policies for transient retries and dead-letter error queue routing.
  - Regenerated Wolverine static handlers; 27/27 unit tests pass.
- **2026-11-09 (Audit Integration, Resilient Retry & Wolverine Outbox):**
  - Added `AuditIntegrationEvent` (`[MessageIdentity("studium.audit.integration-event")]`) published to `studium-audit-events-queue`.
  - Wired `BookingsController` and `CheckReservationNoShowHandler` to publish compliance events on booking creation, check-in, check-out, cancellation, and no-show.
  - Implemented 3-attempt exponential backoff retry policy (200ms, 500ms, 1000ms) in `NotificationsClient`.
  - Enabled Wolverine transactional outbox via `opts.Policies.AutoApplyTransactions()`.
- **2026-11-09 (Ubiquitous Language Refactor — SpaceAvailabilitySyncHandler & UserEligibilitySyncHandler):**
  - Renamed `CatalogEventsHandler` to `SpaceAvailabilitySyncHandler` in `Services/Handlers/`.
  - Renamed `UserStatusChangedHandler` to `UserEligibilitySyncHandler` in `Services/Handlers/`.
- **2026-11-09 (Synchronous Notification Client Integration):**
  - Implemented typed `INotificationsClient` dispatching alerts synchronously to `POST /api/notifications` in `Studium-Notifications`.
- **2026-11-09 (RabbitMQ Migration & SQS Storage Branch):**
  - Migrated messaging to `WolverineFx.RabbitMQ` (`6.63.0`) over AMQP broker (`studium-rabbitmq-service:5672`).
