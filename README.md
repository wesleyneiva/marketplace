# Marcus — gestão inteligente para o seu negócio

Sistema web de gestão (ERP) para mercados e mercadinhos, oferecido como serviço (SaaS) pela WN Labs: produtos,
estoque com validade, caixa (PDV), compras, financeiro com DRE, relatórios com clima e insights com IA.
O nome é uma homenagem a Marco Aurélio, o imperador filósofo: decidir com calma, com informação e com sabedoria.

Clientes entram por **https://app.wnlabs.com.br**; a vitrine pública fica em **https://demo.wnlabs.com.br**.
(Pastas, serviço e banco ainda usam o nome técnico antigo, `marketplace`.)

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
- **Vendas automáticas (simulador)** — por empresa (`Empresa.VendasAutomaticas`: Desligado, SoVendas, Completo), no
  horário de funcionamento de cada loja; o robô abre o Caixa 9 (fora do plano) e vende o dia todo:
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
- **Contas a pagar** (💸 no menu) — as duplicatas da NF-e (`<cobr><dup>`) viram uma conta por parcela; nota sem
  parcelas vira uma conta "à vista" (que pode entrar já paga). Contas lançadas à mão (aluguel, luz) com repetição
  mensal; pagar (data, valor com juros/desconto, forma), desfazer, editar e excluir só em aberto. Resumo no topo e
  no dashboard; vencidas, de hoje e de amanhã entram no `/api/integracao/alertas` (Telegram às 08:00 e 15:00).
- **Pix com QR Code (sem intermediário)** (Configurações → Pix da loja) — o administrador cadastra a chave Pix da loja (CPF/CNPJ
  com dígito conferido, celular vira +55…, e-mail, aleatória) e o nome que o cliente vê. No caixa, ao escolher Pix, aparece o
  **QR Code com o valor** (acompanha o valor digitado, inclusive em pagamento dividido) e o "copia e cola". O código segue o
  padrão BR Code do Banco Central (`Services/Pix/PixQrCode.cs`: EMV + CRC16, conferido com o exemplo do manual) e é montado no
  servidor a partir da chave cadastrada (`GET /api/empresa/configuracao/pix/qrcode?valor=`; desenho pelo QRCoder). O dinheiro cai
  direto na conta da loja; o caixa confere no celular antes de confirmar e a conferência fica pelo extrato OFX. Confirmação
  automática: ver Pix automático (Mercado Pago).
- **Pix automático (Mercado Pago)** (Configurações → Pix da loja → Confirmação automática) — o administrador cola o Access
  Token da aplicação do Mercado Pago (conferido em `/users/me` e guardado CRIPTOGRAFADO com DataProtection; a tela só mostra o
  final). No caixa, ao escolher Pix: "Gerar QR do Pix" cria uma `CobrancaPix` no Mercado Pago pela API de Orders (`POST /v1/orders`, pix,
  external_reference = Guid da cobrança, validade 30 min; pago = `processed/accredited`; no modo teste o comprador é
  test@testuser.com com nome APRO, que o Mercado Pago aprova sozinho). O endereço do aviso da loja aparece em Configurações para
  colar no painel do Mercado Pago (Webhooks, evento Order) e a tela fica em "Aguardando
  o pagamento…"; o caixa pergunta a cada 2,5 s (`GET /api/pix/cobrancas/{id}`, o servidor vai ao Mercado Pago no máximo a cada
  3 s) e o aviso `POST /api/pix/mercadopago/aviso/{segredo}` (sem login) adianta. **O aviso nunca é prova**: o servidor sempre
  consulta o pagamento com o token da loja e confere valor e referência. A venda só aceita o Pix com `cobrancaPixId` de cobrança
  PAGA, do mesmo valor, do mesmo caixa e ainda não usada (FOR UPDATE). Pix pago que ficou sem venda (janela fechada, venda
  cancelada) aparece no caixa como "já recebido e sem venda" para usar. Sem aviso (internet), o caixa pode confirmar manualmente.
  Teste local sem conta: `~/projetos/marketplace-testes/pix-automatico/simulador-mercadopago.py` (launch "mp-simulador") com o
  segredo de desenvolvimento `MercadoPago:Url=http://127.0.0.1:5399`.
