# Arquitetura — CRM Loteadora

Documento vivo. Registra decisões tomadas e o que ainda está planejado.

## Princípios

1. **O backend é a única fonte de autorização.** Capabilities no frontend servem só para esconder botões.
2. **O LLM nunca é fonte de verdade nem de autorização** (fases do Copilot): ele propõe e o backend decide.
3. **Isolamento de tenant em camadas:** global query filter (falha fechada) → bloqueio de gravação cross-tenant no `SaveChanges` → (planejado) Row Level Security no PostgreSQL.
4. **Recurso fora do escopo = 404**, nunca 403, para não revelar a existência de IDs (proteção contra IDOR).
5. **Auditoria na mesma transação** da operação, append-only e encadeada por hash.
6. **Eventos de domínio via transactional outbox.** Automações, notificações e Copilot reagem de forma assíncrona e idempotente.
7. **Privacy by default:** menor privilégio, logs sem dados pessoais, autonomia da IA começando em zero.

## Visão geral

```
Navegador ──cookie httpOnly──► Next.js (UI + BFF) ──Bearer JWT──► ASP.NET Core API ──► PostgreSQL
                                                                     │
                                                         OutboxDispatcher (background)
                                                                     │
                                         handlers: automações · notificações · Copilot (fases futuras)
```

- **BFF:** tokens nunca chegam ao JavaScript do navegador. Requisições que alteram estado exigem o header `x-crm-request` e Origin igual ao host (CSRF). O refresh usa single-flight.
- **Sessões:** access token de 10 min com refresh token rotativo de 7 dias (só o hash fica no banco). Se um token já rotacionado for reutilizado fora da janela de tolerância, a família inteira é revogada. Cada requisição verifica se a sessão está ativa e recarrega as capabilities do banco, então a revogação vale na hora.
- **Login:** mensagem genérica, custo de hash equivalente para e-mail inexistente, bloqueio de 15 min após 5 falhas e rate limit por IP.

## Módulos (monólito modular)

| Módulo | Schema | Status |
|---|---|---|
| IAM (orgs, times, usuários, papéis, capabilities, sessões) | `iam` | ✅ Fase 1 |
| CRM (contatos, leads) | `crm` | ✅ Fase 1 |
| Pipeline (pipelines e etapas configuráveis) | `pipeline` | ✅ Fase 1 |
| Auditoria | `audit` | ✅ Fase 1 |
| Outbox / mensageria | `messaging` | ✅ Fase 1 |
| Agenda + BusinessCalendarService | `scheduling`, `calendar` | ⏳ Fase 2 |
| Tarefas / follow-ups, Macros | `tasks`, `macros` | ⏳ Fase 2 |
| Inbox + WhatsApp (provider Meta) | `inbox`, `channels` | ⏳ Fase 3 |
| Automações (trigger → condições → ações) | `automation` | ⏳ Fase 4 |
| Call Manager | `telephony` | ⏳ Fase 5 |
| Copilot (orchestrator, policy engine, tools, conhecimento) | `copilot`, `knowledge` | ⏳ Fases 6–9 |
| Privacidade / LGPD (inventário, DSR, retenção, incidentes) | `privacy` | ⏳ transversal |

Os módulos ficam no mesmo assembly por enquanto, separados por namespace e schema. Quando o número de módulos crescer, a separação em assemblies (`*.Contracts`) e os testes de arquitetura (NetArchTest) entram junto.

## RBAC

Capabilities com escopo `Own < Team < Org` ficam em `SharedKernel/Security/Capabilities.cs`. Os papéis padrão (`SystemRoles`) são:

| Capability | Administrador | Gestor | Corretor |
|---|---|---|---|
| leads.read / create / update | Org | Team | Own |
| leads.assign | Org | Team | — |
| pipeline.move | Org | Team | Own |
| pipeline.configure | Org | — | — |
| audit.read | Org | Team | — |

O Copilot (fases futuras) age como `ActorKind.Copilot` em nome de um corretor. Suas permissões efetivas são `capabilities(corretor) ∩ nível de autonomia ∩ política da organização`. Ele nunca altera as próprias permissões.

## Auditoria

- Tabela `audit.audit_events`: sequência por organização + `previous_hash` + `hash` (SHA-256).
- O encadeamento é serializado com `pg_advisory_xact_lock` por organização.
- Um trigger bloqueia `UPDATE`, `DELETE` e `TRUNCATE`. O expurgo por retenção será feito por um papel de banco dedicado.
- A coluna `data` é do tipo `json` (não `jsonb`) para preservar o texto exato que entrou no hash. O timestamp é truncado em microssegundos pelo mesmo motivo.
- **Sem dados pessoais em claro:** atualizações registram só os nomes dos campos alterados.
- Leitura de lead gera `lead.viewed` (auditoria de acesso exigida pela LGPD), que fica fora da timeline exibida ao usuário.

## Concorrência

- O lead usa `xmin` do PostgreSQL como versão. O cliente envia `expectedVersion` e a API responde 409 se o lead foi alterado por outra pessoa.
- Planejado para o takeover do Copilot: *fencing token* (`ownership_epoch`) + `SELECT … FOR UPDATE` na conversa.
- Planejado para visitas: `EXCLUDE USING gist (agent_id WITH =, during WITH &&)` para impedir sobreposição de horários.

## Roadmap

| Fase | Escopo | Saída |
|---|---|---|
| **0 — Fundação** ✅ | Solution, auth, RBAC, tenancy, auditoria, outbox, testes de isolamento | Base segura |
| **1 — CRM + Kanban** ✅ | Leads, contatos, pipeline configurável, Kanban, histórico | Uso interno sem IA |
| 2 — Agenda e rotina | BusinessCalendarService (NodaTime, feriados), jornada do corretor, visitas, tarefas, macros | Agenda real |
| 3 — Atendimento humano | Inbox, provider WhatsApp (webhook HMAC, idempotência, janela de 24h, templates) | Conversas no CRM |
| 4 — Automações | Motor de regras, gatilhos temporais em dias úteis, painel de execuções | Rotina automatizada |
| 5 — Call Manager | Filas, campanhas, scripts, resultados configuráveis | Cold call |
| 6 — Copilot N0/N1 | Base de conhecimento (pgvector), RAG com citação, sugestões aprovadas pelo corretor | IA observando e sugerindo |
| 7 — Treinamento | Pipeline de ingestão (PDF/áudio/texto), PII, revisão humana, perfis de estilo | Personalização controlada |
| 8 — Copilot N2 | Policy Engine, aprovações, regra dos 15 min, agendamento com autorização | IA supervisionada |
| 9 — Copilot N3 (piloto) | Atendimento automático dentro da cobertura, kill switch, métricas de qualidade | Autonomia controlada |
| Transversal — LGPD | Inventário, bases legais, DSR, retenção, exportações, incidentes, painel de privacidade | Conformidade operacional |

## Pendências conhecidas

- Row Level Security no PostgreSQL (terceira camada de isolamento).
- Header `Idempotency-Key` nos POSTs.
- MFA (TOTP) para administradores.
- OpenTelemetry e redação de PII nos logs.
- O rate limit por IP do login depende de X-Forwarded-For enviado pelo BFF. Em produção, configurar `KnownProxies` e a origem confiável do IP no ingress.
- Testes de arquitetura (dependências entre módulos).
- Papéis de banco separados: migrator, runtime e auditoria/retenção.
