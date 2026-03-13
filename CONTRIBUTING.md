# Contributing to Dotnetflow

Thanks for your interest in contributing. Here's how to get involved.

## Getting Started

1. Fork the repo and clone your fork
2. Create a branch from `main` (`git checkout -b fix/your-change`)
3. Install dependencies: `dotnet restore` for the API, `npm install` in `frontend/`
4. Run the project locally with `docker compose up --build -d`

## Making Changes

- Keep PRs focused on a single concern
- Follow existing code style and conventions
- Add or update tests for any behavioral changes
- Run the test suite before opening a PR

## PR Format

Structure your PR description like this:

```
**Problem:** What was broken or missing
**Root cause:** Why it was happening
**Fix:** What you changed
**Testing:** How you verified it works
```

## Development Setup

```bash
# Backend
cd src/DotnetFlow.Api
dotnet build
dotnet test

# Frontend
cd frontend
npm install
npm run dev

# Full stack
docker compose up --build -d
```

## Reporting Bugs

Open an issue with:
- Steps to reproduce
- Expected vs actual behavior
- Environment details (OS, .NET version, browser)

## Code Review

All submissions go through review. We look for:
- Correctness and test coverage
- Clean, readable code
- No unnecessary complexity

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