- **Conciliação bancária (extrato OFX)** (Financeiro → Extrato do banco) — o cliente baixa o extrato no internet banking
  (formato OFX, que todo banco gera: Bradesco, Caixa, BB, Itaú, Sicredi…) e envia. `Services/Conciliacao/LeitorOfx.cs` lê
  OFX 1.x (SGML, tags sem fechar) e 2.x (XML), Latin-1 ou UTF-8, valor com ponto ou vírgula; a conta é achada pelo número
  (ACCTID) e, na primeira vez, banco/agência/número ficam gravados na conta. Linha repetida não entra de novo (FITID e, como
  garantia, contagem de linhas iguais). `ConciliacaoService` sugere: 1 linha = 1 lançamento (mesmo valor, até 3 dias),
  depósito da maquininha (bruto − taxa = o líquido do banco), vários PIX do banco = o PIX do dia de um ou mais caixas. As linhas e
  os lançamentos conferidos juntos compartilham um `ConciliacaoId`; o que só o banco tem (tarifa, PIX avulso) é lançado dali mesmo
  com categoria sugerida pela descrição; mostra saldo do banco × sistema na mesma data e o que está no sistema e não veio no
  banco. Lançamento conferido não pode ser excluído nem ter valor/conta mudados sem desfazer a conferência.
  Arquivo de teste: `~/projetos/marketplace-testes/conciliacao/gerar-ofx-teste.py`.
- **Etiquetas de gôndola** (Produtos → 🏷️ Etiquetas) — `Produto.PrecoAlteradoEm` é preenchido sozinho pelo
  `AppDbContext` quando o preço de venda muda; a tela junta os produtos alterados desde um dia, uma categoria ou um
  a um, e imprime em folha A4 (3×8 de 70×37 mm ou 4×10 de 52×29 mm) com código de barras EAN-13/EAN-8 em SVG próprio.
- **Balança e impressora** (⚙️ Configurações) — *etiqueta da balança* do açougue/hortifrúti: produto com **código na
  balança (PLU)**; o PDV lê o EAN-13 "2…" (2 + código 4/5 dígitos + preço em centavos ou peso em gramas + DV; formato por
  empresa) em `GET /api/produtos/balanca/etiqueta/{codigo}`; exportação .csv para cadastrar na balança; gerador de etiqueta
  de teste. *Balança do caixa*: Web Serial (Chrome/Edge, só em https/localhost), protocolo Toledo/Filizola (ENQ → STX peso
  ETX), lida ao abrir a janela de peso e com F4; balança simulada para testes. *Impressora térmica*: cupom 80/58 mm impresso
  por iframe (1–2 vias, automático ao finalizar; `--kiosk-printing` no atalho do Chrome imprime sem janela). Configurações
  de balança do caixa e impressora ficam em cada computador (localStorage).
- **Cidade da loja (sem fronteiras)** — cada empresa tem cidade/UF/coordenadas/**fuso** (busca de cidades do Brasil na
  geocodificação da Open-Meteo, em ⚙️ Configurações ou no cadastro do cliente). O **relógio** (`Relogio.Hoje/Agora/
  InicioDoDiaUtc`) usa o fuso da empresa da requisição (AsyncLocal definido pelo `ContextoEmpresa`; padrão Brasília) e os
  relatórios usam `AT TIME ZONE {fuso}`. O **clima** é por lugar (tabela Clima com latitude/longitude): o `ColetorClima`
  registra a cada 30 min o clima de cada cidade com loja ativa e, para cidade nova, os últimos 60 dias.
- **PDV filtra enquanto digita** — busca por palavras em qualquer ordem; só números (leitor) espera o Enter.
- **Dados fiscais do produto** (preparação da NFC-e, que será por serviço fiscal pronto quando houver CNPJ): NCM, CEST,
  CFOP (padrão 5102), origem e CSOSN/CST; NCM e CEST vêm sozinhos da NF-e de compra; coluna NCM na planilha de importação.
- **Área do dono da plataforma** (`/plataforma`, login próprio criado por `./criar-dono.sh`, só pela rede interna:
  com `Publico__RedeInternaSoDono=true`, a rede interna aceita só o dono e os mercados entram só por app.wnlabs.com.br)
  — Visão geral (o faturamento do dono: receita mensal, recebido, a receber, atrasado, 12 meses), Clientes (ficha,
  plano/assinatura, cidade, horário, usuários, suspender), Cobranças (mensalidades geradas sozinhas, recebimento,
  cobrança no WhatsApp, bloqueio automático por atraso e liberação ao pagar), Planos e Sistema (endereços no ar,
  backups, disco, espaço por cliente). Só USO dos clientes: as vendas e o faturamento de cada mercado são dele.
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

> A empresa de demonstração ganha um catálogo de exemplo (55 produtos, 8 categorias, lotes de validade) na
> primeira execução.
