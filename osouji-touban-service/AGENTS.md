# AGENTS.md

## Purpose
This file defines the minimum shared context an agent needs to work safely in `osouji-touban-service`.
Treat it as the operational contract for code changes, reviews, testing, and document updates.

## Project Summary
- This repository implements an "Osouji Touban" system that manages cleaning areas, area membership, weekly duty plan generation, rebalancing, publication, closure, user management, notifications, and supporting infrastructure.
- The codebase follows layered architecture:
  - `OsoujiSystem.Domain`: aggregates, value objects, domain services, domain errors, domain events
  - `OsoujiSystem.Application`: use case orchestration, application abstractions, query contracts, event handlers
  - `OsoujiSystem.Infrastructure`: PostgreSQL event store, projections, Redis cache, RabbitMQ messaging, outbox, migrations, workers
  - `OsoujiSystem.WebApi`: Minimal API endpoints, request parsing, HTTP mapping, ETag handling
  - `OsoujiSystem.AppHost`: Aspire local orchestration for PostgreSQL, Redis, RabbitMQ
- Target framework is `net10.0`.
- The default real persistence path is `Infrastructure:PersistenceMode=EventStore`. `Stub` mode exists, but treat it as a fallback/testing convenience, not the primary architecture.

## Source Of Truth
- Prefer newer design documents over older ones when versions conflict.
- Current primary references are:
  - `docs/core-domain-design-v3.md`
  - `docs/core-domain-repository-abstraction-v1.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/readmodel-cqrs-design-v1.md`
  - `docs/user-management-bc-design-v1.md`
  - `docs/facility-management-bc-design-v1.md`
  - `docs/notification-design-v1.md`
  - `docs/infrastructure-architecture-adr-v5.md`
  - `docs/infrastructure-implementation-plan-v1.md`
  - `docs/api-endpoint-design-v1.md`
- If code and docs disagree, resolve in this order:
  1. Explicitly accepted ADR / latest design document
  2. Current implementation and tests
  3. Older draft documents and examples
- Do not blindly follow old examples in docs. Example payloads can lag behind implementation. Verify against code and tests.

## Domain Rules That Must Not Drift
- Core BC is `Duty Assignment`.
- Supporting BCs are `User Management` and `Facility Structure`.
- Main aggregates:
  - `Facility`
  - `CleaningArea`
  - `WeeklyDutyPlan`
  - `ManagedUser`
- Important invariants:
  - A `CleaningArea` belongs to exactly one `FacilityId`.
  - New `CleaningArea` registrations must reference an active Facility via the Facility projection.
  - A `CleaningArea` must always have at least one cleaning spot.
  - Duplicate user assignment inside the same area is forbidden.
  - `EmployeeNumber` is exactly 6 digits. Use the real value object rule in code, not stale examples.
  - `WeeklyDutyPlan` must keep unique `Spot -> User` assignments for an area/week.
  - `PlanRevision` increases monotonically on recalculation.
  - Closed weekly plans cannot be recalculated.
  - Week rule changes apply from a future week, not the current week.
- Fairness and assignment behavior comes from `DutyAssignmentEngine` and `FairnessPolicy`. Do not reimplement assignment logic inside endpoints or repositories.

## Layering Rules
- Dependency direction is `WebApi -> Application -> Domain`.
- `Infrastructure` implements `Domain` and `Application` contracts, but domain logic must stay out of infrastructure.
- Keep responsibilities strict:
  - Domain: business rules and invariants only
  - Application: orchestration, transactions, repository calls, event dispatch
  - WebApi: HTTP parsing, validation, response mapping, ETag/If-Match handling
  - Infrastructure: storage, cache, messaging, background workers, integration plumbing
- Read paths must use Application query abstractions and projection-backed repositories. Do not add new query behavior by reconstructing aggregates for GET endpoints unless you are intentionally fixing an existing gap.
- Write behavior must go through Application use cases. Do not call aggregate mutation methods directly from WebApi.

## API Conventions
- Base path is `/api/v1`.
- Existing endpoint groups:
  - `/facilities`
  - `/cleaning-areas`
  - `/weekly-duty-plans`
  - `/users`
  - `/internal`
- Use `camelCase` JSON properties.
- Use UUID strings for IDs.
- `weekId` format is `YYYY-Www`.
- For write endpoints on existing aggregates, preserve optimistic concurrency:
  - `ETag` on reads
  - `If-Match` on writes
- Keep error mapping aligned with Application/Domain semantics:
  - validation/input errors -> `400`
  - not found -> `404`
  - business conflicts / concurrency / duplicate -> `409`
- Reuse `ApiRequestParsing` and `ApiHttpResults` rather than inventing new per-endpoint parsing or error shapes.

## Persistence, Messaging, And Cache Rules
- Event store is in PostgreSQL.
- Redis is used for:
  - aggregate cache fallback acceleration
  - read model cache
