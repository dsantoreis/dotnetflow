# Building a Workflow Engine in .NET 10 That Doesn't Need a PhD to Operate

Enterprise workflow engines have a reputation problem. They're powerful, configurable, and completely impenetrable to anyone who didn't spend six months in vendor training.

I built Dotnetflow because most teams don't need Camunda or Temporal. They need event-driven task routing with a UI their ops team can actually use.

## Where enterprise workflow tools fail small teams

Most workflow engines assume you have a dedicated platform team. You define workflows in XML or a proprietary DSL, deploy them to a workflow server, configure persistence backends, set up monitoring, and pray the version upgrade doesn't break your running instances.

For a team of 5-15 engineers, that's months of setup before you route your first task.

## What Dotnetflow does instead

```bash
git clone https://github.com/dsantoreis/dotnetflow
cd dotnetflow
docker compose up --build
```

Workflows are defined in JSON. Tasks route based on events. The React dashboard shows what's running, what's stuck, and what failed. No XML. No DSL. No certification required.

```csharp
// Define a workflow
POST /api/v1/workflows
{
  "name": "customer-onboarding",
  "steps": [
    { "id": "verify-email", "type": "automated", "handler": "email-verify" },
    { "id": "review-docs", "type": "manual", "assignee": "ops-team" },
    { "id": "activate", "type": "automated", "handler": "account-activate" }
  ]
}
```

Each step can be automated (handler function) or manual (assigned to a team via the dashboard). Events trigger transitions. The engine handles retries, timeouts, and dead-letter queuing.

## Why .NET 10

.NET 10 is the best runtime nobody talks about in the AI/startup world:

- **Performance.** ASP.NET Core consistently tops TechEmpower benchmarks. Dotnetflow handles 3,000+ workflow events/second on modest hardware.
- **Type safety.** C# catches entire categories of bugs at compile time that dynamic languages discover in production.
- **Enterprise adoption.** Banks, insurance, healthcare, government. If you're selling to enterprises in Europe, .NET skills open doors that Python doesn't.
- **Cross-platform.** Runs on Linux, macOS, Windows. Docker images are small and fast to start.

## The Zurich factor

Switzerland's enterprise market runs on .NET more than most developers realize. UBS, Credit Suisse (now UBS), Swiss Re, Zurich Insurance, SBB. Having .NET in your portfolio signals you can work in their stack.

Full docs at [dsantoreis.github.io/dotnetflow](https://dsantoreis.github.io/dotnetflow/).

---

*Daniel Reis, Zurich. Building production AI infrastructure. [github.com/dsantoreis](https://github.com/dsantoreis)*
