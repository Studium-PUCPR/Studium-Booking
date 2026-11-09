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
  - `Studium-Booking.Tests` (xUnit test suite with 26 passing unit tests)

### 1.2 Strict Asynchronous Messaging over RabbitMQ (Golden Path Directive)
- **Problem Solved:** Point-to-point HTTP inter-service calls create runtime coupling, cascading failure vulnerabilities, and violate the platform blueprint established in `Studium Platform Reference.md`.
- **Chosen Solution:** Purely asynchronous messaging over RabbitMQ using Wolverine (`WolverineFx.RabbitMQ`). SQS version preserved on branch `origin/sqs`.
  - Inbound (`studium-booking-events-queue`): Receives `RoomStatusChangedEvent`, `RoomMaintenanceEvent`, `OperatingHoursChangedEvent`, and `ResourceUpdatedEvent` from `Studium-Catalog` to maintain an autonomous replicated read model (`ReplicatedRoom`, `ReplicatedMaintenanceBlock`, `ReplicatedOperatingHours`). Also receives `UserStatusChangedEvent` from `Studium-Users` to maintain local `BlockedUsers`.
  - Outbound (`studium-users-events-queue`): Publishes `BookingIntegrationEvent` (`ReservationCreated`, `ReservationCheckedIn`, `ReservationCancelled`, `NoShowRecorded`) for user audit logs and penalty tracking in `Studium-Users`.
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

### 1.6 Concurrency Protection: PostgreSQL GiST Exclusion Constraint
- With 3 pod replicas running concurrently, application-level `AnyAsync` validation checks suffer from race conditions.
- Enforced at PostgreSQL engine level using `btree_gist` extension with `tstzrange` (`timestamp with time zone` range constructor):
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

### 1.7 Separation of Concerns: No-Show Penalties & Blocked Users Read Model
- User behavioral tracking and penalties belong strictly to `Studium-Users`.
- `Studium-Booking` only reports when a no-show occurs by dispatching `BookingIntegrationEvent(NoShowRecorded)`.
- `Studium-Users` tracks `NoShowCount30Days`, auto-blocks users when the threshold of 5 is met, and broadcasts `UserStatusChangedEvent`.
- `Studium-Booking` consumes `UserStatusChangedEvent` via `UserStatusChangedHandler` and maintains a local `BlockedUser` table for instantaneous reservation rejection without inter-service RPC.

### 1.8 Automated No-Show Evaluation via Wolverine Delayed Messaging
- Upon reservation creation, `BookingsController` schedules a `CheckReservationNoShowMessage` with a 15-minute delay from reservation start time.
- If attendance has not transitioned to `CheckedIn` when the timer fires, the reservation transitions to `NoShow`, frees the room, triggers waitlist promotion, and dispatches `BookingIntegrationEvent(NoShowRecorded)`.

---

## 2. Project Structure & File Map

```
Studium-Booking/
├── Directory.Build.props              # Enforces net10.0, Nullable, ImplicitUsings
├── Directory.Packages.props           # Central Package Management with WolverineFx.RabbitMQ
├── Dockerfile                         # Multi-stage build with codegen write
├── k8s/
│   └── deployment.yaml                # Production deployment (3 replicas, HPA, RabbitMQ config)
├── .github/workflows/
│   └── ci-cd.yml                      # CI/CD pipeline building, testing, pushing to ECR and deploying to EKS
├── Domain/
│   ├── Common/                        # AggregateRoot, ValueObject, DomainException
│   └── Entities/                      # Booking, ReservationTimeSlot, BookingAuditLog, WaitlistEntry, ReplicatedRoom, BlockedUser
├── Data/                              # BookingDbContext, Configurations, Migrations
├── Services/                          # DTOs, Handlers (CatalogEventsHandler, UserStatusChangedHandler, CheckReservationNoShowHandler)
├── Api/                               # Controllers, Extensions (MessagingExtensions for RabbitMQ), HostedServices
└── Tests/                             # 26 unit tests covering domain invariants and workflows
```

---

## 3. Recent Changes History
- **2026-11-09 (Migrated Messaging from AWS SQS to RabbitMQ):**
  - Replaced `WolverineFx.AmazonSqs` with `WolverineFx.RabbitMQ` (v6.63.0) across `Directory.Packages.props`, `Services/Studium-Booking.Services.csproj`, and `Api/Studium-Booking.Api.csproj`.
  - Updated `Api/Extensions/MessagingExtensions.cs` to connect to RabbitMQ broker at `amqp://studium:StudiumRabbitMQ2026!@studium-rabbitmq-service.studium.svc.cluster.local:5672` with `.AutoProvision()`.
  - Updated `k8s/deployment.yaml` with RabbitMQ configuration keys.
  - Re-generated Wolverine static handlers in Release mode; 26 unit tests passing.
  - SQS version preserved on branch `origin/sqs`.
- **2026-11-08 (Enforce Role-based Duration Limits):**
  - Enforced max 2 hours reservation duration for Students and max 5 hours for Professors and Administrators in `BookingsController` and `WaitlistController`.
- **2026-11-08 (GiST Exclusion Constraint, Wolverine No-Show Timer, BlockedUsers Read Model):**
  - Added PostgreSQL `btree_gist` extension and `CK_Bookings_NoDoubleBooking` exclusion constraint with `tstzrange`.
  - Added Wolverine delayed message scheduling for 15-minute no-show evaluation.
  - Transitioned behavioral penalties to `Studium-Users` and synchronized `BlockedUsers`.