- RabbitMQ is used for outbox delivery, notifications, and integration events.
- The accepted read-model cache design is ADR v5:
  - detail cache uses `pointer invalidate + read-through refill`
  - do not introduce detail negative cache unless the ADR is intentionally changed
  - projector invalidates pointers and bumps list namespaces; it does not rebuild detail payloads
- Redis failure should degrade to PostgreSQL fallback, not break the main read path.
- Messaging and notification flows are at-least-once. Preserve idempotency behavior.
- Use the existing connection string names:
  - `ConnectionStrings:osouji-db`
  - `ConnectionStrings:osouji-redis`
  - `ConnectionStrings:osouji-rabbitmq`

## Background Workers And Operational Context
- Infrastructure registers hosted services for migrations, RabbitMQ topology, projections, metrics, cache recovery, outbox publishing, notifications, integration consumer, and retention purge.
- Integration tests intentionally remove hosted services and drive projection manually. Do not "fix" that isolation behavior unless the tests and fixture are updated together.
- Local orchestration paths:
  - Aspire AppHost: `src/OsoujiSystem.AppHost/AppHost.cs`
  - Docker Compose: `docker-compose.yml`

## Testing And Verification
- For code changes in this .NET repository, always run the full .NET pipeline after edits:
  1. `dotnet restore`
  2. `dotnet build`
  3. `dotnet test`
- Run from repository root.
- Do not narrow `dotnet test` to one project as a final verification step unless the user explicitly asks for a scoped run.
- Test layout:
  - `tests/OsoujiSystem.Domain.Tests`: domain invariants and domain services
  - `tests/OsoujiSystem.Infrastructure.Tests`: infrastructure behavior
  - `tests/OsoujiSystem.WebApi.Tests`: API/integration coverage with Testcontainers
- When changing API contracts, update or add integration tests first-class, not only unit tests.
- When changing domain invariants or fairness behavior, prioritize domain tests around `CleaningArea`, `WeeklyDutyPlan`, `ManagedUser`, and `DutyAssignmentEngine`.

## Editing Rules For Agents
- Make the smallest coherent change that preserves architecture.
- Prefer extending existing abstractions and helpers over introducing parallel patterns.
- Keep naming aligned with existing ubiquitous language:
  - `CleaningArea`, `CleaningSpot`, `AreaMember`
  - `WeeklyDutyPlan`, `DutyAssignment`, `OffDuty`
  - `ManagedUser`, `AuthIdentityLink`
- Preserve strongly typed IDs and value objects. Do not replace them with raw primitives deeper in the stack.
- Preserve `Result<T, DomainError>` patterns in domain code and `ApplicationResult<T>` patterns in application code.
- Preserve optimistic concurrency contracts in repositories and APIs.
- Preserve event-driven side effects already modeled by domain/application events.
- Do not move business rules into controllers/endpoints, SQL, cache code, or message handlers.
- Do not add new dependencies or infrastructure patterns when an existing package or mechanism already covers the need.

## Documentation Update Rules
- If a change alters behavior, invariants, endpoint contracts, or operational design, update the relevant document in `docs/`.
- Prefer updating the latest versioned doc instead of creating `v2`/`v6` casually. Add a new version only when the repository is already using versioned supersession for that topic and the change is substantial.
- If you discover a mismatch between docs and implementation, either:
  - align code to the accepted design, or
  - update the document to reflect the accepted implementation
- Do not leave silent drift.

## Practical Entry Points
- Solution file: `OsoujiSystem.slnx`
- API startup: `src/OsoujiSystem.WebApi/Program.cs`
- Endpoint registration: `src/OsoujiSystem.WebApi/Endpoints/OsoujiApiEndpointRouteBuilderExtensions.cs`
- Application DI: `src/OsoujiSystem.Application/DependencyInjection/ServiceCollectionExtensions.cs`
- Infrastructure DI: `src/OsoujiSystem.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Main domain models:
  - `src/OsoujiSystem.Domain/Entities/CleaningAreas/CleaningArea.cs`
  - `src/OsoujiSystem.Domain/Entities/WeeklyDutyPlans/WeeklyDutyPlan.cs`
  - `src/OsoujiSystem.Domain/Entities/UserManagement/ManagedUser.cs`

## Common Pitfalls
- Do not trust outdated sample values such as non-6-digit employee numbers in older docs.
- Do not bypass projections by adding aggregate-backed GET behavior as a convenience.
- Do not break `If-Match` / `ETag` semantics on mutation endpoints.
- Do not treat background worker removal in integration tests as accidental.
- Do not couple `User Management` to a specific IdP SDK type.
- Do not weaken idempotency in RabbitMQ consumers or notification delivery.

## When Unsure
- Check the latest relevant document and the corresponding tests.
- If still ambiguous, follow existing implementation patterns in the same layer before inventing a new approach.
