# Dotnetflow Demo Walkthrough

This walkthrough takes you from zero to a running workflow in under 5 minutes.

## 1. Start the stack

```bash
docker compose up --build -d
```

This spins up:
- **API** on `http://localhost:8081`
- **PostgreSQL** on port 5432
- **React dashboard** on `http://localhost:5173`

Verify everything is healthy:

```bash
curl http://localhost:8081/health
# {"status":"Healthy"}
```

## 2. Create a workflow

Let's build a lead onboarding workflow with three steps:

```bash
curl -s -X POST http://localhost:8081/api/workflows \
  -H "Content-Type: application/json" \
  -d '{
    "name": "onboard-lead",
    "description": "Validate, enrich, and assign new leads",
    "triggerEvent": "lead.created",
    "steps": [
      {"name": "validate", "order": 0},
      {"name": "enrich", "order": 1},
      {"name": "assign", "order": 2}
    ]
  }' | python3 -m json.tool
```

Response:
```json
{
    "id": "a1b2c3d4-...",
    "name": "onboard-lead",
    "description": "Validate, enrich, and assign new leads",
    "isActive": true,
    "steps": [
        {"name": "validate", "order": 0},
        {"name": "enrich", "order": 1},
        {"name": "assign", "order": 2}
    ],
    "createdAt": "2026-03-13T00:00:00Z"
}
```

## 3. Set up a trigger

Create a trigger that starts the workflow when a `lead.created` event fires:

```bash
curl -s -X POST http://localhost:8081/api/triggers \
  -H "Content-Type: application/json" \
  -d '{
    "workflowId": "<workflow-id-from-step-2>",
    "eventType": "lead.created"
  }' | python3 -m json.tool
```

## 4. Fire an event

Now simulate a new lead coming in:

```bash
curl -s -X POST http://localhost:8081/api/events \
  -H "Content-Type: application/json" \
  -d '{
    "type": "lead.created",
    "payload": "{\"email\": \"jane@example.com\", \"source\": \"website\"}"
  }' | python3 -m json.tool
```

## 5. Watch the execution

The background worker picks up the execution automatically. Check its progress:

```bash
curl -s http://localhost:8081/api/executions | python3 -m json.tool
```

```json
[
    {
        "id": "e5f6g7h8-...",
        "workflowId": "a1b2c3d4-...",
        "status": "Completed",
        "currentStepIndex": 3,
        "triggerData": "{\"email\": \"jane@example.com\", \"source\": \"website\"}",
        "startedAt": "2026-03-13T00:00:01Z",
        "completedAt": "2026-03-13T00:00:07Z"
    }
]
```

All three steps ran: validate, enrich, assign. Done.

## 6. Open the dashboard

Navigate to `http://localhost:5173` to see:
- Active workflows and their step definitions
- Running and completed executions
- Real-time status updates

## 7. Try conditional steps

Create a workflow with a condition that only processes high-value leads:

```bash
curl -s -X POST http://localhost:8081/api/workflows \
  -H "Content-Type: application/json" \
  -d '{
    "name": "vip-routing",
    "description": "Route VIP leads to senior sales",
    "steps": [
      {"name": "check-tier", "order": 0, "type": "condition", "conditionExpression": "field:contains:enterprise"},
      {"name": "assign-senior", "order": 1},
      {"name": "notify-manager", "order": 2}
    ]
  }' | python3 -m json.tool
```

Start it manually:

```bash
# This triggers the condition -- "enterprise" is in the payload, so it passes
curl -s -X POST "http://localhost:8081/api/workflows/<workflow-id>/execute" \
  -H "Content-Type: application/json" \
  -d '{"triggerData": "enterprise plan, $50k ARR"}' | python3 -m json.tool
```

The condition step evaluates the payload. If it contains "enterprise", execution continues. Otherwise the step is skipped and the workflow moves to the next one.

## Cleanup

```bash
docker compose down -v
```

That's it. Three endpoints, automatic event matching, background processing, and a dashboard to watch it all happen.
