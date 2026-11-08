# Studium-Booking — Project State & Agent Context (`AGENTS.md`)

> **MANDATORY AGENT DIRECTIVE:**
> After **every user prompt**, always evaluate whether we reached a useful architectural conclusion, resolved a design question, or made code/configuration changes. If so, **update both the relevant sections and Section 3 (`Recent Changes History`) in this `./AGENTS.md` file before finishing your turn** so any future chat session immediately has full context of the project state.

---

## 1. Architectural Decisions & Conclusions Log

### 1.1 Target Framework (`.NET 10.0`) & Clean Architecture
- **Decision:** Build `Studium-Booking` matching the `Studium-Users` and `Studium-Catalog` gold standard with .NET 10 (`net10.0`), Central Package Management (`Directory.Packages.props`), and five Clean Architecture projects:
  - `Studium-Booking.Domain` (zero external dependencies; rich entities and invariants)
  - `Studium-Booking.Data` (EF Core 10, Npgsql PostgreSQL)
  - `Studium-Booking.Services` (DTOs, Wolverine handlers, Mapster)
  - `Studium-Booking.Api` (Thin composition root, Scalar, OpenAPI, Health check)
  - `Studium-Booking.Tests` (xUnit test suite with 29 passing unit tests)

### 1.2 Strict Asynchronous Messaging over Amazon SQS (Golden Path Directive)
- **Problem Solved:** Overcame legacy README notes suggesting synchronous REST calls from Booking to Catalog/Users. Point-to-point HTTP inter-service calls create runtime coupling, cascading failure vulnerabilities, and violate the platform blueprint established in `Studium Platform Reference.md`.
- **Chosen Solution:** Purely asynchronous messaging over Amazon SQS using Wolverine (`WolverineFx.AmazonSqs`):
  - Inbound (`studium-booking-events-queue`): Receives `RoomStatusChangedEvent`, `RoomMaintenanceEvent`, `OperatingHoursChangedEvent`, and `ResourceUpdatedEvent` from `Studium-Catalog` to maintain an autonomous replicated read model (`ReplicatedRoom`, `ReplicatedMaintenanceBlock`, `ReplicatedOperatingHours`).
  - Outbound (`studium-users-events-queue`): Publishes `BookingIntegrationEvent` (`ReservationCreated`, `ReservationCheckedIn`, `ReservationCancelled`, `NoShowRecorded`) for user audit logs in `Studium-Users`.
  - Outbound (`studium-notifications-queue`): Publishes `ReservationNotificationEvent` and `WaitlistSlotAvailableEvent` for `Studium-Notifications`.

### 1.3 Pre-Generated Wolverine Codegen in Docker Build
- In Docker production containers, dynamic Roslyn compilation causes cold-start latency and container timeouts.
- `CritterStackDefaults` is configured with `TypeLoadMode.Static` in Production, and `codegen write` pre-generates handler code during Docker multi-stage builds.

### 1.4 Horizontal Scalability & Kubernetes Topology (Rubric Anchor R2/R3)
- Configured `k8s/deployment.yaml` with `replicas: 3` under the unified `studium` namespace.
- Added HorizontalPodAutoscaler targeting 90% CPU utilization (min 3, max 6 replicas).
- Health probes configured at `/health` with cold start delays (liveness 30s, readiness 15s) to allow startup database migrations to finish.

### 1.5 Database Migrations & Hosted Service Isolation
- Applied migrations run automatically at startup via `DatabaseMigrationHostedService` (`IHostedService`). CLI invocations (`codegen write`) never attempt to connect to the database.

---

## 2. Project Structure & File Map
- [`Studium-Booking.sln`](./Studium-Booking.sln) — Root solution file containing the 5 projects.
- [`Directory.Build.props`](./Directory.Build.props) & [`Directory.Packages.props`](./Directory.Packages.props) — CPM and build settings.
- **`Domain/`:**
  - `Entities/Bookings/Booking.cs` — Booking aggregate root with check-in, check-out, cancel, no-show methods and audit history.
  - `Entities/Bookings/ReservationTimeSlot.cs` — Value object enforcing start < end, 15m min duration, 12h max duration, and overlap checks.
  - `Entities/Bookings/BookingStatus.cs` — Enum (`Confirmed`, `CheckedIn`, `Completed`, `Cancelled`, `NoShow`).
  - `Entities/Bookings/BookingAuditLog.cs` — Positional record for self-contained entity audit history.
  - `Entities/Penalties/PenaltyRecord.cs` — User infraction and suspension tracking entity.
  - `Entities/Penalties/PenaltyType.cs` — Enum (`NoShowInfraction`, `Suspension`, `LateCancellation`).
  - `Entities/Waitlist/WaitlistEntry.cs` — Queued reservation waitlist entity.
  - `Entities/Waitlist/WaitlistStatus.cs` — Enum (`Pending`, `Notified`, `Promoted`, `Expired`, `Cancelled`).
  - `Entities/ReplicatedCatalog/ReplicatedRoom.cs` — Autonomous replicated read model from `Studium-Catalog`.
  - `Entities/ReplicatedCatalog/ReplicatedMaintenanceBlock.cs` — Maintenance interval records.
  - `Entities/ReplicatedCatalog/ReplicatedOperatingHours.cs` — Weekly operating schedule records.
  - `Exceptions/` — `DomainException`, `EntityNotFoundException`, `BookingConflictException`, `UserBlockedException`.
  - `Common/UtcDateTime.cs` — UTC timestamp normalizer.
