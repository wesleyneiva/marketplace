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
- **Cancelamentos com autorização do gerente** — no caixa, tirar um item (ou parte dele), cancelar a compra inteira e
  cancelar uma venda **já paga com estorno** só com a senha de um gerente (o gerente escolhe o nome e digita a própria senha;
  senha errada conta para o bloqueio da conta). Gerente logado autoriza a si mesmo. Tudo fica em `AutorizacoesGerente`
  (quem pediu, quem autorizou, motivo, valor pelo preço do banco) e aparece em Vendas → Cancelamentos e autorizações.
  API: `GET /api/caixa/gerentes`, `POST /api/caixa/cancelamentos` (item/compra antes de pagar), `GET /api/vendas/{id}/estorno`
  (prévia: por onde volta cada forma) e `POST /api/vendas/{id}/cancelar` (`autorizacao`, `pix`: Devolver/Manter/PorFora).
  **Estorno**: dinheiro sai da gaveta do caixa da venda (caixa aberto) ou, com o caixa da venda já fechado, da gaveta aberta de
  quem cancela (movimento `Devolucao`, lançado no fechamento dela) ou do cofre; Pix automático é **devolvido pelo Mercado Pago**
  (`POST /v1/orders/{id}/refund`, chave de idempotência fixa por cobrança) ou fica como "Pix recebido sem venda" (troca, só com o
  caixa aberto); Pix manual e cartão: a tela orienta devolver pelo app do banco / estornar na maquininha. Caixa já lançado no
  Financeiro: o estorno sai da conta de cada forma, hoje, na categoria Vendas no PDV (a DRE já tira a venda cancelada da receita).
  O operador de caixa só cancela vendas do caixa aberto dele; as de caixas fechados, o gerente cancela na tela Vendas.
- **Caixa offline** — sem internet o caixa continua vendendo. O catálogo (`GET /api/caixa/catalogo`), os gerentes e o QR fixo do
  Pix (`GET /api/empresa/configuracao/pix/qrcode-fixo`, sem valor: o cliente digita) ficam guardados no computador (IndexedDB,
  `core/offline/caixa-offline.service.ts`, renovados ao abrir o caixa e a cada 10 min); a busca sem acento roda local. As vendas sem
  internet vão para uma fila e sobem sozinhas quando ela volta (`POST /api/vendas/offline`, em ordem; teste a cada 10 s em
  `/api/saude`). **Toda venda leva um `idLocal`** (índice único): se a internet cair na resposta, a venda guardada não entra duas
  vezes. Offline vale o preço cobrado (selo "preço diferente do cadastro" se mudou) e o estoque pode ficar negativo; Pix automático e
  fiado não. Cancelar item/compra sem internet: escolhe o gerente, sem senha → registrado "sem conferência" (em destaque em
  Cancelamentos). Abrir/fechar caixa, sangria/suprimento e estorno pedem internet; o fechamento espera a fila esvaziar. A fila é por
  usuário. "Sem internet" = status 0 ou 502/503/504/52x (Cloudflare). As telas abrem sem internet pelo service worker
  (`public/sw.js`, registrado só na versão publicada) com os arquivos listados em `offline-arquivos.json` (gerado pelo `publicar.sh`);
  a sessão usa o último usuário guardado no computador quando `/api/auth/eu` não responde. Migration `CaixaOffline`.
- **Vendas** (menu, gerência) — lista do dia com filtros (dia, caixa, forma, situação), totais (vendas, total, ticket médio,
  canceladas), selo "Pix confirmado pelo Mercado Pago", cupom completo com Reimprimir e Cancelar venda (estorno).
  No caixa, "Vendas do caixa" faz o mesmo para o caixa aberto.
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
  **Desde 17/10/2026 os envios automáticos estão DESLIGADOS** (ver Últimas atualizações).
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
- **Módulos liberados por cliente** (área do dono → Clientes → ficha → Módulos liberados) — "Todos os módulos" (inclusive os
  que vierem) ou um a um: Vendas, Compras e fornecedores, Financeiro, Contas a pagar, Relatórios, Importar planilha, Etiquetas
  (e Fiado, quando `MODULOS.fiado` voltar a `true`). O básico (painel, caixa, produtos, estoque, usuários, configurações) é de todos.
  `Empresa.Modulos` (text[], null = todos; `Models/Modulos.cs`), `PUT /api/plataforma/empresas/{id}/modulos`. No cliente: o login
  traz `empresa.modulos`; menu, rotas (`precisaModulo`), botões e cartões do painel seguem a lista, e a casca pergunta
  `/api/auth/eu` a cada minuto e ao voltar para a aba (muda sem sair e entrar; tela de módulo tirado volta ao início). O servidor
  barra a API (`Seguranca/ModulosLiberados.cs`: compras, contas, relatórios, importação, fiado, financeiro — menos as listas de
  contas/categorias que Contas a pagar usa); Vendas, Etiquetas e Fornecedores só somem da tela. Compras automáticas só com Compras.
