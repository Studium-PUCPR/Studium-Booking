# Studium-Booking (.NET Microservice)

Scope: RF9–RF17, RF19, RF20, RNF4, RNF13.

Responsibilities:

Real-Time Availability & Agenda: Serves live room status (UC009), 30-minute agenda slots (UC010), and advanced filtering (UC011).

Transactional Booking Engine: Enforces the max 2-hour duration and max 2 active reservations limit (UC014), prevents overlapping slots with strict database locking and idempotency keys (UC012, UC013, RNF4), and handles cancellations/modifications (UC015) and user history (UC019).

QR Code Check-In & Waitlist: Validates door QR scans within the 15-minute window (UC017) and manages the FIFO waitlist queue (UC016).

SQS Delay Timers & SNS Fan-Out: Uses SQS Delay Queues (or EventBridge Scheduler) for the 15-minute check-in reminder, 15-minute no-show auto-cancellation, and 5-minute waitlist confirmation timeout, publishing lifecycle events (ReservationConfirmed, NoShowCancelled, WaitlistSlotAvailable) to SNS.

Occupancy Analytics: Aggregates check-in hours, peak times, and no-show percentages to generate and export admin reports in PDF/spreadsheet formats (UC020 / W08).
