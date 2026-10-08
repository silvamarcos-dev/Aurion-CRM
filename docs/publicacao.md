# Publicação

Existem duas formas de colocar o CRM na internet.

| | Túnel temporário | Servidor definitivo |
|---|---|---|
| Para quê | Demonstrar agora, mandar um link rápido | Uso real da equipe |
| Onde roda | Neste computador | VPS na nuvem (Docker) |
| Endereço | `https://<aleatório>.trycloudflare.com` (muda a cada vez) | Seu domínio (ex.: `crm.suaempresa.com.br`) |
| Disponível | Enquanto o PC estiver ligado e o script rodando | 24 h |
| Custo | Grátis | A partir de ~US$ 6/mês (VPS 2 GB) + domínio |

---

## 1. Túnel temporário (Cloudflare Quick Tunnel)

```bash
powershell -ExecutionPolicy Bypass -File scripts\publicar-tunel.ps1
```

O script:
1. compila a API (Release) e o site (produção, na pasta `web/.next-prod`, sem afetar o `npm run dev`);
2. sobe a **API somente em `127.0.0.1:5180`**. Ela **não** fica exposta: só o site acessa;
3. sobe o site em `127.0.0.1:3200`;
4. abre o túnel e imprime o endereço público (também salvo em `.publish/url.txt`).

Para encerrar:

```bash
powershell -ExecutionPolicy Bypass -File scripts\parar-tunel.ps1
```

Os logs ficam em `.publish/logs/`. Os links de acesso de visitante ou desenvolvedor precisam ser gerados **acessando o CRM pelo endereço do túnel**, porque o link usa o endereço da página aberta.

> **Atenção:** o túnel usa o banco de **desenvolvimento**, com dados fictícios. O endereço é público: qualquer pessoa com ele vê a tela de login. As contas de demonstração usam a senha forte de `.env.local`, e o login tem bloqueio por tentativas e limite de requisições.

---

## 2. Servidor definitivo (Docker + HTTPS automático)

### Requisitos
- VPS com Ubuntu 24.04, 2 vCPU e 2 GB de RAM. **Prefira um datacenter no Brasil** (LGPD: evita transferência internacional; ver `docs/arquitetura.md`).
- Um domínio com registro **A** apontando para o IP do servidor.
- Firewall liberando apenas as portas 22, 80 e 443.

### Instalação

```bash
# no servidor
curl -fsSL https://get.docker.com | sh
git clone https://github.com/silvamarcos-dev/Aurion-CRM.git
cd Aurion-CRM/deploy
cp .env.example .env
nano .env        # preencha domínio, senhas e o administrador inicial
docker compose up -d --build
docker compose logs -f api   # acompanhe: migrations + criação do administrador
```

Acesse `https://SEU_DOMINIO` e entre com `BOOTSTRAP_ADMIN_EMAIL`. Depois do primeiro login, **apague `BOOTSTRAP_ADMIN_PASSWORD` do `.env`**: ela só é usada quando o banco está vazio.

### O que sobe

```
Internet ──443──► Caddy (HTTPS Let's Encrypt) ──► web (Next.js: UI + BFF) ──► api (.NET) ──► db (PostgreSQL 17)
                    única porta exposta            rede interna 172.28.0.0/16, sem portas publicadas
```

- A API roda em modo produção: aplica as migrations ao iniciar, não cria dados de exemplo e não expõe OpenAPI.
- O banco usa um papel próprio (`crm_app`), sem superusuário.
- A API só confia em `X-Forwarded-For` vindo da rede interna, o que mantém correto o limite de tentativas de login por IP.

### Atualizar

```bash
cd Aurion-CRM && git pull && cd deploy && docker compose up -d --build
```

### Backup (obrigatório antes de dados reais)

```bash
# diário às 3h, guardando 14 dias — adicionar no crontab do servidor
0 3 * * * docker exec crm-loteadora-db-1 pg_dump -U postgres -Fc crm > /root/backups/crm-$(date +\%F).dump && find /root/backups -mtime +14 -delete
```

Copie os backups para fora do servidor (outro provedor ou bucket) e **teste a restauração** periodicamente.

### Antes de usar com clientes reais (checklist)
- [ ] Backups automáticos fora do servidor, com restauração testada.
- [ ] Senha do administrador inicial trocada e removida do `.env`.
- [ ] Datacenter e provedores documentados no inventário LGPD (local de processamento, transferência internacional).
- [ ] MFA para administradores (pendência no roadmap).
- [ ] Monitoramento de disponibilidade (ex.: UptimeRobot em `https://SEU_DOMINIO/login`).
- [ ] Revisão jurídica/DPO das bases legais e do aviso de privacidade.