- **Posição do estoque** (Estoque → primeira aba, a que abre por padrão) — quanto a loja tem de cada produto e quanto vale:
  mercadoria a custo, valor se vender tudo (e o lucro bruto possível), produtos com estoque, sem estoque (zerados/negativos) e
  abaixo do mínimo; "Valor por categoria" (barra e % a custo, clicar filtra); lista com busca, categoria, situação e ordem (nome,
  maior valor, menor quantidade), 50 por página, e "Consultar" abre o cartão do produto no topo. Valor só conta saldo positivo.
  Totais e categorias são sempre da loja inteira (só ativos). `GET /api/estoque/posicao` (`busca`, `categoriaId`,
  `situacao`: comSaldo/semEstoque/zerado/negativo/baixo, `ordem`: nome/valor/quantidade). Produtos = cadastro; Estoque = quanto tem e o movimento.
- **Consultar produto no Estoque** — busca no topo da página Estoque (nome sem acento, palavras em qualquer ordem ou código de
  barras): cartão com estoque, mínimo, situação, custo/venda, lotes com validade (vencido em vermelho, com "Dar baixa"), últimas
  5 movimentações ("Ver todas" abre a aba filtrada) e Entrada/Perda/Ajuste; atualiza sozinho depois de registrar.
- **Perda com "Tudo" e lote vencido já preenchido** — na janela de perda, o botão **Tudo** põe o saldo inteiro do lote escolhido
  (ou, sem lote, o estoque todo); escolher o lote já põe a quantidade dele. Produto com lote vencido abre com esse lote, a quantidade
  exata (ex.: 4,673 kg) e o motivo "Vencido"; o "Dar baixa" da aba Validades abre com aquele lote (`shared/movimentar-dialog.ts`).
- **Recebimento do pedido gera a conta a pagar** (Compras → Receber → "Pagamento ao fornecedor"): **Boleto / a prazo** (vencimento,
  valor = recebido × custo ou digitado, nº do boleto) → conta em Contas a pagar; **Pago na entrega** (forma e de qual conta saiu) →
  conta já paga e o dinheiro sai na hora no Financeiro (gaveta não fica negativa); **A conta vem com a NF-e** → nada agora, o pedido
  fica "aguardando NF-e" e, na Entrada por NF-e, aparece como "já recebido": ligada a ele, a nota lança **só as contas** e guarda o
  XML, sem repetir o estoque. Conferência da NF-e avisa quando o fornecedor tem pedido recebido há até 15 dias com conta lançada
  (para não pagar duas vezes). `PedidoCompra.Pagamento`, `ContaPagar.PedidoCompraId` (migration `PagamentoDoPedido`). Pedidos
  recebidos antes disso aparecem com o selo "sem conta a pagar" (no Juvenal: 105 pedidos, ~R$ 14 mil, nunca viraram conta).
- **Pedido de compra ao fornecedor por WhatsApp/e-mail** — "Enviar" (e "Criar e enviar") abre a janela `pages/compras/enviar-pedido.ts`
  com a mensagem do pedido pronta (itens, observação, entrega pelo prazo do fornecedor; editável) e os canais do cadastro do
  fornecedor: **WhatsApp** (wa.me com o texto), **E-mail** (mailto com assunto e corpo), **Copiar** e "Já enviei por outro meio".
  Sai do WhatsApp/e-mail de quem está usando (o da loja). O pedido só vira "Enviado" quando um canal é usado; "Reenviar" nos já
  enviados. Fornecedor sem WhatsApp nem e-mail: aviso com link para o cadastro. Envio automático (sem clique, pela API do
  WhatsApp da loja ou e-mail próprio do sistema) fica para quando houver fornecedores reais.
