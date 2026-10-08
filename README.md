# CRM Loteadora

CRM comercial para loteadora: leads, Kanban, atendimento, automações e (fases futuras) Copilot de IA supervisionado.

- **Backend:** .NET 10 · ASP.NET Core (minimal APIs) · EF Core 10 · PostgreSQL 17
- **Frontend:** Next.js 15 (App Router) · TypeScript · Tailwind v4 · TanStack Query · dnd-kit
- **Arquitetura:** monólito modular + BFF. Detalhes em [docs/arquitetura.md](docs/arquitetura.md).

## Estrutura

```
backend/
  src/Loteadora.Crm.SharedKernel   # Entity, exceptions, capabilities, ICurrentActor, ITenantContext
  src/Loteadora.Crm.Modules        # Iam, Crm, Pipeline, Audit, Outbox, Persistence (um schema por módulo)
  src/Loteadora.Crm.Api            # host HTTP: auth JWT, rate limiting, ProblemDetails, endpoints /api/v1
  tests/Loteadora.Crm.Tests        # unitários + integração com PostgreSQL real (isolamento de tenant, RBAC, auditoria)
web/                               # Next.js: telas + BFF (/api/auth/*, /api/bff/*)
```

## Rodando localmente

Pré-requisitos: .NET SDK 10, Node 20+, PostgreSQL 17.

Segredos locais **não** ficam no repositório. O backend lê via `dotnet user-secrets`:

```bash
cd backend/src/Loteadora.Crm.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=crm_dev;Username=crm_app;Password=<senha>"
dotnet user-secrets set "Auth:SigningKey" "<chave aleatória com 32+ caracteres>"
dotnet user-secrets set "Seed:Password" "<senha inicial dos usuários de demonstração>"
```

API (aplica migrations e cria dados fictícios em Development):

```bash
dotnet run --project backend/src/Loteadora.Crm.Api --urls http://localhost:5180
```

Frontend:

```bash
cd web && npm install && npx next dev --port 3100
```

Acesse http://localhost:3100. Usuários de demonstração (senha = `Seed:Password`):

| E-mail | Papel | Escopo |
|---|---|---|
| admin@demo.local | Administrador | organização |
| gestor@demo.local | Gestor (Time Norte) | time |
| corretor1@demo.local | Corretor (Time Norte) | próprios leads |
| corretor2@demo.local | Corretor (Time Sul) | próprios leads |
| admin@outra.local | Administrador de **outra** organização | — (para testar isolamento) |

## Publicação

- **Demonstração rápida (link temporário a partir deste PC):** `powershell -ExecutionPolicy Bypass -File scripts\publicar-tunel.ps1`
- **Servidor definitivo (Docker + HTTPS automático):** pasta `deploy/`

Detalhes, segurança e checklist em [docs/publicacao.md](docs/publicacao.md).

## Testes

```bash
dotnet test backend
```

Os testes de integração criam e removem um banco `crm_test_*` por execução. A conexão base vem de
`CRM_TEST_CONNECTION` ou de `CRM_DB_CONNECTION` no arquivo `.env.local` da raiz (não versionado).
O usuário do banco precisa da permissão `CREATEDB`.

## Migrations

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Nome> -p src/Loteadora.Crm.Modules -s src/Loteadora.Crm.Api -o Persistence/Migrations
```