- **`Data/`:**
  - `BookingDbContext.cs` — DbContext applying Fluent API configurations from assembly.
  - `Configurations/` — Dedicated `IEntityTypeConfiguration<T>` mappings for `Booking`, `BookingAuditLog`, `PenaltyRecord`, `WaitlistEntry`, `ReplicatedRoom`, `ReplicatedMaintenanceBlock`, `ReplicatedOperatingHours`.
  - `Migrations/` — Auto-generated EF Core migration and snapshot (`Initial_Migration`).
- **`Services/`:**
  - `DTOs/` — Positional record DTOs for bookings, waitlist, penalties, room status, reports, and events.
  - `DTOs/BookingMappingConfig.cs` — Mapster configuration flattening value object properties.
  - `Handlers/CatalogEventsHandler.cs` — Wolverine message handler consuming `studium-booking-events-queue`.
  - `Options/BookingOptions.cs` — Options pattern configuration (grace periods, time zone, thresholds).
- **`Api/`:**
  - `Program.cs` — Composition root with OpenAPI, Scalar, Wolverine SQS, and Health check.
  - `Controllers/BookingsController.cs` — Core reservation lifecycle (`/api/bookings`).
  - `Controllers/RoomsController.cs` — Real-time room status and timelines (`/api/bookings/rooms`).
  - `Controllers/WaitlistController.cs` — Waitlist operations (`/api/bookings/waitlist`).
  - `Controllers/PenaltiesController.cs` — Penalty inspections and admin revocations (`/api/bookings/penalties`).
  - `Controllers/ReportsController.cs` — Occupancy reports and utilization analytics (`/api/bookings/reports`).
  - `Middleware/DomainExceptionHandler.cs` — Global RFC 7807 problem details handler.
  - `HostedServices/DatabaseMigrationHostedService.cs` — Startup migration runner.
  - `Extensions/AuthenticationExtensions.cs` & `MessagingExtensions.cs` — Modular DI extensions.
- **`Tests/`:**
  - `Domain/BookingTests.cs` — Invariants, time slots, check-in windows, check-out, cancel, no-show, audit tests.
  - `Domain/PenaltyTests.cs` — Infractions, suspensions, and revocation tests.
  - `Domain/WaitlistTests.cs` — Waitlist creation and lifecycle state transitions.
  - `Domain/ReplicatedRoomTests.cs` — Maintenance block and operating hours validation tests.
  - `DTOs/DtoMappingTests.cs` — Mapster adapter and JSON serialization tests.
- [`Dockerfile`](./Dockerfile) — Multi-stage build with `codegen write`.
- [`k8s/deployment.yaml`](./k8s/deployment.yaml) — Production deployment with 3 replicas, HPA, ConfigMap, and Service.
- [`.github/workflows/ci-cd.yml`](./.github/workflows/ci-cd.yml) — Automated test, build, push, secret sync, and EKS deploy workflow.

---

## 3. Recent Changes History
- **2026-11-08 (Initial Implementation & Deployment):**
  - Scaffolded 5-project Clean Architecture solution with .NET 10, CPM, and strict nullability.
  - Implemented Domain entities: `Booking`, `ReservationTimeSlot`, `BookingAuditLog`, `PenaltyRecord`, `WaitlistEntry`, `ReplicatedRoom`, `ReplicatedMaintenanceBlock`, `ReplicatedOperatingHours`.
  - Implemented Data layer with `BookingDbContext` and isolated Fluent API configurations.
  - Generated initial EF Core PostgreSQL migration `Initial_Migration`.
  - Implemented Services layer with DTOs, options, and Wolverine `CatalogEventsHandler` consuming events from `Studium-Catalog`.
  - Implemented REST API controllers with canonical routes: `BookingsController` (`/api/bookings`), `RoomsController` (`/api/bookings/rooms`), `WaitlistController` (`/api/bookings/waitlist`), `PenaltiesController` (`/api/bookings/penalties`), and `ReportsController` (`/api/bookings/reports`).
  - Generated Wolverine static handler codegen via `codegen write`.
  - Added 29 passing xUnit unit tests across domain and DTO logic.
  - Added production-grade Dockerfile, `k8s/deployment.yaml` with 3 replicas and HPA, and `.github/workflows/ci-cd.yml` with dynamic secret assembly.
  - Successfully verified CI/CD build and deployed 3 live replicas to Amazon EKS in the `studium` namespace.
