---
title: API Reference
---

All endpoints return JSON. Resources use GUID identifiers.

## Health

```
GET /health
```

Returns `"Healthy"` when the service is running.

## Workflows

### List Workflows

```
GET /api/workflows
```

### Get Workflow

```
GET /api/workflows/{id}
```

### Create Workflow

```
POST /api/workflows
Content-Type: application/json

{
  "name": "order-processing",
  "description": "Process incoming orders through validation, payment, and fulfillment",
  "steps": [
    {
      "name": "validate",
      "type": "Action",
      "handler": "OrderValidator",
      "nextStep": "charge-payment"
    },
    {
      "name": "charge-payment",
      "type": "Action",
      "handler": "PaymentProcessor",
      "nextStep": "fulfill"
    },
    {
      "name": "fulfill",
      "type": "Action",
      "handler": "FulfillmentService"
    }
  ]
}
```

### Update Workflow

```
PUT /api/workflows/{id}
```

### Delete Workflow

```
DELETE /api/workflows/{id}
```

## Executions

### List Executions

```
GET /api/executions
```

Returns all workflow executions with status and timing.

### Get Execution

```
GET /api/executions/{id}
```

### Start Execution

```
POST /api/executions/{workflowId}/start
Content-Type: application/json

{
  "input": { "orderId": "ORD-123", "amount": 99.99 }
}
```

Starts a new execution of the specified workflow with the given input data.

### Cancel Execution

```
POST /api/executions/{id}/cancel
```

## Events

### List Events

```
GET /api/events
```

Returns the event log with filtering support.

### Publish Event

```
POST /api/events
Content-Type: application/json

{
  "type": "order.created",
  "payload": { "orderId": "ORD-123" }
}
```

### List Event Types

```
GET /api/events/types
```

Returns distinct event types seen in the system.
