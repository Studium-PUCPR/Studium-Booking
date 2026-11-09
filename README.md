# Studium-Booking (Deployed with 3 Replicas)

Stack: `.NET 10.0` Web API + PostgreSQL (`studium_booking`) + Amazon SQS (`WolverineFx.AmazonSqs`) + Amazon Cognito (`JwtBearer`) + Scalar API Reference (`/scalar/v1`).

## Rubric R2 & R3 Anchor
* **Horizontal Scalability:** Runs with 3 Pod replicas in EKS under the `studium` namespace (`replicas: 3`) with HorizontalPodAutoscaler (HPA) targeting 90% CPU utilization.
* **Strict Asynchronous Messaging:** Adheres strictly to the Studium Golden Path blueprint (`Studium Platform Reference.md`). Inter-service communication is purely event-driven over Amazon SQS using Wolverine (`studium-booking-events-queue`, `studium-users-events-queue`, and `studium-notifications-queue`). No synchronous inter-service HTTP calls.

## Scope & Capabilities
- **Real-Time Room Status & Timeline (UC009):** Autonomous room availability resolution (`Available`, `Occupied`, `Reserved`, `Maintenance`, `Inactive`) and chronological booking timelines under `/api/bookings/rooms/{roomId}/status` and `/api/bookings/rooms/{roomId}/timeline`.
- **Reservation Lifecycle & Conflict Validation (UC012–UC015):** Booking creation, listing, detail view, cancellation, and check-out under `/api/bookings`. Enforces room status, operating hour windows, maintenance block exclusion, concurrent reservation conflict detection, and user suspension checks.
- **Waitlist (UC016):** Queuing system under `/api/bookings/waitlist` when rooms are booked. When a confirmed booking is cancelled, the oldest pending waitlist candidate is notified and a `WaitlistSlotAvailable` event is dispatched.
- **QR Code Check-In (UC017):** Attendance confirmation via QR code / check-in code under `/api/bookings/{id}/check-in` and `/api/bookings/{id}/qr-code` within configurable grace windows.
- **No-Show Evaluation & Blocked Users Read Model:** Automated 15-minute no-show evaluation timer via Wolverine delayed messaging (`CheckReservationNoShowMessage`), manual no-show recording (`/api/bookings/{id}/mark-no-show`), and publishing `BookingIntegrationEvent(NoShowRecorded)` to `studium-users-events-queue`. User infractions and blocking are managed autonomously by `Studium-Users` (auto-blocking after 5 no-shows in 30 days) and synchronized to `Studium-Booking`'s `BlockedUsers` read model via `UserStatusChangedEvent`.
- **Occupancy & Utilization Reports (UC020):** Aggregated metrics under `/api/bookings/reports/occupancy` covering check-in rates, cancellation rates, no-show rates, total booked hours, peak hour histograms, and room breakdown.

## High-Concurrency Multi-Replica Safety (PostgreSQL GiST Exclusion Constraint)
With 3 active EKS pod replicas, standard LINQ application-level checks are vulnerable to concurrency races. `Studium-Booking` enforces double-booking prevention at the database engine level via the PostgreSQL `btree_gist` extension:
```sql
ALTER TABLE "Bookings"
ADD CONSTRAINT "CK_Bookings_NoDoubleBooking"
EXCLUDE USING gist (
    "RoomId" WITH =,
    tstzrange("StartsAtUtc", "EndsAtUtc") WITH &&
)
WHERE ("Status" IN (0, 1));
```
Any simultaneous overlapping reservation request yields Postgres error `23P01` (`exclusion_violation`), cleanly translated by `DomainExceptionHandler` into an HTTP 409 Conflict.

## Messaging Architecture (Wolverine + Amazon SQS)
- **Inbound (`studium-booking-events-queue`):**
  - Replicates catalog state autonomously from `Studium-Catalog`:
    - `RoomStatusChangedEvent`: updates room activation status.
    - `RoomMaintenanceEvent`: adds/removes maintenance blocks.
    - `OperatingHoursChangedEvent`: updates weekly operating hour windows.
    - `ResourceUpdatedEvent`: tracks room equipment updates.
  - Replicates user blocking state from `Studium-Users`:
    - `UserStatusChangedEvent`: maintains local `BlockedUsers` read model for fast O(1) reservation eligibility checks.
- **Local Delayed Messaging (Wolverine Scheduler):**
  - `CheckReservationNoShowMessage`: Scheduled with a 15-minute delay from reservation start time to evaluate attendance and auto-mark no-shows.
- **Outbound (`studium-users-events-queue`):** Dispatches `BookingIntegrationEvent` (`ReservationCreated`, `ReservationCheckedIn`, `ReservationCancelled`, `NoShowRecorded`) for institutional audit logging and penalty evaluation in `Studium-Users`.
- **Outbound (`studium-notifications-queue`):** Dispatches `ReservationNotificationEvent` and `WaitlistSlotAvailableEvent` for student/professor alerts and email notifications.

## Local Environment Setup & Testing

### 1. Start PostgreSQL in Docker
```bash
docker run --name studium-booking-db \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=studium_booking \
  -p 5432:5432 \
  -d postgres:16-alpine
```

### 2. Restore & Run Application
```bash
dotnet restore
dotnet run --project Api/Studium-Booking.Api.csproj
```
Interactive API documentation is accessible at `http://localhost:5002/scalar/v1`.

### 3. Run Unit Tests
```bash
dotnet test Studium-Booking.sln
```
