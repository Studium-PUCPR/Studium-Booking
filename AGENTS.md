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
  - `Studium-Booking.Tests` (xUnit test suite with 26 passing unit tests)

### 1.2 Hybrid Communication: RabbitMQ (Async) + INotificationsClient (Sync)
- **Problem Solved:** While inter-service state propagation and audit tracking must remain strictly asynchronous (RabbitMQ), immediate user alerts (such as missed check-in deadlines and booking confirmations) are dispatched synchronously to `Studium-Notifications`.
- **Chosen Solution:**
  - Inbound (`studium-booking-events-queue`): Receives catalog space availability events (`RoomStatusChangedEvent`, `RoomMaintenanceEvent`, etc.) and `UserStatusChangedEvent`.
  - Outbound (`studium-users-events-queue`): Publishes `UserActivityIntegrationEvent` (`[MessageIdentity("studium.users.user-activity")]`) to update activity counters and infraction tracking in `Studium-Users`.
  - Outbound (`studium-audit-events-queue`): Publishes `AuditIntegrationEvent` (`[MessageIdentity("studium.audit.integration-event")]`) on booking creation, check-in, check-out, cancellation, and automated no-show.
  - Synchronous HTTP (`INotificationsClient`): Dispatches `POST /api/notifications` directly to `Studium-Notifications` with a 3-attempt exponential backoff retry policy (200ms, 500ms, 1000ms).
  - Outbox Middleware: Configured `opts.Policies.AutoApplyTransactions()` to guarantee transactional message publishing.

### 1.3 Ubiquitous Language & Domain Handler Naming
- **Context:** Handlers named after external sender services (`CatalogEventsHandler`, `UserStatusChangedHandler`) created conceptual coupling that obscured domain responsibility in `Studium-Booking`.
- **Decision:**
  - Renamed `CatalogEventsHandler` to `SpaceAvailabilitySyncHandler` to emphasize that it synchronizes room availability and maintenance windows for bookings.
  - Renamed `UserStatusChangedHandler` to `UserEligibilitySyncHandler` to clarify that it maintains the local `BlockedUsers` read model for reservation authorization.

### 1.4 Pre-Generated Wolverine Codegen in Docker Build
- In Docker production containers, dynamic Roslyn compilation causes cold-start latency and container timeouts.
- `CritterStackDefaults` is configured with `TypeLoadMode.Static` in Production, and `codegen write` pre-generates handler code during Docker multi-stage builds.

### 1.5 Horizontal Scalability & Kubernetes Topology (Rubric Anchor R2/R3)
- Configured `k8s/deployment.yaml` with `replicas: 3` under the unified `studium` namespace.
- Added HorizontalPodAutoscaler targeting 90% CPU utilization (min 3, max 6 replicas).
- Health probes configured at `/health` with cold start delays (liveness 30s, readiness 15s) to allow startup database migrations to finish.

### 1.6 Concurrency Protection: PostgreSQL GiST Exclusion Constraint
- Uses native PostgreSQL GiST indexing (`btree_gist` extension) to prevent double bookings at the database engine level via `EXCLUDE USING gist (room_id WITH =, time_slot WITH &&) WHERE (status IN ('Confirmed', 'CheckedIn'))`.

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
  - `Handlers/` — Wolverine event handlers (`SpaceAvailabilitySyncHandler`, `UserEligibilitySyncHandler`, `CheckReservationNoShowHandler`).
  - `DTOs/` — Request, response, and event contracts (`AuditIntegrationEvent`, `UserActivityIntegrationEvent`).
- `Api/`:
  - `Controllers/BookingsController.cs`, `WaitlistController.cs` — REST endpoints.
  - `Extensions/` — WebApplicationBuilder extensions (`MessagingExtensions`, `AuthenticationExtensions`, `HttpClientExtensions`).
  - `HostedServices/DatabaseMigrationHostedService.cs` — Startup migration runner.
- `Tests/` — 26 unit tests covering domain rules and mapping.

---

## 3. Recent Changes History
- **2026-11-09 (Audit Integration, Resilient Retry & Wolverine Outbox):**
  - Added `AuditIntegrationEvent` (`[MessageIdentity("studium.audit.integration-event")]`) published to `studium-audit-events-queue`.
  - Wired `BookingsController` and `CheckReservationNoShowHandler` to publish compliance events on booking creation, check-in, check-out, cancellation, and no-show.
  - Implemented 3-attempt exponential backoff retry policy (200ms, 500ms, 1000ms) in `NotificationsClient`.
  - Enabled Wolverine transactional outbox via `opts.Policies.AutoApplyTransactions()`.
  - Added `RabbitMQ:AuditQueueName` to `appsettings.json` and `k8s/deployment.yaml`.
  - Regenerated Wolverine static handlers and verified all 26 unit tests pass.
- **2026-11-09 (Ubiquitous Language Refactor — SpaceAvailabilitySyncHandler & UserEligibilitySyncHandler):**
  - Renamed `CatalogEventsHandler` to `SpaceAvailabilitySyncHandler` in `Services/Handlers/`.
  - Renamed `UserStatusChangedHandler` to `UserEligibilitySyncHandler` in `Services/Handlers/`.
  - Replaced `BookingIntegrationEvent` with `UserActivityIntegrationEvent` (`[MessageIdentity("studium.users.user-activity")]`).
  - Removed dead notification event definitions (`ReservationNotificationEvent`, `WaitlistSlotAvailableEvent`).
  - Updated `appsettings.json` removing obsolete SQS configuration.
  - Regenerated Wolverine static handlers and verified all 26 unit tests pass.
- **2026-11-09 (Synchronous Notification Client Integration):**
  - Implemented typed `INotificationsClient` dispatching alerts synchronously to `POST /api/notifications` in `Studium-Notifications`.
  - Updated `CheckReservationNoShowHandler` and `BookingsController` to synchronously deliver missed deadline and booking alerts.
  - Removed `studium-notifications-queue` publishing from RabbitMQ configuration.
- **2026-11-09 (RabbitMQ Migration & SQS Storage Branch):**
  - Migrated messaging to `WolverineFx.RabbitMQ` (`6.63.0`) over AMQP broker (`studium-rabbitmq-service:5672`).
- **2026-11-08 (Initial Implementation & Deployment):**
  - Scaffolded .NET 10 microservice with 3 replicas, GiST exclusion constraints, and 26 unit tests.