- **Relógio de Brasília e rodapé** — barra do topo com a hora de Brasília, 24 h, HH:MM (`shared/relogio-brasilia.ts`, sempre America/Sao_Paulo);
  rodapé "Desenvolvido por WN Labs · contato@wnlabs.com.br" (`shared/rodape.ts`) na casca, no login e na troca de senha.
- **Demonstração pública** — com `Demonstracao__Ativa=true`, o login **do endereço `demo.wnlabs.com.br`** (`Demonstracao__Host`) mostra **"Ver demonstração"**: entra como
  visitante (perfil Gerente, **somente leitura** — qualquer gravação é barrada no servidor) na empresa 1, onde o
  simulador vende. Pela internet (Cloudflare Tunnel, cabeçalho `CF-Connecting-IP`): `/api/integracao` fechado,
  login com senha só no endereço dos clientes (`Publico__HostClientes`, padrão app.wnlabs.com.br) e nunca para usuários da empresa de demonstração, limite de 10 tentativas/min no login e 300 pedidos/min por IP.
- **Regras importantes**
  - o preço sempre vem do banco (nunca da tela);
  - produto vencido não é vendido;
  - cancelamento devolve a mercadoria ao **mesmo lote** e exige dinheiro na gaveta;
  - concorrência: `xmin` (otimista) nas operações de estoque e `SELECT … FOR UPDATE` (pessimista) na venda.

## Últimas atualizações

### 16/10/2026
Tudo publicado em https://app.wnlabs.com.br (backup do banco antes de cada migration, em `~/backup/marketplace-antes-*-2026-10-16.dump`).

- **Fonte do sistema: Space Grotesk** (a mesma do site tech.wnlabs.com.br), embutida em `marketplace-web/public/fontes`
  (sem depender do Google); títulos h1/h2 em negrito com letras mais justas. Commit `df89932`.
- **Relatórios sem Fiado**: com `MODULOS.fiado = false`, a forma Fiado das vendas antigas some de "Formas de pagamento". `f4a9121`.
- **Conciliação bancária pelo extrato OFX** (Financeiro → Extrato do banco): importa o .ofx de qualquer banco, não repete
  linhas, sugere a conferência (1 a 1, maquininha bruto − taxa, PIX do banco um a um × PIX por caixa/dia), lança o que só o
  banco tem (tarifas) com categoria sugerida, mostra saldo do banco × sistema e o que está no sistema e não veio no banco.
  Migration `ConciliacaoOfx`. Commit `d54b1e1`. Arquivo de teste: `~/projetos/marketplace-testes/conciliacao/gerar-ofx-teste.py`.
- **Pix com QR Code (manual)** (Configurações → Pix da loja): chave Pix da loja (CPF/CNPJ conferidos, celular, e-mail,
  aleatória) e, no caixa, QR com o valor da venda direto na conta da loja, sem intermediário; o caixa confere no celular.
  BR Code do Banco Central gerado no servidor (`Services/Pix/PixQrCode.cs`, QRCoder). Migration `PixDaLoja`. Commit `0b0102c`.
- **Pix automático pelo Mercado Pago** (Configurações → Pix da loja → Confirmação automática): o administrador cola o Access
  Token (guardado criptografado); no caixa "Gerar QR do Pix" cria o pedido na **API de Orders** do Mercado Pago, o caixa
  pergunta a cada 2,5 s e o aviso (webhook) adianta; quando aprova, abre o aviso grande **"Pix recebido!"** com bipe e a
  **venda é registrada sozinha**. Pix pago sem venda aparece como "já recebido e sem venda" para usar. Migration
  `PixAutomatico`. Commits `2883a0e`, `27b380b`, `b1a60a6`, `fb8be86`, `54c96b5`.
  Testado com o simulador local (`~/projetos/marketplace-testes/pix-automatico/simulador-mercadopago.py`, launch "mp-simulador")
  **e com a conta de teste real do Mercado Pago** (aprovou em ~7 s). Para receber de verdade: ativar as credenciais de produção no
  painel do Mercado Pago (setor, site, termos), trocar o token no Marcus e desmarcar "credenciais de teste"; opcional: colar o
  endereço do aviso (mostrado em Configurações) no painel do Mercado Pago → Webhooks, evento **Order**.
- **Vídeo de apresentação** refeito (2min07, fora deste repositório: `~/projetos/marketplace-video`, roteiro em `ROTEIRO.md`).

Lojas e dados: o Mercadinho Juvenal (empresa 4) está com o Pix automático ligado em **modo teste** e tem 1 Pix de teste de
R$ 3,50 pago e sem venda (aparece no caixa como "já recebido e sem venda").

