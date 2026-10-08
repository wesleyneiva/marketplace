# 🛒 Marketplace — ERP de mercadinho (projeto de estudo)

Sistema web de gestão para um mercadinho **fictício**: produtos, estoque com validade, caixa (PDV),
dashboard e, em breve, automações com n8n/Telegram e insights com IA.

| Camada | Tecnologia |
|---|---|
| Front-end | Angular 22 (signals, `httpResource`, rotas com guards) |
| Back-end | ASP.NET Core 10 Web API (C#, Controllers) |
| Banco | PostgreSQL 18 (Docker) + Entity Framework Core |
| Login | ASP.NET Core Identity com cookie HttpOnly · perfis Administrador, Gerente e Caixa |

## Funcionalidades

- **Produtos e categorias** — busca sem acento, ordem alfabética do português, margem calculada.
- **Estoque** — entradas, perdas e ajustes; **lotes de validade com FEFO** (sai primeiro o que vence
  primeiro); histórico completo de "quem mexeu, quando e por quê"; alertas de validade e estoque baixo.
- **Caixa (PDV)** — feito para teclado e leitor de código de barras; venda por peso (KG); desconto
  limitado por perfil; pagamento dividido com troco; cupom; sangria/suprimento; **fechamento cego**.
- **Simulador de clientes** — trabalhador em segundo plano que "abre" o Caixa 9 às 07:00 e vende o dia todo:
  clientes por hora (picos no almoço e às 18h, sábado mais forte, começo do mês), cesta sorteada por
  popularidade e pelo **clima real de Porto Alegre** (calor → bebidas geladas; frio → café; chuva → menos
  gente; fim de semana → churrasco), pagamentos realistas com troco, fornecedor repondo às 07:00 e
  fechamento às 21:00. O clima de cada hora fica na tabela `Clima`, para cruzar com as vendas.
- **Regras importantes**
  - o preço sempre vem do banco (nunca da tela);
  - produto vencido não é vendido;
  - cancelamento devolve a mercadoria ao **mesmo lote** e exige dinheiro na gaveta;
  - concorrência: `xmin` (otimista) nas operações de estoque e `SELECT … FOR UPDATE` (pessimista) na venda.

## Estrutura

```
Marketplace.Api/     API em C# (Controllers, Services, Models, Data, Migrations)
marketplace-web/     telas em Angular
compose.yaml         PostgreSQL (só em 127.0.0.1)
publicar.sh          compila tudo e atualiza o serviço
```

## Rodando

**Produção (serviço systemd `marketplace`)** — `http://oracle-a1:5100`

```bash
./publicar.sh                          # compila Angular + API e reinicia o serviço
journalctl -u marketplace -f           # acompanhar o log
sudo systemctl status marketplace      # situação do serviço
```

A configuração do serviço (com a senha do banco) fica em `/etc/marketplace/marketplace.env`, fora do Git.

**Backup** — todo dia às 03:45 (`backup-marketplace.timer`), `pg_dump` em `~/backup/marketplace-auto/AAAA-MM-DD`
(7 dias guardados, com `resumo.txt` de linhas por tabela). Restaurar:

```bash
sudo systemctl stop marketplace
docker exec -i marketplace-db pg_restore -U marketplace -d marketplace --clean --if-exists < marketplace.dump
sudo systemctl start marketplace
```

**Desenvolvimento** — telas em `http://oracle-a1:4200` com recarga automática

```bash
docker compose up -d                                  # banco (senha em .env — veja .env.example)
cd Marketplace.Api && dotnet run --launch-profile http   # API de desenvolvimento na porta 5200
cd marketplace-web && npx ng serve --host 0.0.0.0        # Angular (repassa /api para a 5200)
```

Em desenvolvimento, a conexão com o banco vem do `dotnet user-secrets` (`ConnectionStrings:Marketplace`).

> Os dados são fictícios: o catálogo inicial (55 produtos, 8 categorias, lotes de validade) é criado
> automaticamente na primeira execução.
