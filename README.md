# Approval Workflow API

![ci](https://github.com/lilsnog/approval-workflow-api/actions/workflows/ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![C#](https://img.shields.io/badge/C%23-12-239120)

A multi-stage approval engine built with **ASP.NET Core 8 minimal APIs**. It handles expense claims, purchase orders and any other process that needs an approval chain, with **amount-based routing**, **segregation of duties**, **SLA escalation** and a **full audit trail**.

Approval chains are defined in configuration, so changing who signs off on what is a config change, not a deployment.

## Features

| | |
|---|---|
| **Config-driven chains** | Each workflow is an ordered list of stages (role + SLA). Stages can carry a `MinimumAmount`, so small requests take a shorter path. |
| **Segregation of duties** | Only the stage's role can decide. Requesters can't approve their own request. One person can't approve two stages of the same request. |
| **SLA escalation** | A `BackgroundService` checks pending stages on a timer and escalates each overdue stage exactly once, notifying the escalation role. |
| **Audit trail** | Every submit, approval, rejection, escalation and withdrawal is recorded with actor, stage, timestamp and comment. |
| **Concurrency-safe** | Decisions run under a per-request lock, so two approvers clicking at the same moment can't both advance a stage (covered by a test). |
| **Clean errors** | Domain errors map to RFC 7807 `problem+json` responses (401 / 403 / 404 / 422). |
| **Testable by design** | Time is injected through .NET 8's `TimeProvider`, and persistence and notifications sit behind interfaces. Tests move the clock forward instead of sleeping. |

## Architecture

```
Endpoints/        thin minimal-API handlers: HTTP in, DTO out
Application/      ApprovalService (use cases) + interfaces for storage, workflows, notifications
Domain/           ApprovalRequest aggregate + WorkflowDefinition; all business rules live here
Infrastructure/   in-memory repository, config workflow catalog, API-key auth,
                  logging notifier, SLA escalation background service
```

The domain has no dependency on ASP.NET. To persist to SQL Server, PostgreSQL or MySQL, implement `IRequestRepository` with EF Core or Dapper. To send real emails, Teams messages or webhooks, implement `INotifier`.

## Run it

```bash
dotnet run --project src/ApprovalWorkflow.Api --urls http://localhost:5077
```

Demo users (API keys live in `appsettings.json`; in production, swap API-key auth for JWT / OpenID Connect):

| Key | User | Role |
|---|---|---|
| `demo-ada-requester` | ada | staff |
| `demo-bayo-manager` | bayo | manager |
| `demo-chidi-finance` | chidi | finance |
| `demo-dami-cfo` | dami | cfo |

Open `requests.http` in VS Code (REST Client) or Rider to walk through a full flow, or use curl:

```bash
# Ada submits ₦750,000: route is Line Manager -> Finance (CFO stage starts at ₦5m)
curl -s -H "X-Api-Key: demo-ada-requester" -H "Content-Type: application/json" \
  -d '{"workflow":"expense","title":"Team offsite venue","amount":750000,"currency":"NGN"}' \
  http://localhost:5077/api/requests/

# Finance tries to jump the queue
curl -s -H "X-Api-Key: demo-chidi-finance" -H "Content-Type: application/json" -d '{}' \
  http://localhost:5077/api/requests/{id}/approve
# {"title":"Not allowed","status":403,"detail":"Stage 'Line Manager' must be decided by role 'manager'."}
```

## Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/workflows` | Configured approval chains |
| `POST` | `/api/requests` | Submit a request |
| `GET` | `/api/requests?status=&mine=&awaitingMe=` | List / filter (e.g. "what is waiting for my role") |
| `GET` | `/api/requests/{id}` | Request with current stage, due time and `overdue` flag |
| `POST` | `/api/requests/{id}/approve` | Approve the current stage |
| `POST` | `/api/requests/{id}/reject` | Reject (comment required) |
| `POST` | `/api/requests/{id}/withdraw` | Requester withdraws |
| `GET` | `/api/requests/{id}/audit` | Full audit trail |
| `GET` | `/health` | Liveness |

## Configuring a workflow

```json
"Workflows": {
  "expense": [
    { "Name": "Line Manager", "Role": "manager", "SlaHours": 24, "EscalateToRole": "head-of-dept" },
    { "Name": "Finance",      "Role": "finance", "SlaHours": 24, "EscalateToRole": "cfo" },
    { "Name": "CFO",          "Role": "cfo",     "SlaHours": 48, "MinimumAmount": 5000000 }
  ]
}
```

## Tests

```bash
dotnet test
```

The xUnit tests cover routing by amount, stage-by-stage approval, role checks, self-approval and double-approval prevention, rejection rules, withdrawal, SLA escalation (exactly once per stage, reset on advance), notifier events, and concurrent approvals racing on the same stage.

## License

MIT
