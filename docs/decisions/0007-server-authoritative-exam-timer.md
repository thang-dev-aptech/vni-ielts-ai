# ADR-0007 — Server-authoritative exam timing

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Solution architect, security engineer
- **Related:** Requirement G-4 · [T6](../security/threat-model.md) · [`../architecture/key-flows.md`](../architecture/key-flows.md)

## Context

Requirement G-4: *never trust client-side exam timers.*

A client-controlled timer can be paused, reset, or rewound by anyone with browser developer tools or a modified app. Unlimited time on a timed examination invalidates every score the platform produces — which invalidates the product.

The complication is that a purely server-side timer is a poor experience: the learner needs a smooth visible countdown, and round-tripping every second is wasteful and fails offline.

## Options considered

| Option | For | Against |
|---|---|---|
| **Server-authoritative, client displays** | Correct and unforgeable; smooth UI; tolerates brief disconnection | Requires clock reconciliation |
| Client-authoritative | Simplest; works offline | Trivially bypassed. Violates G-4 |
| Server polling every second | Simple to reason about | Wasteful; breaks offline; still requires client rendering between polls |
| Signed client timer token | Cryptographically bound | The client still controls when it *stops*. Solves the wrong half |

The fourth option is worth noting because it is a tempting non-solution: signing the start time does not prevent the client from simply not submitting until later.

## Decision

**Timing authority is the server. The client timer is a display concern.**

1. The server sets `startedAt` from its own clock at session creation and derives `deadlineAt` from the exam's `TimingProfile`.
2. **The client never supplies a time.** Any client-supplied timestamp is ignored.
3. Every API response carries `X-Server-Time`; the client reconciles its display clock against it.
4. The client reconciles on every response and on every resume from background.
5. At zero, the client submits — but the **server** decides whether the submission was in time.
6. Submissions after `deadlineAt` are rejected with `409 SESSION_EXPIRED`.
7. Answer saves after the deadline are rejected.

## Consequences

### Positive
- Timer manipulation is ineffective — the client can lie to itself but not to the server.
- Smooth countdown UI without per-second network traffic.
- Brief disconnection does not break the timer; the client corrects on reconnect.
- Resuming a backgrounded app shows a corrected timer, not a stale one.

### Negative
- Clock reconciliation logic is required in the client.
- A learner may submit believing they were in time and be rejected. Mitigated by displaying the reconciled server time and warning at 5 minutes and 1 minute.

### Risks accepted
- **Network latency at the boundary.** A submission sent at 00:00:00 may arrive after the deadline. `[ASSUMPTION]` A small grace period (a few seconds) absorbs transit time. The grace period is server-side, fixed, and not disclosed to clients.
- Server clock accuracy matters. NTP synchronisation is an operational requirement.

## Notes

**The deadline does not pause when connectivity is lost.** This is a deliberate product decision, not a technical limitation — a pausable timer would be trivially exploitable by disabling the network.

It must be stated in the **pre-exam briefing screen**, before the learner starts, not discovered afterwards. Whatever the new UI looks like, that disclosure is a requirement of this ADR, not a design preference. `[BUSINESS DECISION]` confirm this policy.

The general principle, which applies beyond the timer: **the client renders state; the server owns it.** The same reasoning governs answer scoring (the answer key is never sent to the client) and entitlement checks.
