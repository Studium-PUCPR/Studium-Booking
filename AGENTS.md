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
  - Inbound (`studium-booking-events-queue`): Receives catalog events (`RoomStatusChangedEvent`, `RoomMaintenanceEvent`, etc.) and `UserStatusChangedEvent`.
  - Outbound (`studium-users-events-queue`): Publishes `BookingIntegrationEvent` (`ReservationCreated`, `ReservationCheckedIn`, `ReservationCancelled`, `NoShowRecorded`) for infraction counters in `Studium-Users`.
  - Synchronous HTTP (`INotificationsClient`): Dispatches `POST /api/notifications` directly to `Studium-Notifications` using a typed `HttpClient` with standard resilience.

### 1.3 Pre-Generated Wolverine Codegen in Docker Build
- In Docker production containers, dynamic Roslyn compilation causes cold-start latency and container timeouts.
- `CritterStackDefaults` is configured with `TypeLoadMode.Static` in Production, and `codegen write` pre-generates handler code during Docker multi-stage builds.

### 1.4 Horizontal Scalability & Kubernetes Topology (Rubric Anchor R2/R3)
- Configured `k8s/deployment.yaml` with `replicas: 3` under the unified `studium` namespace.
- Added HorizontalPodAutoscaler targeting 90% CPU utilization (min 3, max 6 replicas).
- Health probes configured at `/health` with cold start delays (liveness 30s, readiness 15s) to allow startup database migrations to finish.

### 1.5 Concurrency Protection: PostgreSQL GiST Exclusion Constraint
- With 3 pod replicas running concurrently, application-level `AnyAsync` validation checks suffer from race conditions.
- Enforced at PostgreSQL engine level using `btree_gist` extension with `tstzrange`:
  ```sql
  ALTER TABLE "Bookings"
  ADD CONSTRAINT "CK_Bookings_NoDoubleBooking"
  EXCLUDE USING gist (
      "RoomId" WITH =,
      tstzrange("StartsAtUtc", "EndsAtUtc") WITH &&
  )
  WHERE ("Status" IN (0, 1));
  ```
- Postgres error code `23P01` (`exclusion_violation`) is intercepted by `DomainExceptionHandler` and mapped to HTTP 409 Conflict.

---

## 2. Project Structure & File Map
- [`Directory.Build.props`](./Directory.Build.props) & [`Directory.Packages.props`](./Directory.Packages.props) — CPM locking dependencies.
- [`Studium-Booking.sln`](./Studium-Booking.sln) — Root solution.
- `Domain/`:
  - `Entities/Bookings/Booking.cs` — Rich domain entity with temporal interval logic, status transitions, and check-in codes.
  - `Entities/Bookings/ReservationTimeSlot.cs` — Value object modeling UTC intervals and duration calculations.
  - `Entities/Waitlist/WaitlistEntry.cs` — Queuing entity for fully booked space promotion.
  - `Entities/Replication/ReplicatedRoom.cs`, `ReplicatedMaintenanceBlock.cs`, `ReplicatedOperatingHours.cs` — Autonomous local read models.
  - `Entities/Users/BlockedUser.cs` — Read model synchronized from `Studium-Users`.
- `Data/`:
  - `BookingDbContext.cs` — EF Core context with GiST exclusion constraint and btree_gist extension.
  - `Configurations/` — Fluent API configurations.
  - `Migrations/` — Auto-applied migrations.
- `Services/`:
  - `Clients/` — `INotificationsClient.cs` and `NotificationsClient.cs` for synchronous HTTP dispatch.
  - `Handlers/` — Wolverine event handlers (`CatalogEventsHandler`, `UserEventsHandler`, `CheckReservationNoShowHandler`).
  - `DTOs/` — Request, response, and event contracts.
- `Api/`:
  - `Controllers/BookingsController.cs`, `WaitlistController.cs` — REST endpoints.
  - `Extensions/` — WebApplicationBuilder extensions (`MessagingExtensions`, `AuthenticationExtensions`, `HttpClientExtensions`).
  - `HostedServices/DatabaseMigrationHostedService.cs` — Startup migration runner.
- `Tests/` — 26 unit tests covering domain rules and mapping.

---

## 3. Recent Changes History
- **2026-11-08 (Initial Implementation & Deployment):**
  - Scaffolded .NET 10 microservice with 3 replicas, GiST exclusion constraints, and 26 unit tests.
- **2026-11-09 (RabbitMQ Migration & SQS Storage Branch):**
  - Migrated messaging to `WolverineFx.RabbitMQ` (`6.63.0`) over AMQP broker (`studium-rabbitmq-service:5672`).
- **2026-11-09 (Synchronous Notification Client Integration):**
  - Implemented typed `INotificationsClient` dispatching alerts synchronously to `POST /api/notifications` in `Studium-Notifications`.
  - Updated `CheckReservationNoShowHandler` and `BookingsController` to synchronously deliver missed deadline and booking alerts.
  - Removed `studium-notifications-queue` publishing from RabbitMQ configuration.
  - Re-generated static Wolverine handlers via `codegen write` and verified 26/26 unit tests.