### 16/10/2026 (noite) — Vendas, cancelamentos com o gerente, caixa offline e ajustes no caixa
Tudo publicado em https://app.wnlabs.com.br; backup antes de cada migration em `~/backup/marketplace-antes-AutorizacaoGerente-2026-10-16.dump`
e `~/backup/marketplace-antes-CaixaOffline-2026-10-16.dump`. Detalhes técnicos em Funcionalidades ("Cancelamentos com autorização do
gerente", "Vendas", "Caixa offline").

- **Tela Vendas** (menu Vendas, `/vendas`, gerência): lista do dia com filtros (dia, caixa, forma, situação), totais, selos
  "Pix confirmado pelo Mercado Pago" / "sem internet" / "preço diferente do cadastro", cupom completo com Reimprimir e Cancelar
  venda (estorno), aba **Cancelamentos e autorizações** (quem pediu, quem autorizou, motivo, valor; "senha não conferida" quando
  foi sem internet). No caixa, o botão "Vendas do caixa" faz o mesmo para o caixa aberto. Commit `7ee55d6`.
- **Cancelamentos só com o gerente**: tirar item (ou parte), cancelar a compra e cancelar venda paga com **estorno** (dinheiro da
  gaveta/cofre, Pix automático devolvido pelo Mercado Pago, cartão/Pix manual com orientação; caixa já fechado → Financeiro de hoje).
  O gerente escolhe o nome e digita a própria senha no caixa. Migration `AutorizacaoGerente`. Commit `7ee55d6`.
- **Caixa offline**: sem internet o caixa continua vendendo (dinheiro, cartão, Pix pelo QR fixo da loja); vendas guardadas no
  computador e enviadas sozinhas quando a internet volta, sem duplicar (`idLocal` em toda venda); telas abrem sem internet pelo
  service worker. Migration `CaixaOffline`. Commit `9e596a8`.
- **Quantidade "3*arroz" pelo clique**: escolher o produto da lista com o mouse ignorava a quantidade (só o Enter usava). `7a78834`.
- **Venda em andamento guardada**: itens e desconto ficam no computador (localStorage, por usuário e turno) e voltam ao retornar ao
  caixa, no F5 ou se o navegador fechar ("Venda em andamento recuperada"); somem ao finalizar ou cancelar. `9c4d0ee`.
- Barra do caixa estava ilegível no tema escuro (cor do texto) — corrigida.

Limitações conhecidas (decididas assim; mudar se o cliente pedir):
- Offline: o caixa precisa ter sido **aberto com internet**; abrir/fechar caixa, sangria/suprimento, estorno, Pix automático, fiado e
  etiqueta da balança pedem internet; o fechamento espera a fila esvaziar. Cancelar item sem internet fica "senha não conferida".
- Offline vale o preço cobrado na hora; o estoque pode ficar negativo (o gerente acerta).
- Venda em andamento recuperada não guarda os pagamentos já lançados (só itens e desconto); o preço mostrado é o do momento do bipe
  (online, o servidor cobra o preço atual do cadastro).
- Ainda **não testado com queda real de internet numa loja** (no dev, derrubando a API: busca local, venda guardada, reenvio sem duplicar).
- Para o Pix automático receber de verdade: ativar as credenciais de produção no Mercado Pago e trocar o token no Marcus.

### 20/10/2026 — backup de 2 em 2 min, segunda nuvem (R2), apresentação e área do dono
- **Área do dono → Sistema**: card **Backup contínuo** (último envio à nuvem; vermelho "PARADO" se passar de 10 min)
  separado do **Backup diário completo** (antes só aparecia o diário das 04:30, parecendo que o contínuo não existia).
- **Perda máxima ~4 min** (antes ~10): `archive_timeout=120` no compose e `backup-wal.timer` a cada 2 min.
  O `infra/enviar-wal.sh` agora manda só os pedaços novos desde o último envio bom (marcas em `/var/lib/enviar-wal`),
  sem listar a nuvem inteira a cada vez (as nuvens cobram requisições acima da cota grátis).
- **Segunda nuvem: Cloudflare R2** (`nuvem2:`, bucket `marcus-backup`, mesmas 2 senhas do rclone): cobre a conta da
  Oracle suspensa (VM e bucket estão na mesma conta). `sudo infra/configurar-r2.sh` configura e copia o que já existe;
  `backup-nuvem.sh` e `enviar-wal.sh` mandam para as duas; `recuperar-banco.sh --remoto nuvem2` recupera pelo R2.
  Plano de desastre atualizado (Situação B pela outra nuvem).
  **Configurado e testado em 20/10** (conta Cloudflare wnlabss@gmail, Account ID `2365eeb5…`): cópia inicial idêntica
  (468 arquivos, 1,2 GiB), envio de 2 em 2 min "R2 ok", e `recuperar-banco.sh --teste --nuvem --remoto nuvem2` reconstruiu
  o banco **só com o R2**, igual à produção até a última venda. A chave secreta foi rotacionada (tinha passado pelo chat).
  Supabase foi avaliado e descartado como clone: não aceita o registro contínuo (WAL) e não roda o Marcus; o ganho real
  era ter os backups fora da conta da Oracle, que o R2 resolve.
- **Apresentação para diretoria** (https://claude.ai/artifact/NMSermTFFHcn3r5zxh5yfa): elementos entram sozinhos
  (build `auto`; imagens pela direita, itens da lista pela esquerda, cartões subindo), transições em fade; 1 clique =
  próximo slide. As animações só tocam no modo **Apresentar** e somem se o computador pede "reduzir movimento"
  (Windows: Acessibilidade → Efeitos visuais → Efeitos de animação ligado).
- **Importação de produtos: coluna "Código na balança (PLU)"** (14ª do modelo; aceita também "PLU", "Código da balança",
  "Cód balança", "Balança"): inteiro de 1 a 99999, sem repetir na planilha nem com outro produto; produto existente com a
  célula vazia mantém a PLU atual. Sem migration (o campo `CodigoBalanca` já existia).
- Compras automáticas (dúvida do usuário): os rascunhos que aparecem sozinhos em Pedidos são de propósito
  (Configurações → Compras automáticas); itens já em rascunho saem da sugestão por contarem como "a caminho".

### 19/10/2026 (tarde) — teste de estresse, robustez, disco e redundância fase 1
- **Teste de estresse** (`estresse/`, README próprio): ambiente isolado com 100 mercados de teste. Resultado oficial
  (`estresse/resultados/2026-10-19-com-cache.html`): **aprovado até 500 mercados reais de 3 caixas**; recomendado
  vender até ~300 nesta VM.
- **Robustez** (achados do teste): rotinas de fundo não derrubam mais a API, nova tentativa ao ligar, 90 conexões por
  aplicação (`Banco__MaxConexoes`), PostgreSQL com 300 conexões e 2 GB de cache (compose), índices de cobertura
  (migration `IndicesRelatorios`), migrations com até 10 min, relatórios e DRE guardados 2 min (`CacheCurto`; a DRE
  recalcula na hora quando o financeiro muda).
- **Disco**: MEGA removido, caches limpos, n8n enxuto (PROMO FEET só salva erros; histórico de 7 dias), logs do
  sistema com teto de 200 MB, volume aumentado para 200 GB (~165 GB livres).
- **Redundância fase 1** (`docs/plano-de-desastre.md`): arquivamento contínuo do PostgreSQL (WAL a cada ≤5 min,
  `infra/enviar-wal.sh` + `backup-wal.timer` levam à nuvem a cada 5 min), cópia base diária no backup das 03:45, e
  `infra/recuperar-banco.sh` (`--teste` / `--vm-nova` / `--ate "horário"`). Testado: só com a nuvem, igual à produção
  menos os últimos minutos (perda máxima ~10 min). O `configuracao.tar.gz` diário agora leva também token da
  Cloudflare, `.env`, serviços do systemd e `~/scripts` (cópias versionadas em `infra/vm/`).
- **Cartilha de implantação** do cliente (`docs/cartilha-implantacao/`) e apresentação para diretoria (artifact).
- As 2 senhas do rclone (criptografia da nuvem) foram guardadas pelo usuário no gerenciador de senhas (19/10/2026).

### 19/10/2026 — Posição do estoque
- **Aba "Posição do estoque"** (a primeira do Estoque): responde "quanto tenho de mercadoria?" — valor a custo e a venda,
  produtos com/sem estoque, abaixo do mínimo, valor por categoria e a lista de todos os produtos com saldo e valor. Sem migration.
  Antes, a tela Estoque não mostrava o estoque inteiro (só validades, movimentações e estoque baixo); o saldo só aparecia em Produtos.
  No dev (Teste Vendas): R$ 21.012,19 a custo e R$ 30.283,37 a venda, conferidos com SQL.
- **Insights da IA (Relatórios) religado**: o fluxo "Marketplace – Insights IA" do n8n (Webhook → Gemini → resposta, sem Telegram)
  tinha sido desligado em 17/10 junto com os do Telegram, e o botão "Gerar insights" parou. Religado só ele; testado com a chave
  da produção (resposta em ~2 s). No dev o botão dá 403 porque o dev não tem `Integracao:ChaveApi` (a tela mostra "IA sobrecarregada"
  para qualquer falha). Os outros 3 fluxos (Resumo do dia, Alertas, Semana) seguem desligados.
- Visto e não mexido: as outras abas (Validades etc.) mostram erro no console quando a API cai (`value()` do httpResource em erro);
  a tela continua de pé.

### 17/10/2026 — módulos por cliente, relógio, rodapé, Telegram desligado, estoque, compras, pagamento do pedido e backup
Tudo publicado em https://app.wnlabs.com.br (último commit do dia: ver `git log`). Backups antes das migrations:
`~/backup/marketplace-antes-ModulosLiberados-2026-10-17.dump` e `~/backup/marketplace-antes-PagamentoDoPedido-2026-10-17.dump`.
Detalhes técnicos em Funcionalidades.

- **Módulos liberados por cliente** (área do dono → Clientes → ficha → Módulos liberados): todos ou um a um; o sistema do cliente
  muda sozinho em até 1 min (menu, rotas, botões, painel) e a API barra o que não foi liberado. Migration `ModulosLiberados`.
  Commit `cdd9cbc`. Todos os clientes atuais estão com "todos".
- **Relógio de Brasília** na barra do topo (24 h, HH:MM, sem data) e **rodapé** "Desenvolvido por WN Labs · contato@wnlabs.com.br"
  em todas as telas (casca, login, troca de senha).
- **Telegram desligado** (decisão do usuário, por enquanto): os fluxos do n8n "Marketplace – Resumo do dia / Alertas / Semana"
  estão inativos (o "Insights IA", que só atende o botão dos Relatórios, foi religado em 19/10) e /vendas e /estoque saíram do fluxo "Comandos do bot" e da /ajuda do `~/assistente/bot.py` (o resto do
  assistente pessoal segue). Cópias em `~/backup/n8n-marcus-telegram-2026-10-17/` e `~/assistente/bot.py.antes-sem-marcus`.
  Religar: `docker exec n8n n8n publish:workflow --id=<id>` + `docker restart n8n`. A API `/api/integracao` continua no ar.
- **Estoque**: busca "Consultar produto" no topo (estoque, mínimo, lotes com validade, últimas movimentações, Entrada/Perda/Ajuste).
- **Perda**: botão **Tudo** (saldo do lote ou estoque todo) e lote vencido já escolhido com a quantidade exata e motivo "Vencido";
  "Dar baixa" (Validades) abre com aquele lote.
- **Pedido de compra ao fornecedor**: "Enviar" abre a mensagem pronta e os canais do cadastro (WhatsApp wa.me, e-mail mailto,
  copiar, "já enviei por outro meio"); só vira "Enviado" quando sai; "Reenviar" nos enviados. Campo do fornecedor agora
  "WhatsApp / telefone".
- **Recebimento do pedido de compra gera a conta a pagar** (boleto, pago na entrega ou "vem com a NF-e", sem duplicar com a nota).
  Antes, receber o pedido só punha no estoque e o dinheiro nunca saía do Financeiro. Migration `PagamentoDoPedido`.
  Testado no dev: boleto (conta com vencimento), pago na entrega por PIX (saiu R$ 60 do banco), gaveta sem dinheiro barrada sem
  deixar nada pela metade, NF-e ligada ao pedido "aguardando NF-e" lançou só as 2 parcelas. Os 105 pedidos antigos do Juvenal
  ficam sem conta (decisão do usuário: loja de teste).
- **Janelas fecham ao clicar fora** (`shared/fechar-ao-clicar-fora.ts`, atributo `fecharAoClicarFora` no `<dialog>`; só fecha se o
  clique começa e termina fora). Ficaram SÓ com X/Esc: as do caixa (PDV) e as que mostram senha provisória uma vez (Usuários →
  senha nova, área do dono → credencial do cliente). Janela nova: pôr o atributo (menos nesses casos).
- **Backup**: o diário (03:45) passou a guardar também `configuracao.tar.gz` (`/etc/marketplace/marketplace.env` + chaves do
  DataProtection). Vai para a nuvem da Oracle às 04:30, criptografado pelo rclone, 30 dias. **Restauração testada** (contagens
  iguais à produção). Ver Rodando → Backup.

Pendências:
- **Vídeo** (`~/projetos/marketplace-video`): tirar o Telegram — só quando o usuário pedir.
- Backup da nuvem cobre só Marcus + n8n: **decidido em 20/10 não incluir** o assistente do Telegram e os outros serviços da VM
  (se a VM sumir, eles se reinstalam do zero).
- (19/10) **Cofre de senhas**: o usuário está montando um arquivo único (guardado em 2+ lugares) com a lista passada no chat:
  rclone (feito), chave S3 da Oracle (ID, secreta, namespace), contas Oracle/GitHub/Cloudflare/domínio/Tailscale, chave SSH,
  IP do Tailscale (oracle-a1, 100.x) e IP público, integrações (Gemini, Telegram, Evolution, afiliados). Nunca mostrar senhas no
  chat: passar o comando para ele rodar no terminal.
- (19/10) **Treino mensal da recuperação**: `sudo infra/recuperar-banco.sh --teste --nuvem` e também
  `--remoto nuvem2` (R2) (próximo: ~19/11/2026).
- (20/10) **Cofre de senhas**: acrescentar o token do R2 (Account ID, Access Key ID, Secret Access Key nova).
- (19/10) **Compactar o banco do n8n** (VACUUM, n8n parado ~2 min) depois de ~26/10, quando a limpeza automática de 7 dias tiver passado.
- (19/10) **Redundância fase 2** (2ª VM em outro provedor, réplica em tempo real, mesmo túnel Cloudflare): junto com o 1º cliente pagante.
- (19/10) Ideia combinada e não feita: aba "Despesas e resultado" na Visão geral do dono quando houver custos.
- Envio AUTOMÁTICO do pedido ao fornecedor (sem clique: WhatsApp da loja pela API/Evolution ou e-mail próprio) — decidir quando
  houver fornecedores reais.
- Dev: o dono de teste `dono@marketplace.test` usa a mesma senha do admin da "Teste Vendas" (`usuarios-teste.txt`); o fornecedor
  "Frigorífico Boa Carne" (#24) tem contato, e-mail e CNPJ fictícios (98.765.432/0001-98, o das notas de teste "distribuidora");
  pedidos de teste #24 (boleto), #26 (pago na entrega), #27 (NF-e nota-4 já ligada), #28 (enviado, não recebido).
  A nota de teste `~/projetos/marketplace-testes/notas/nota-4-boleto-2-parcelas.xml` já foi usada no dev (empresa 12).

### Para continuar num chat novo
- Ler este README (Funcionalidades + Últimas atualizações, começando por 19/10/2026 (tarde), 19/10 e 17/10) e as Pendências acima.
  Memórias do assistente em `~/.claude/projects/-home-ubuntu-projetos-marketplace/memory/`.
- **Infra** (19/10): disco 200 GB (~165 GB livres); PostgreSQL com 300 conexões, 2 GB de cache e arquivamento contínuo (compose);
  plano de desastre em `docs/plano-de-desastre.md`; scripts em `infra/` (enviar-wal, recuperar-banco) e cópias versionadas das
  rotinas da VM em `infra/vm/` (os originais rodam de `~/scripts` e `/etc/systemd/system`). Teste de estresse: `estresse/README.md`
  (rodar fora do horário das lojas; o ambiente isolado foi desmontado e se recria em ~10 min).
- **Publicar**: `./publicar.sh` (backup `pg_dump -Fc` da produção antes de migration nova; depois conferir migration, log, app e demo).
  O usuário autorizou commit, push e publicar sem perguntar.
- **Dev**: launch `.claude/launch.json` → `api-dev` (porta 5200, já com `MercadoPago__Url` do simulador), `web-dev` (4200),
  `mp-simulador` (5399, com devolução `/refund`). Banco do dev = `marketplace_dev` (conferir `dotnet user-secrets list`);
  a API de dev **não** aplica migrations sozinha: `cd Marketplace.Api && dotnet ef database update`.
- **Empresa de teste no dev**: "Teste Vendas" (#12, 2 caixas, 190 produtos do Juvenal, Pix automático no simulador);
  usuários admin/gerente/caixa (e o dono de teste) em `~/projetos/marketplace-testes/vendas/usuarios-teste.txt`.
- Testes de navegador: o painel às vezes não desenha (fica oculto); dirigir a página por script (input + keydown Enter) funciona.
  Logar por `fetch('/api/auth/login')` e depois recarregar a página. Para simular queda de internet no dev: parar o `api-dev`.
- Ícones novos: acrescentar o nome em `marketplace-web/ferramentas/gerar-icones.py` e rodar o script (não editar `icones.ts`).
- n8n 2.x: `update:workflow` está obsoleto; usar `publish:workflow`/`unpublish:workflow` e reiniciar o container.
- Próximos passos possíveis: as pendências acima; testar o caixa offline numa loja de verdade; abrir caixa sem internet; NFC-e
  (serviço fiscal, quando houver CNPJ); credenciais de produção do Mercado Pago.

## Stack (conferida em 20/10/2026)
Resumo: **Angular + ASP.NET Core (.NET 10) + PostgreSQL 18**, num Ubuntu ARM na Oracle, publicado pelo **Cloudflare Tunnel**,
com backup contínuo criptografado em **duas nuvens**. Versão em PDF: `docs/stack/Stack-do-Marcus.pdf` (fonte: `docs/stack/stack.html`).

| Camada | Tecnologia |
|---|---|
| Acesso | Cloudflare (DNS de wnlabs.com.br, HTTPS, proteção) → **Cloudflare Tunnel** (`cloudflared` 2026.10, a VM liga para a Cloudflare; o DNS aponta para o túnel, não para um IP) |
| Acesso administrativo | Tailscale (SSH na `oracle-a1`, sem porta aberta) |
| Telas | **Angular 22** + TypeScript 6; fonte Space Grotesk e ícones Lucide embutidos; service worker + IndexedDB (caixa offline) |
| Servidor / API | **ASP.NET Core** em C#, **.NET 10**; entrega as telas e a API na porta 5100 (serviço `marketplace` do systemd); atende app.wnlabs.com.br e demo.wnlabs.com.br |
| Dados | **PostgreSQL 18** (Docker, `postgres:18-alpine`, só 127.0.0.1); Entity Framework Core 10 + Npgsql |
| Bibliotecas | ASP.NET Identity (login, perfis, bloqueio), ClosedXML (planilha de produtos), QRCoder (QR do Pix) |
| Rotinas no servidor | compras automáticas, clima por cidade, cobranças, liquidação da maquininha, simulador da demonstração |
| Integrações | Mercado Pago (API de Orders, Pix automático), n8n + Google Gemini (Insights IA), serviço de clima |
| Backup | WAL contínuo (pedaço a cada 2 min) + cópia completa às 03:45; rclone criptografado → Oracle Object Storage e Cloudflare R2; `infra/recuperar-banco.sh` |
| Máquina | VM Oracle Cloud Ampere (ARM `aarch64`), 4 núcleos, 23 GB RAM, disco 200 GB; Ubuntu 26.04 LTS; Docker |

Na mesma VM, fora do Marcus: n8n (só o fluxo Insights IA atende o Marcus), Evolution API (WhatsApp, ainda não usada pelo
Marcus), site PROMO FEET, assistente do Telegram, pgAdmin, Portainer e Homepage.

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

**Backup** — todo dia às 03:45 (`backup-marketplace.timer`, `~/scripts/backup-marketplace.sh`) em `~/backup/marketplace-auto/AAAA-MM-DD`
(7 dias na VM): `marketplace.dump` (`pg_dump`), `resumo.txt` (linhas por tabela) e, desde 17/10/2026, `configuracao.tar.gz`
(`/etc/marketplace/marketplace.env` + chaves do DataProtection em `~/.local/share/marketplace/chaves`, sem elas os tokens do
Mercado Pago não abrem). Às 04:30 (`backup-nuvem.timer`) tudo vai para o Oracle Object Storage, criptografado pelo rclone, 30 dias
(ver `~/scripts/backup-nuvem.sh`; a senha da criptografia fica no gerenciador de senhas). **Restauração testada em 17/10/2026**:
baixado da nuvem, restaurado num banco à parte, contagens iguais às da produção, configuração e chaves idênticas.
Configuração numa VM nova: `sudo tar -xzf configuracao.tar.gz -C /`. Restaurar o banco:

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
