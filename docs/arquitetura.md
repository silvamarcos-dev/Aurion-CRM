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
| BusinessCalendarService (expediente, feriados, exceções, cobertura do Copilot) | `calendar` | ✅ Fase 2 |
| Agenda (jornada, ausências, visitas, disponibilidade) | `scheduling` | ✅ Fase 2 |
| Tarefas / follow-ups, Macros | `tasks`, `macros` | ✅ Fase 2 |
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
| calendar.read / calendar.manage | Org | Team | Own |
| tasks.read / tasks.manage | Org | Team | Own |
| macros.use | Org | Org | Org |
| macros.manage | Org (compartilhadas) | Org (compartilhadas) | Own (pessoais) |

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
- Visitas: `EXCLUDE USING gist (organization_id =, agent_user_id =, tstzrange(starts_at, ends_at) &&) WHERE status = 'Scheduled'`. Dois compromissos agendados do mesmo corretor nunca se sobrepõem, nem com requisições simultâneas (coberto por teste com 6 reservas paralelas).

## Calendário comercial (Fase 2)

- **Toda regra de horário passa pelo `BusinessCalendar`**, uma classe pura com NodaTime e fuso da organização. Nenhum outro código compara horários diretamente.
- **Precedência por data:** exceção (fechado ou expediente especial) > feriado confirmado > horário semanal. Os intervalos são semiabertos `[início, fim)`.
- **Feriados:** os nacionais vêm da BrasilAPI (`IHolidayProvider`) e entram como **pendentes**. A API inclui pontos facultativos, como o Carnaval, então só valem depois da confirmação do administrador. Feriados estaduais e municipais são cadastrados manualmente, com UF e código IBGE configurados na organização. Apenas o ano é enviado ao provedor.
- **Cobertura do Copilot:** `Coverage(instant)` diz se a janela permite atuação sem o corretor (fora do expediente, sábado, domingo, feriado — tudo configurável). Isso define a janela; o nível de autonomia é separado e começa no mínimo.

## Disponibilidade real (Fase 2)

`AvailabilityCalculator` (puro) = jornada do corretor − feriados (se ele não trabalha neles) − dias fechados − ausências − compromissos (com folga de deslocamento) − antecedência mínima − limite diário de visitas.

- **Sem jornada cadastrada, não há disponibilidade.** O sistema nunca presume horário livre.
- **Bloqueios** (sempre recusados): conflito, ausência, horário no passado.
- **Avisos** (exigem confirmação explícita do humano, registrada na auditoria): fora da jornada, sem folga, antecedência, feriado, limite diário.
- O Copilot (Fase 8) usará o mesmo cálculo via `calendar.get_availability` e **nunca** poderá confirmar sem autorização do corretor.

## Tarefas e macros (Fase 2)

- `Lead.NextFollowUpAt` é **derivado** da tarefa aberta mais próxima do lead e recalculado na mesma transação. Concluir um follow-up, ligação ou mensagem registra o último contato.
- **Macros:** template com **lista branca** de variáveis (`{{lead.first_name}}`, `{{agent.name}}`…). Variável desconhecida é recusada ao salvar, e o valor é inserido literalmente, sem reavaliação. A renderização exige que o lead esteja no escopo do usuário, então uma macro não vaza dados. Nada é enviado; o envio entra na Fase 3.

## Acessos externos, sugestões e painel do desenvolvedor

### Modo visitante e modo desenvolvedor
- O administrador (`users.manage`) gera um **link de acesso** em *Configurações › Acessos externos*, com validade de 1 a 30 dias, para o papel **Visitante** ou **Desenvolvedor**.
- Cada link cria um **usuário convidado próprio**: sem senha, sem login por e-mail e com vínculo que expira junto com o link. **Revogar** encerra o vínculo e as sessões imediatamente.
- O token aparece **uma única vez**. O banco guarda só o hash SHA-256. O link usa o **fragmento** (`/acesso#token`), que o navegador não envia ao servidor, então o token não aparece em logs nem em Referer, e a página remove o token da barra de endereço.
- Os papéis convidados **não têm `pii.view`**. A API devolve nome abreviado ("Mariana S."), telefone e e-mail mascarados, e observações e motivos ocultos (inclusive no histórico e na auditoria). Sem `pii.view`, a busca por telefone ou e-mail fica desabilitada, para evitar que alguém "descubra" dados mascarados.
- Os dois papéis são **somente leitura**: não têm capabilities de escrita, e isso é imposto no servidor. O desenvolvedor tem ainda `audit.read`, `dev.tools` e a gestão de sugestões.

### Sugerir mudança
- Botão flutuante em todas as telas (`suggestions.create`, disponível para todos os papéis). Registra tipo, resumo, detalhes, página (sem query string), tamanho da tela e, opcionalmente, o **elemento apontado**: uma descrição legível mais o caminho CSS.
- A descrição do elemento só usa **texto de interface** (botões, títulos, colunas, rótulos). O conteúdo de células e cards não é registrado, e sequências numéricas e e-mails são ocultados.
- Em `/sugestoes`, o autor vê as próprias sugestões (`suggestions.read: Own`). Admin e desenvolvedor veem todas (`Org`) e fazem a triagem (status, prioridade) com comentários (`suggestions.manage`).

### Painel do desenvolvedor (`/desenvolvedor`, `dev.tools`)
- **Sistema:** ambiente, versão (com o commit), runtime, versão do banco e migrations aplicadas/pendentes. Um botão copia o diagnóstico.
- **Fila de eventos:** contagem do outbox (aguardando, em nova tentativa, falha definitiva, processados em 24h) e falhas recentes.
- **Auditoria:** trilha filtrável, com textos livres ocultos.
- **Mapa da API:** gerado das rotas registradas, então está sempre em sincronia com o código.
- **Permissões:** matriz de papéis × capabilities e as permissões da sessão atual.

## Roadmap

| Fase | Escopo | Saída |
|---|---|---|
| **0 — Fundação** ✅ | Solution, auth, RBAC, tenancy, auditoria, outbox, testes de isolamento | Base segura |
| **1 — CRM + Kanban** ✅ | Leads, contatos, pipeline configurável, Kanban, histórico | Uso interno sem IA |
| **2 — Agenda e rotina** ✅ | BusinessCalendarService (NodaTime, feriados), jornada do corretor, visitas, tarefas, macros | Agenda real |
| **2.5 — Acessos externos** ✅ | Modo visitante/desenvolvedor por link, mascaramento de dados pessoais, sugestões de mudança, painel técnico | Compartilhar e colaborar com segurança |
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
