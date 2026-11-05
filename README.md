# Studium-Booking (Deployed with 3 Replicas)

Stack: .NET + PostgreSQL (db-booking).

Scope: Real-time room status (UC009), reservations and conflict validation (UC012–UC015), waitlist (UC016), QR Code check-in (UC017), and occupancy reports (UC020).   

Rubric R2 & R3 Anchor:

* Runs with 3 Pod replicas in EKS to demonstrate horizontal scaling and load balancing.
* Calls Studium-Catalog and Studium-Users synchronously via REST over Kubernetes ClusterIP DNS when a reservation is requested.
* Publishes ReservationConfirmed, NoShowCancelled, and WaitlistSlotAvailable to the SNS Topic (studium-booking-events) and uses SQS Delay Queues for the 15-minute no-show timer.   
