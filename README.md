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
- **Histórico retroativo** — `POST /api/simulador/historico?dias=60` gera o passado com o **clima real de cada hora**
  (Open-Meteo: archive-api + forecast). As vendas ficam com `Origem = Historico` e **não mexem no estoque**
  (origens: `Caixa`, `Simulador`, `Historico`). Feriados nacionais e de Porto Alegre/RS têm horário de domingo.
- **Integração com n8n/Telegram** — `/api/integracao/{resumo,alertas,estoque}` (somente leitura, protegida por
  chave no cabeçalho `X-Api-Key`), cada resposta com uma `mensagem` pronta para o Telegram. Fluxos no n8n:
  resumo do dia às 21:15, alertas às 08:00 e 15:00 (só quando há alerta) e os comandos `/vendas` e `/estoque` no bot.
- **Multi-empresa (SaaS)** — o mesmo sistema atende vários mercados (o de 2 caixas e o de 20) sem um ver o
  dado do outro. Toda tabela tem `EmpresaId`; o `AppDbContext` aplica um **filtro global** (`WHERE EmpresaId = …`)
  em toda consulta e **carimba** a empresa ao gravar (e recusa gravar linha de outra empresa). A empresa vem do
  cookie de login (claim `empresa`), da chave do n8n (`Integracao:EmpresaId`) ou é definida no código nos
  trabalhos em segundo plano. ⚠️ O filtro **não vale para SQL puro** (`SqlQuery`/`FromSql`): ali o
  `"EmpresaId" = {db.EmpresaAtual}` vai escrito à mão. Cada empresa tem o seu **limite de caixas** (o "plano").
  Cliente novo: `./nova-empresa.sh --nome "Mercado do Zé" --subdominio mercadoze --caixas 2 --admin-email … --admin-nome …`
  (cria a empresa, as categorias padrão e o administrador com senha provisória).
- **Importar produtos por planilha** (Produtos → 📥 Importar planilha) — modelo .xlsx com as categorias da empresa
  (`GET /api/produtos/importacao/modelo`), aceita .xlsx ou .csv (Excel brasileiro: `;` e ANSI), reconhece as
  colunas pelo nome. **Prévia** linha a linha sem gravar (`POST …/importacao/previa`: Novo / Atualizar / Erro +
  avisos) e **gravação** (`POST /api/produtos/importacao`) que confere tudo de novo e grava só as linhas sem erro
  num único `SaveChanges` (tudo ou nada). Produto existente (pelo código de barras; sem código, pelo nome) é
  atualizado — célula opcional vazia mantém o valor atual. Estoque inicial (só produto novo) vira movimentação
  "Inventário" + lote de validade.
- **Entrada por XML da NF-e** (Compras → 📄 Entrada por NF-e) — lê o XML 4.00 do fornecedor (`<nfeProc>` ou `<NFe>`,
  sem DTD), calcula o custo real de cada item (produtos − desconto + frete/seguro/outras + IPI + ICMS-ST) e reconhece
  o produto: 1º pelo **vínculo** salvo (fornecedor + código do item → produto + fator da caixa), 2º pelo código de
  barras da unidade (`cEANTrib`), 3º pelo da embalagem (`cEAN`). Fator deduzido da nota (`qTrib`/`qCom`, ex.: 2 CX → 24 UN).
  Conferência sem gravar (`POST /api/compras/notas/conferencia`) e registro numa transação (`POST /api/compras/notas`):
  fornecedor pelo CNPJ (cadastra se não existe), produtos novos (sugestão de categoria pelo NCM e preço pela margem
  média da categoria), entrada no estoque com lote/validade, vínculos atualizados e baixa opcional do pedido de compra.
  A chave de acesso é única por empresa (a mesma nota não entra duas vezes) e o XML fica guardado (`GET …/notas/{id}/xml`).
- **Demonstração pública** — com `Demonstracao__Ativa=true`, o login **do endereço `demo.wnlabs.com.br`** (`Demonstracao__Host`) mostra **"Ver demonstração"**: entra como
  visitante (perfil Gerente, **somente leitura** — qualquer gravação é barrada no servidor) na empresa 1, onde o
  simulador vende. Pela internet (Cloudflare Tunnel, cabeçalho `CF-Connecting-IP`): `/api/integracao` fechado,
  login com senha só no endereço dos clientes (`Publico__HostClientes`, padrão app.wnlabs.com.br) e nunca para usuários da empresa de demonstração, limite de 10 tentativas/min no login e 300 pedidos/min por IP.
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
