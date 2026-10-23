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
  limitado por perfil; pagamento dividido com troco; cupom; sangria/suprimento; **fechamento cego**;
  **troca e vale-troca** (F7); **cliente da venda** (F8), **vendedor** e **crédito parcelado**.
- **Clientes** — cadastro e histórico (compras, o que mais leva, trocas, oficina), aniversariantes e "sumidos".
- **Vendedores** — quem atendeu, comissão do mês (com % por categoria) lançada em Contas a pagar.
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
  automática: ver Pix automático (Mercado Pago ou direto no banco).
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
- **Pix automático direto no banco da loja (Banco do Brasil, Itaú, Bradesco, Santander, Inter, Sicoob, Sicredi, Efí)** (Configurações → Pix da loja → Confirmação
  automática → "Direto no banco da loja" → escolher o banco) — o mesmo Pix automático, mas o dinheiro cai **na conta PJ da loja**, sem o Mercado Pago no meio.
  `Services/Pix/PixBancoCentral.cs` fala a **API Pix padrão do Banco Central** (`PUT/GET/PATCH /cob/{txid}`,
  `PUT /pix/{e2eid}/devolucao/{id}`, `GET /cob?inicio&fim` para conferir ao ligar), com o que muda por banco num `Perfil`
  (BB: `oauth.hm.bb.com.br`/`api.hm.bb.com.br/pix/v2` nos testes, `oauth.bb.com.br`/`api-pix.bb.com.br/pix/v2` na produção, login
  Basic client_credentials, chave do desenvolvedor na URL `?gw-dev-app-key=`, QR em `pixCopiaECola` ou `textoImagemQRcode`).
  Desde 23/10 também **Inter, Sicoob, Itaú, Bradesco, Santander, Sicredi e Efí** (o que muda por banco está no `Perfil`: login no corpo × Basic, sem secret,
  campo extra na URL ou no cabeçalho, certificado já no sandbox, token fixo no sandbox; ver 23/10 em Últimas atualizações).
  Só o BB está **conferido** com o banco de verdade; os outros aparecem na tela como "ainda não conferido" (`Perfil.Conferido`).
  Outro banco (C6, Cora, Banrisul, BTG…) = outro `Perfil`. Nubank PJ e Caixa não têm API Pix pública. txid = a `Referencia` da cobrança (32 letras); a devolução usa o E2E
  guardado (`CobrancaPix.EndToEndId`) e o id fixo `D<txid>` (não devolve duas vezes). A loja cadastra client_id, client_secret,
  chave do desenvolvedor, "ambiente de testes" e, na produção, o **certificado digital A1 (.pfx) + senha** (mTLS; abre e mostra
  "nome · vence dd/mm/aaaa" antes de ligar); tudo CRIPTOGRAFADO (DataProtection). O QR sai na **chave Pix da loja** (tem que ser
  dessa conta). **Sem aviso (webhook)**: o padrão exige mTLS do nosso lado, que o túnel da Cloudflare não repassa; o caixa
  pergunta a cada 3 s (token do banco guardado até 1 min antes de vencer). Cada cobrança guarda o seu provedor (`Provedor`):
  trocar de provedor não atrapalha Pix já gerado. Caixa, aviso "Pix recebido!", estorno ("Devolver ao cliente pelo Banco do
  Brasil agora") e cupom mostram o provedor certo. Teste local sem conta: `~/projetos/marketplace-testes/pix-automatico/simulador-bb.py`
  (launch "bb-simulador", porta 5398; client_id começando com "SIMULADOR"; valor terminado em ,13 nunca é pago).
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
  contas/categorias que Contas a pagar usa); Vendas, Etiquetas e Fornecedores só somem da tela.
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

### 23/10/2026 (mais recente, tarde) — Pix automático também no Bradesco, Santander, Sicredi e Efí (não conferidos)
- Mesmo esquema da manhã (abaixo): perfis montados pela documentação, "ainda não conferido" na tela, testados só no simulador.
  Na lista da tela: Banco do Brasil, Itaú, Bradesco, Santander, Inter, Sicoob, Sicredi, Efí.
- **Bradesco**: login Basic sem escopo, `qrpix(-h).bradesco.com.br/v2` e `/auth/server/oauth/token`; e-CNPJ A1 (.pfx) já na
  homologação. **Santander**: login no corpo com `?grant_type=client_credentials` também na URL,
  `trust-pix(-h).santander.com.br/api/v1` e `/oauth/token`; .pfx da empresa (parte pública na aplicação do portal) já na
  homologação. **Sicredi**: login Basic com `grant_type` e `scope` na URL, `api-pix(-h).sicredi.com.br/api/v2`; certificado
  emitido pelo Sicredi (.cer + .key). **Efí** (ex-Gerencianet): login Basic com **corpo em JSON** (`Perfil.LoginJson`, novo),
  `pix(-h).api.efipay.com.br/v2`; certificado .p12 da conta Efí, sem senha (o servidor tenta senha nula e vazia).
- Tela: o **.cer binário (DER)** do Sicredi agora é aceito (vira PEM no navegador antes de enviar).
- Simulador `simulador-bancos.py` com `/bradesco`, `/santander`, `/sicredi`, `/efi`, cada um conferindo o seu jeito de login
  (Basic × corpo × JSON, campos exigidos na URL, formulário × JSON, certificado); certificados novos de teste
  `cliente-sem-senha.p12` e `cliente-der.cer`. `api-dev` no launch aponta os quatro para ele.
- Testado no dev (pela API, loja #12): nos 8 bancos ligar → QR → "Paga" → venda → cancelar com devolução → cancelar Pix não
  pago; Bradesco sem certificado recusado; Efí com senha errada recusada; Santander também em "produção"; Sicredi com .cer
  binário; na tela, o Sicredi ligado com .cer + .key. A #12 voltou ao Mercado Pago de teste, sem chave Pix.

### 23/10/2026 (manhã) — Pix automático direto no Inter, Sicoob e Itaú (ainda não conferidos com os bancos)
- Análise pedida pelo usuário ("deixar prontas as APIs dos outros bancos"): **Nubank PJ** não tem API Pix pública (só cobrança e
  QR fixo no app) e a **Caixa** não tem API pública de Pix/cobrança (só por intermediário): para essas lojas, Mercado Pago ou
  Pix manual. Escolhidos primeiro **Inter** (o PJ mais comum em loja pequena, integração feita pelo próprio cliente), **Sicoob**
  e **Itaú**; depois Bradesco, Santander, Sicredi, Efí.
- **Motor genérico** (`PixBancoCentral.Perfil` com propriedades nomeadas): `LoginNoCorpo` (client_id/secret no formulário, não
  no Basic), `SemSecret` (Sicoob), `ClientIdNoCabecalho` (Sicoob), `TokenFixoNoTeste` (sandbox do Sicoob: o "secret" é o token
  fixo do portal), `CertificadoNoTeste` (Inter), `Escopos` opcional (Itaú não usa), `Extra` = campo a mais na URL (BB,
  `gw-dev-app-key`) ou no cabeçalho (Inter, `x-conta-corrente`, opcional), QR também em `brcode` (Sicoob), `Conferido`.
  **Certificado .crt + .key** (Inter e Itaú emitem assim): a tela aceita os dois arquivos juntos (ou um .pfx), o servidor junta e
  guarda como .pfx (chave com senha também: `ENCRYPTED PRIVATE KEY`) — **sem migration**. Certificado agora é exigido ao ligar
  na produção de todos (e no sandbox do Inter). Banco que recusa o certificado no TLS 1.3 só "reinicia a conexão": com
  certificado, a mensagem diz para conferir o certificado (antes dizia "o banco não respondeu").
- Endereços (pela documentação, **conferir no 1º uso de cada banco**): Inter `cdpj.partners.bancointer.com.br` (`/oauth/v2/token`,
  `/pix/v2`; sandbox `cdpj-sandbox.partners.uatinter.co`); Sicoob `auth.sicoob.com.br/auth/realms/cooperado/…/token` e
  `api.sicoob.com.br/pix/api/v2` (sandbox `sandbox.sicoob.com.br/sicoob/sandbox/pix/api/v2`, token fixo); Itaú
  `sts.itau.com.br/api/oauth/token` e `secure.api.itau/pix_recebimentos/v2` (sandbox `devportal.itau.com.br/api/jwt` e
  `/sandboxapi/pix_recebimentos_ext_v2/v2`). Trocar sem publicar: `PixBanco:<Codigo>:UrlApi`/`UrlToken` no `marketplace.env`.
- **Tela**: "Direto no banco da loja" → lista de bancos ("Inter (ainda não conferido)"…); o formulário muda por banco (Sicoob
  sem secret na produção e "Token de acesso do sandbox" no teste; BB com a chave do desenvolvedor; Inter com a conta corrente
  opcional) e mostra o aviso **"Ainda não conferido com o banco"** (ao escolher e depois de ligado, com o roteiro do Pix de
  R$ 0,01). Rota nova `GET /api/pix/bancos`; `PUT /api/pix/banco` aceita `certificadoPem` e `clientSecret` vazio.
- **Simulador** `~/projetos/marketplace-testes/pix-automatico/simulador-bancos.py` (launch `bancos-simulador`, **https** na
  porta 5397, com certificado do cliente): `/inter`, `/sicoob`, `/itau`, cada um conferindo o jeito do seu banco (login no
  corpo, sem secret, escopo, cabeçalho `client_id`, certificado obrigatório). Certificados de teste em `.../pix-automatico/certs/`
  (CA do banco simulado; `cliente.crt`+`cliente.key`, `cliente.pfx` senha teste123, `cliente-com-senha.key`, `estranho.*` =
  recusado). O `api-dev` aponta os três para ele e liga `PixBanco:AceitarServidorDeTeste` (aceita o certificado do simulador;
  **só no Development**).
- **Testado no dev** (pela API, loja #12, e a tela do Inter no navegador): nos 4 bancos ligar → gerar QR → "Paga" → venda →
  cancelar com devolução pelo banco → cancelar Pix não pago; Inter sem certificado, com certificado de outra empresa e só com o
  .crt (recusados com mensagem clara); Sicoob sandbox (token fixo) e produção (.pfx; senha errada recusada); Itaú sandbox sem
  certificado e produção com .crt + .key com senha; BB igual a antes (sem a chave do desenvolvedor recusa). A #12 voltou ao
  Mercado Pago de teste, sem chave Pix, como estava.
- **Falta, por banco, para virar "conferido"**: credenciais do ambiente de testes (sandbox) de cada banco + um Pix de R$ 0,01 na
  produção do 1º cliente. Depois do teste real: `Conferido = true` no perfil.

### 22/10/2026 — Pix automático direto no Banco do Brasil (pronto para o 1º cliente BB)
Resumo do fim do dia 22/10 (este chat): (1) **R2 sem os "501"** na cópia diária (`no_head`); (2) **Treino B** de restaurar
uma loja, feito pelo usuário com sucesso (as duas na seção "fim da noite" abaixo); (3) explicado o que o Marcus tem de
pagamento/banco (Pix manual e automático, OFX; sem TEF e sem maquininha integrada); (4) **Pix direto no BB**, abaixo.
- Pedido do usuário: um possível cliente tem o BB como conta PJ principal; deixar pronto "caso algum cliente precise", sem
  esperar o cliente. Montado pela **API Pix padrão do Banco Central** (vale para outros bancos depois) com o perfil do BB (ver
  Funcionalidades → "Pix automático direto no banco da loja"). Migration `PixPeloBanco` (colunas novas na Empresa e
  `EndToEndId` na cobrança; nada muda para quem usa o Mercado Pago). Rotas: `PUT /api/pix/banco`, `DELETE /api/pix/automatico`
  (o antigo `/mercado-pago` continua).
- **Testado no dev com o simulador do BB**: credencial errada e falta da chave do desenvolvedor recusadas com mensagem clara;
  ligar pela tela; no caixa "Gerar QR" → "Pix recebido! Confirmado pelo Banco do Brasil" → venda registrada sozinha; cancelar a
  venda → "Devolver ao cliente pelo Banco do Brasil agora" → devolvido; cancelar Pix não pago; modo produção com certificado
  .pfx de teste (senha errada recusada; "nome · vence"); desligar. Mercado Pago testado de novo depois (continua igual).
- **Testado no ambiente de testes REAL do BB** (22/10, aplicação "Marcus - teste" do usuário, launch `api-dev-bb-real`): login
  OAuth + chave do desenvolvedor ok; ligar ok; criar cobrança ok (chave de teste `hmtestes2@bb.com.br` aceita, QR
  `qrcodepix-h.bb.com.br/...`); consulta ok (ATIVA); cancelar ok (REMOVIDA_PELO_USUARIO_RECEBEDOR). Achado: o BB responde
  **404 "Nenhum resultado encontrado"** ao listar `/cob` sem cobranças no período (corrigido: conta como acesso ok). Falta:
  simular o pagamento: o endpoint de testes do BB (`/testes-portal-desenvolvedor/v1/boletos-pix/pagar`) responde 403
  "Application key is not allowed to call this resource method" para a aplicação Pix do usuário, e o portal não mostra
  API de testes para acrescentar. O caminho que sobra: no 1º cliente BB, em produção, um Pix real de R$ 0,01 pago pelo
  app do banco e devolvido pelo Marcus (confere o CONCLUIDA com a lista "pix" e a devolução). Script de tentativa:
  `~/projetos/marketplace-testes/pix-automatico/bb-pagar-teste.sh <nº da cobrança>`.
  As credenciais de teste do BB passaram pelo chat em 22/10: gerar novas no portal antes de usar de novo.
- Ajuste visual: a caixinha "São credenciais de teste" (e a do BB) ficava em cima do texto no formulário do Pix.
- **Área do dono → Sistema → "Disco livre" corrigido**: mostrava "177,9 GB de 207 GB" num volume de 200 GB (dividia por
  1.000.000.000 e usava o tamanho do sistema de arquivos). Agora na unidade da Oracle (1024³) e o total é o tamanho do volume
  (lido de `/sys/class/block/<disco>/size`): "165,7 GB de 200 GB", igual ao `df -h`.

### 22/10/2026 (fim da noite) — restaurar uma loja só e as duas nuvens na área do dono
- **Restaurar UMA loja** (`infra/restaurar-loja.sh`; `docs/plano-de-desastre.md` → Situação A): o backup continua sendo do
  banco inteiro (todas as lojas, cada linha com `EmpresaId`); a restauração é por loja. Reconstrói o banco à parte no horário
  pedido (`recuperar-banco.sh --teste --manter-teste`, porta 5434), mostra tabela por tabela o que muda (contagem e
  "impressão digital" do conteúdo) e, com `--gravar`: backup do banco atual (`~/backup/<banco>-antes-restaurar-loja-N-…`),
  para o serviço (só produção), apaga as linhas da loja e copia as do backup numa transação só
  (`session_replication_role = replica`), liga o serviço, confere as contagens e apaga o banco à parte. Fora: Empresas,
  logins (`--com-usuarios`), Planos/Faturas/Clima. Todas as tabelas de dados de loja têm `EmpresaId` (conferido).
  Testado no dev (#13 estragada → voltou; #12 intacta). `--banco marketplace_dev` para testar no dev.
- **Envio contínuo**: Oracle e R2 agora independentes (`infra/enviar-wal.sh`): antes, falha na Oracle parava o script antes
  do R2 (justo o caso em que o R2 importa). `wal-ultimo.txt`: "… · Oracle ok · R2 ok".
- **Área do dono → Sistema → "Backup nas nuvens"**: uma linha para a Oracle e outra para a Cloudflare R2, cada uma com o
  contínuo (2 em 2 min), o diário e quanto está guardado (lê `wal-ultimo.txt` e `nuvem-ultimo.txt`).
- **Resolvido (22/10)**: os "501 NotImplemented" da cópia diária para o R2. O envio dava certo; o erro era a conferência
  que o rclone 1.60 faz logo depois (`HEAD ?versionId=…`, que o R2 não implementa), e a 2ª tentativa só pulava os
  arquivos. `no_head = true` no `r2:` (já no `configurar-r2.sh`): backup completo rodado em seguida sem nenhum erro,
  Oracle e R2 com 1290 arquivos / 1,802 GiB cada.
- **Treino B (restaurar uma loja)**: `infra/treino-restaurar-loja.sh foto|estragar|conferir` no dev (loja #13 "Loja Teste
  Tênis"); passo a passo em `docs/plano-de-desastre.md` → Treino B. Ensaiado em 22/10: estrago (estoque zerado, 7
  clientes apagados) desfeito, "Igual à foto".

### 22/10/2026 (noite) — bot que morria em silêncio e histórico da TopSpin (ago–out)
- **Causa do "bot parado"** (as paradas de hoje depois de publicar): os `catch` das rotinas em segundo plano deixavam passar
  `OperationCanceledException` (para respeitar o desligamento), mas o tempo esgotado do HttpClient (Open-Meteo lento) também é
  `TaskCanceledException`: o erro escapava e, com `BackgroundServiceExceptionBehavior.Ignore`, a rotina morria sem log até o
  próximo reinício. Agora os `catch` olham o token de parada (`when (!parar.IsCancellationRequested)`) no bot, clima, coletor,
  cobranças e liquidação; o clima tem timeout de 20 s. Venda recusada do bot aparece no log normal (Information).
- O bot da TopSpin estava funcionando (conferido com log de cada rodada): ~0,6 cliente/hora às 14h, ~6 vendas/dia útil;
  4 horas sem venda foi sorteio. Para diagnosticar serviço travado: `dotnet-stack` instalado (`~/.dotnet/tools`); o socket
  do processo fica no /tmp privado do systemd (caminho longo: criar um link curto e usar `TMPDIR`).
- **Histórico da TopSpin** (`./gerar-historico-loja.sh --empresa 5 --de 2026-08-01 --ate 2026-10-20`,
  `GeradorHistoricoEsporte`; backup antes em `~/backup/marketplace-antes-historico-topspin-2026-10-22.dump`): 346 vendas,
  R$ 201,7 mil (ago 80,5 mil; set 72,8 mil; 1–20/out 50,6 mil), ~metade com cliente fixo (60 criados, "desde" 01/08),
  vendedor Lucas, crédito parcelado, 40 OS entregues (numeradas antes das de agora). Financeiro desde 01/08: caixas lançados e
  depositados, maquininha, 91 boletos de fornecedor pelo custo vendido, contas fixas copiadas da implantação (sem a mensalidade
  do Marcus) e DAS 6%, tudo pago no vencimento. **Saldo de abertura do Itaú em 01/08 = R$ 60 mil** (com 40 mil ficava
  negativo: o crédito parcelado cai mês a mês); hoje ~R$ 10,8 mil + R$ 87 mil a receber da maquininha.
  Mesmo perfil do bot ao vivo com 75% do movimento e item caro (≥ R$ 150) saindo 1 unidade. Desfazer: restaurar o backup.

### 22/10/2026 (noite) — Oficina: refazer em garantia
Pedido do usuário (caso real numa loja de POA: encordoamento ruim, a loja refez e deu saída "zerada" na corda).
Migration `GarantiaDaOficina` (backup antes em `~/backup/marketplace-antes-GarantiaDaOficina-2026-10-22.dump`):
`OrdensServico` ganha GarantiaDeId, MotivoGarantia, CordaGarantiaId e CustoGarantia.
- OS **entregue** → botão **Refazer** (`POST /api/ordens/{id}/garantia`): motivo (corda estourou, tensão errada, encordoamento
  malfeito, cliente não gostou, outro), corda nova (já vem a mesma da 1ª vez, se tem estoque; ou "só reajuste"), prazo.
  Abre OS nova **ligada à original**, valor 0 e paga; a corda sai do estoque como **perda "Garantia da oficina"** (pelo custo;
  na DRE em "Perdas e ajustes"). Tudo numa transação.
- Lista: selo "garantia da OS nº X" na nova e "voltou Nx" na original. Card **Retrabalho no mês**: quantos, custo em corda e
  % das raquetes entregues que voltaram. Motivo "Garantia da oficina" entrou em `EstoqueService.MotivosPerda`.
- Testado no dev (OS 50 → garantias 51 com corda, estoque 5→4, e 52 sem corda; OS não entregue recusada).

### 22/10/2026 (fim da tarde) — cliente, vendedor e parcelas (fase 4 do plano dos ramos)
Migration `ClientesVendedoresParcelas` (backup antes em `~/backup/marketplace-antes-ClientesVendedoresParcelas-2026-10-22.dump`):
`Clientes` ganha Nascimento, Email, Raquete, Corda, Tensao e Simulado; tabelas `Vendedores` e `ComissoesMes`;
`Vendas.VendedorId`; `PagamentosVenda.Parcelas`; `Recebiveis.Parcela/Parcelas` (índice único agora por caixa+forma+parcela);
`Categorias.ComissaoPercentual`; `OrdensServico.ClienteId`. Categoria financeira nova "Comissões de vendedores" (`comissoes`).
- **Clientes** (menu, `/clientes` e a ficha `/clientes/:id`; `Services/ClientesService.cs`, `api/clientes`): o mesmo cadastro do
  fiado, para todos os ramos. Lista com busca, filtros (aniversariantes do mês, sumidos 60+ dias, novos, inativos) e números; ficha
  com compras, ticket, de quantos em quantos dias volta, há quantos dias não compra, o que mais leva, compras (com vendedor e
  parcelas), trocas e (esporte) as OS e "o que usa" (raquete, corda, tensão). Botão WhatsApp.
- **Caixa**: **Cliente (F8)** busca por nome/telefone/CPF ou cadastra na hora (telefone ou CPF já existente devolve o mesmo
  cliente); **Vendedor** no painel do total (fica escolhido neste computador; um vendedor só já vem escolhido); **Crédito**
  pede as parcelas (1 a 12). Cupom (tela e impresso) com vendedor, cliente e "em 3x de …". Caixa offline leva vendedor e cliente.
- **Oficina**: OS com telefone liga ao cadastro (cria o cliente se não existe) e guarda raquete/corda/tensão.
- **Vendedores** (`/vendedores`, `ComissaoService`): cadastro sem login, % de comissão, % próprio por categoria. Comissão do mês
  = % sobre o que o cliente pagou por item (com o desconto) − estorno das trocas; venda cancelada não entra. Mês fechado →
  "Lançar em Contas a pagar" (uma conta por vendedor, vence dia 5; não lança duas vezes).
- **Parcelas**: no fechamento do caixa o crédito parcelado vira um recebível por parcela (1ª no prazo do crédito, as outras
  de 30 em 30 dias, mesma taxa); Financeiro → A receber mostra "parcela 2/3".
- **Bot da loja de esporte**: 60 clientes fixos (`Simulado`, os 20 primeiros voltam mais), 45% das vendas identificadas e toda
  OS no nome de um deles (com a raquete/tensão dele), vendedor sorteado entre os ativos, crédito parcelado em compras maiores.
- Ajustes do usuário (mesmo dia): cadastro do cliente com **CPF obrigatório** (tela, caixa e API; a OS da oficina e os clientes
  do bot continuam sem CPF), busca "nome ou CPF", sem endereço e aniversário no formulário (os dados antigos ficam), janela
  mais larga. Tirado "(Gemini, pelo n8n)" dos Relatórios e o parêntese da Visão geral do dono; botões dos pedidos alinhados.
- Mais ajustes: Clientes com os 3 cards clicáveis (ativos / compraram em 30 dias / sumidos) e filtro "Quem mais gasta"; tensão
  sempre salva com "lbs" (`ClientesService.Tensao`). **Sugestão de compra**: produto ZERADO que vendeu nos últimos 60 dias entra
  (pelo menos 1 ou o mínimo; motivo "sem estoque") — antes um número de tênis com mínimo 0 que acabou não aparecia.
  **Número no menu "Estoque"** (`GET /api/estoque/atencao`): sem estoque ou abaixo do mínimo, como o de Compras.
- **Pedido ao fornecedor** (teste do usuário pelo WhatsApp: chegou certo): a mensagem não traz mais "Entrega: até …" e pede
  para o fornecedor confirmar pedido, valores e **data de entrega**; no pedido aberto (A caminho) dá para mudar a previsão
  (`PUT /api/compras/pedidos/{id}/previsao`), que é a que marca "atrasado". Para o teste, o fornecedor Asics Brasil da TopSpin
  está com o WhatsApp/e-mail do usuário e as meias Asics com mínimo 10 (eram 2): desfazer quando ele terminar.
- TopSpin: vendedor "Lucas" (2%) cadastrado na produção — o funcionário vendedor e encordoador do financeiro de exemplo.

### 22/10/2026 (tarde) — troca e vale-troca (fase 3 do plano dos ramos)
Migration `TrocasEValeTroca` (tabelas `Trocas`, `ItensTroca`, `ValesTroca`; `PagamentosVenda.ValeTrocaId`;
`Empresas.PrazoTrocaDias` 30 e `ValidadeValeDias` 90). Serviço `Services/TrocaService.cs`, API `api/trocas`.
- **No caixa: Troca (F7)** (`shared/troca-caixa.ts`): acha a venda pelo número do cupom ou, sem cupom, bipando o produto
  devolvido (vendas recentes com ele); marca itens/quantidade e o motivo (Tamanho, Defeito, Não gostou, Outro). Os itens
  voltam ao estoque (movimentação `Troca`, perecível volta ao lote de onde saiu) e o valor pago (com a parte do desconto
  da venda) vira um **vale-troca** `VT-XXXXXX`. "Escolher o produto novo": o crédito entra sozinho no pagamento (mais caro →
  paga a diferença; mais barato → o resto fica no vale). Ou imprime o vale (`htmlValeTroca`). Fora do prazo: senha do gerente
  (`AcaoAutorizada.TrocaForaPrazo`, aparece em Vendas → Cancelamentos e autorizações). Precisa de internet.
- **Vale-troca como forma de pagamento** (`FormaPagamento.ValeTroca`): digita o código no pagamento; não passa do saldo
  nem da validade (FOR UPDATE: dois caixas não usam o mesmo saldo). Não é dinheiro: fora do fechamento e do Financeiro, como o
  fiado. Venda paga com vale cancelada → o valor volta ao vale. Venda que já teve troca não pode mais ser cancelada.
  Cupom (tela e impresso) mostra o código e o saldo que ficou. Sem internet não aceita vale.
- **DRE**: linha "(−) Devoluções (trocas)" (crédito das trocas pela data da troca; saldo de vale vencido volta como receita
  no mês em que venceu); o custo dos itens devolvidos sai do CMV; imposto calculado já sem as devoluções.
- **Vendas → Trocas e vales** (`/vendas/trocas`): trocas do período, por motivo, produtos mais trocados, vales em aberto
  (saldo que os clientes têm na loja) e vencidos; reimprimir o vale. **Configurações → Trocas e vale-troca**: prazo e validade.
- Relatórios de vendas, ranking e Insights continuam pela venda original (a troca não "desvende" o item lá); só a DRE desconta.
- Testado no dev (Loja Teste Tênis): troca 36→37 pagando a diferença, uso parcial do vale (saldo no cupom), cancelamento
  devolvendo ao vale, troca fora do prazo com gerente, bloqueios (trocar 2×, cancelar venda trocada, passar do saldo).

### 22/10/2026 — compras: pedido só sai da Sugestão, Pedidos só recebe
Pedido do usuário: rascunho em "Pedidos" confundia (parece pedido já feito, e o botão Enviar tinha a cara do Receber).
- **Saiu a rotina de compras automáticas** (rascunhos montados ao abrir a loja), a opção em Configurações e o aviso
  "pedidos prontos" do painel. Migration `SemComprasAutomaticas`: tira `Empresa.ComprasAutomaticas` e
  `PedidoCompra.SugestaoAutomatica` e apaga os rascunhos e os automáticos cancelados (sem backup antes, a pedido do usuário;
  o backup contínuo cobre).
- **Sugestão de compra** é o único lugar de onde saem os pedidos (além do "Novo pedido"): um botão só, **Enviar pedido ao
  fornecedor**. O pedido nasce em rascunho só para a janela de envio ter o número; fechou sem mandar → apagado
  (`DELETE /api/compras/pedidos/{id}`; sobras de outro dia são apagadas no próximo pedido). Rascunho não aparece em Pedidos
  nem conta como "a caminho" (só `Enviado` conta).
- **Pedidos**: só enviados/recebidos/cancelados; filtro "A caminho"; botões Receber (destaque), Reenviar e Cancelar.
- **Número no menu "Compras"** e aviso no painel: produtos que a sugestão manda comprar (`GET /api/compras/sugestao/resumo`,
  `core/compras-resumo.ts`; atualiza ao trocar de tela, a cada minuto e ao enviar um pedido).

### 21/10/2026 (noite) — grade de tamanho e cor, serviços, oficina e a TopSpin Store
Resumo do dia 21/10: o Marcus deixou de ser só de mercado. **Ramo da empresa** (mercado / loja de esporte), **grade de
tamanho e cor**, **serviços sem estoque**, **Oficina** (ordens de encordoamento) e o **2º cliente de teste, a TopSpin Store**
(loja de tênis em Porto Alegre, nos moldes do Juvenal, bot vendendo e encordoando). Também: paginação com número digitável
em todas as listas, Contas a pagar com filtros, clima corrigido ao ligar a VM, limpeza do disco e atualizações (n8n 2.44.5).
Plano completo dos ramos: `docs/plano-ramo-e-grade.md` (fases 1, 2 e 5 feitas). Migrations do dia: `RamoDaEmpresa`,
`GradeEServicos`, `OrdensDeServico` (backups antes em `~/backup/marketplace-antes-*-2026-10-21.dump`).

- **Grade** (fase 2 de `docs/plano-ramo-e-grade.md`, migration `GradeEServicos`, backup antes em
  `~/backup/marketplace-antes-GradeEServicos-2026-10-21.dump`): cada tamanho × cor é um produto comum agrupado num
  `Modelo`; venda, estoque, compras, NF-e e caixa offline seguem por produto. Telas em Produtos → Modelos com grade
  (só aparecem no ramo esporte: `ramo().grade`). Caixa: busca pelo nome → escolhe tamanho/cor; código de barras → direto.
  Variação sem EAN ganha código interno `20…` (`Services/CodigoInterno.cs`).
- **Serviços** (`Produto.Servico`, ex.: encordoamento): vendem sem estoque; fora de estoque baixo, posição e sugestão de compra.
- **Planilha**: colunas Modelo, Tamanho, Cor, Marca e Serviço (as linhas com o mesmo Modelo viram um modelo).
- **Bot da loja de esporte** (`Simulador/PerfilEsporte.cs`): ~16 clientes/dia útil (sábado 1,6x), cesta de 1–3 itens,
  bolas/overgrip/cordas/encordoamento girando, raquete e tênis de vez em quando, 40–42 saindo mais, mais crédito.
  Testado no dev (200 vendas): ticket ~R$ 380, raquete em ~7% das vendas, calçado ~15%.
- **Oficina** (ordens de serviço, migration `OrdensDeServico`; menu "Oficina" só no ramo com serviços): cliente deixa a
  raquete (serviço, corda, tensão, prazo), Começar → Pronta → Avisar (WhatsApp com a mensagem pronta) → Entregar.
  "Da última vez" pelo telefone. Cobrança no caixa: botão **Cobrar** abre `/pdv?os=N` com serviço e corda no carrinho e
  marca a OS como paga ao concluir. O bot da loja de esporte abre OS para cada encordoamento que vende (~70% levam a corda
  da loja), faz e entrega (`Simulador/OficinaDemonstracao.cs`, só nas OS `Automatica`).
- **TopSpin Store implantada** (produção, empresa #5, ramo esporte, Porto Alegre/RS, 1 caixa, seg–sex 9–18 e sáb 10–16,
  domingo fechado; plano **Lojas** R$ 1.899 cobrando de 21/11/2026; bot SoVendas com movimento baixo/moderado, ~6–7
  vendas por dia útil): admin `wesley@marcus.com` (senha provisória em `~/projetos/marketplace-testes/topspin/acesso-topspin.txt`,
  o sistema pede a senha definitiva no 1º acesso). Catálogo importado (`importar-produtos.sh`) e financeiro
  (`topspin-financeiro.json`: Itaú PJ R$ 40 mil de exemplo, 14 contas fixas = R$ 22.099/mês — aluguel R$ 5 mil e UM
  funcionário que é vendedor e encordoador (R$ 2.800) —, saldo mínimo 8 mil, Simples 6%).
- Catálogo da TopSpin: `~/projetos/marketplace-testes/topspin/gerar-produtos-topspin.py` → `topspin-produtos.csv`
  (40 modelos/254 variações + 45 simples e serviços, 10 fornecedores; estoque ~R$ 300 mil a custo).

### 21/10/2026 — limpeza do disco, clima ao ligar, atualizações, paginação, contas a pagar e ramo da empresa
- **Clima ao ligar a VM**: duas rotinas pediam a mesma cidade ao mesmo tempo e a 2ª dava "chave duplicada" na tabela Clima,
  deixando o simulador ~5 min no clima neutro. `ClimaService.RegistrarHoraAsync` agora ignora a hora já gravada.
- **Disco** (15% usado): removidos os snaps órfãos do Firefox antigo (gnome-46-2404, mesa-2404, gtk-common-themes, bare,
  ~880 MB), a revisão antiga do `oracle-cloud-agent` (1.61), o cache do apt e um relatório de falha. O `core18` fica (é a base
  do agente da Oracle). Snapshot do Firefox (94 MB) em `/var/lib/snapd/snapshots` expira sozinho (~31 dias).
- **Ramo da empresa** (fase 1 de `docs/plano-ramo-e-grade.md`; migration `RamoDaEmpresa`, backup antes em
  `~/backup/marketplace-antes-RamoDaEmpresa-2026-10-21.dump`): `Empresa.Ramo` = "mercado" (todos os atuais) ou "esporte".
  Escolhido na área do dono → Clientes (novo/editar); decide as categorias iniciais (`Ramos.CategoriasPadrao`), as
  palavras ("o mercado"/"a loja"), o que aparece (balança/PLU e validade só no mercado) e o texto da IA. Angular:
  `core/ramos.ts` + `AuthService.ramo()`. Comando: `nova-empresa ... --ramo esporte`.
  No dev: "Loja Teste Tênis" (#13, esporte), admin em `usuarios-teste.txt`.
- **Paginação com número digitável** (componente único `shared/paginacao.ts`, `<app-paginacao>`): Anterior / Próxima +
  campo com o número da página (digita e Enter, ou sai do campo; fora da faixa volta para a válida). Usado em Vendas, Produtos,
  Estoque (posição e movimentações) e Contas a pagar. Toda paginação nova deve usar esse componente.
- **Contas a pagar com página e filtros**: `GET /api/contas` agora devolve `ContasPagina` (itens, total, valor total do filtro)
  em páginas de 25, com busca (descrição, fornecedor, nº do documento, observação; sem acento), categoria, fornecedor, origem
  (nota / à mão) e período (vencimento; na aba Pagas, a data do pagamento). Sem período, Pagas/Todas mostram só as pagas dos
  últimos 90 dias; com período, qualquer época (a aba deixou de se chamar "Pagas (90 dias)"). Antes: até 500 linhas sem página.
  No dev ficaram 72 contas de teste na "Teste Vendas" (#12): "Aluguel da loja" e "Conta de luz" (1/36 a 36/36).
- **Atualizações**: n8n 2.42.4 → **2.44.5** (backup parado em `~/backup/n8n-2026-10-21-antes-2.44`, os 14 fluxos ativos
  conferidos iguais antes/depois; aviso "Python 3 is missing" do runner é inofensivo: nenhum fluxo usa Python), Homepage e
  Portainer (imagens novas), Ubuntu (cloudflared 2026.10.3, containerd, docker compose 5.6.1, cloud-init 26.2, tmux,
  ubuntu-pro). Imagens velhas apagadas (2,3 GB). Evolution API fica na v2.3.7 fixa (WhatsApp do PROMO FEET; atualizar só
  com calma).

### 20/10/2026 — backup de 2 em 2 min, segunda nuvem (R2), stack, PLU, atualizações e clima confiável
Resumo do dia: perda máxima numa catástrofe caiu de ~10 para **~4 min**; backups agora em **duas nuvens de empresas
diferentes** (Oracle + Cloudflare R2), recuperação testada pelas duas; ficha técnica da stack em PDF; coluna PLU na
importação; VM e bibliotecas atualizadas; **clima mantido, mas só afirma o que os dados de cada mercado comprovam**
(30 h por condição + teste estatístico; IA sem regra fixa de chuva). Tudo publicado (último commit do dia: ver
`git log`). Sem migrations.

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
- **Atualizações (20/10)**: Ubuntu todo (cloudflared 2026.10.2, systemd 259.9 — liberado em fases, instalado com
  `-o APT::Get::Always-Include-Phased-Updates=true` —, python3-jwt de segurança), sem reiniciar a VM; Angular 22.2.3 (pelo
  `ng update`: o `npm update` travava nos peers exatos), ASP.NET Identity/EF Core/OpenApi e dotnet-ef 10.0.13, QRCoder 1.8.1
  (o SVG do Pix mudou de formato e ficou ~4x menor; conferido com leitor de QR: mesmo código Pix da 1.6.0).
  **TypeScript 7 não**: o Angular 22 exige TypeScript `>=6.0 <6.1` (esperar o Angular 23).
  Aviso trimestral da Oracle (Critical Patch Update): só vale para software DA Oracle (Database, Java, MySQL, WebLogic…),
  que não usamos. Único software da Oracle na VM: o agente da nuvem (snap `oracle-cloud-agent`, fica "held" = não se
  atualiza sozinho); atualizado à mão de 1.61 para 1.64 com `sudo snap refresh oracle-cloud-agent`. Conferir a cada aviso.
- **Stack do Marcus**: seção "Stack" neste README e ficha técnica em PDF para cliente/diretoria
  (`docs/stack/Stack-do-Marcus.pdf`, fonte `docs/stack/stack.html`; gerar de novo com o Chromium do Playwright:
  `~/.cache/ms-playwright/chromium-1248/chrome-linux-arm64/chrome --headless --no-sandbox --no-pdf-header-footer
  --print-to-pdf=Stack-do-Marcus.pdf file://$PWD/stack.html`). Sem IPs, IDs ou tokens.
- **Túnel explicado** no plano de desastre: o DNS aponta para o túnel (não para um IP); quem liga o `cloudflared` com o
  token recebe o tráfego. Nunca ligar o `cloudflared` em duas VMs com bancos diferentes (VM de teste: túnel desligado).
- **O que se perde numa queda**: só os ~4 min antes dela que o servidor já tinha confirmado (vendas online, cadastros);
  vendas feitas no caixa offline durante a queda sobem depois, sem duplicar.
- **Clima mais confiável** (o usuário viu "céu limpo" no Rio com tempo nublado e pensou em remover o clima). Conferido
  com os METAR dos aeroportos SBRJ/SBGL: temperatura (±0,5 °C) e chuva do Open-Meteo batem; o que erra são as nuvens.
  Mudanças: (1) o "agora" (aviso do simulador) diz só "sem chuva" nos códigos 0–3 (`ClimaService.DescreverAgora`);
  (2) Relatórios → clima: faixa de temperatura só aparece com ≥30 horas de mercado aberto, e chuva × movimento só com
  ≥30 horas com e sem chuva E diferença confiável (teste de taxas, z ≥ 3) — senão "ainda juntando dados" ou "sem
  diferença confiável" (`RelatorioClima.EfeitoChuvaPct`/`AvisoChuva`); (3) Insights IA: saiu a regra FIXA "chuva reduz
  o movimento" (verdade só no simulador); a IA só fala do efeito do clima como está nos dados daquele mercado.
  Demo conferida: 7 dias → "juntando dados"; 30 dias → -25% na chuva; 90 dias → -22%, com produtos sensíveis.
- Compras automáticas: substituídas em 22/10 (ver 22/10/2026: os pedidos saem só da Sugestão).

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
- (22/10) **Pix pelo BB, o que falta conferir**: (1) ~~aplicação no portal e ambiente de testes~~ feito em 22/10 (login,
  escopos, criar, consultar e cancelar ok no BB real); (2) **simular o pagamento** no ambiente de testes (ver o CONCLUIDA e a
  devolução) — não dá no ambiente de testes (403), fica para o Pix de R$ 0,01 do 1º cliente — e confirmar se a produção usa `gw-dev-app-key` ou `gw-app-key` (dá para trocar sem código:
  `PixBanco:BancoDoBrasil:ParamChaveProducao`); (3) na
  produção, com o cliente: aplicação dele no portal, API Pix liberada pelo gerente, certificado A1 cadastrado no portal e
  tarifa do Pix recebido no pacote PJ dele. Credenciais nunca no chat: ele cola na tela (Configurações → Pix da loja).
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
- (22/10) **Outros ramos (loja de tênis)**: plano em `docs/plano-ramo-e-grade.md` — fases 1 (ramo), 2 (grade), 3 (troca e
  vale-troca), 4 (cliente, vendedor, parcelas) e 5 (oficina, com garantia) **feitas e publicadas**. Falta a conversa com uma
  loja de verdade (fase 0) e o que ficou da grade (abaixo).
- (21/10) **Grade, o que ficou para depois**: NF-e "criar como variação do modelo", pedido de compra em matriz, relatórios
  por modelo e curva de tamanhos, preço próprio marcado por variação, demo de loja de esporte. Caixa: recarregar a página no meio da cobrança de uma OS perde o "marcar paga ao
  concluir" (marcar "Paga" na Oficina).
- (22/10) **Bot "parado"**: a causa real (tempo esgotado do clima matava a rotina em silêncio) foi corrigida à noite. Se um
  bot parecer parado: a demo fica normalmente 20–35 min sem venda de manhã e a TopSpin vende ~0,6 cliente/hora (horas sem
  venda são normais); comparar com o mesmo horário do dia anterior e procurar "Erro na rodada"/"não conseguiu comprar" no
  `journalctl -u marketplace`. Serviço travado: `dotnet-stack` (ver 22/10 noite).
- (22/10) **Restos do teste do pedido ao fornecedor na TopSpin** (o usuário pediu para deixar por enquanto): fornecedor
  **Asics Brasil** com o WhatsApp (21 96964-7393) e o e-mail do usuário; **meias Asics Quarter** com mínimo 10 (eram 2);
  **regatas Asics P** (branca e rosa) com mínimo 3 (eram 0). Desfazer quando ele pedir.
- (22/10) **Histórico da TopSpin** (01/08–20/10) gerado para análise; o usuário está olhando números e telas. Desfazer =
  restaurar `~/backup/marketplace-antes-historico-topspin-2026-10-22.dump` (os outros backups manuais foram apagados em 22/10;
  os automáticos diários e o da nuvem continuam). Saldo de abertura do Itaú em 01/08 = R$ 60 mil (decisão minha, ajustável).
- (22/10) Ideias que ficaram: link da Oficina para a ficha do cliente; taxa própria do crédito parcelado (hoje usa a do
  crédito à vista); aniversariantes saiu da tela de Clientes (o campo continua no banco).
- (21/10) **TopSpin**: o usuário já usa a loja (fez pedidos de compra, cadastrou clientes); vendedor "Lucas" (2%); bot
  SoVendas com 60 clientes fixos, OS e crédito parcelado. Plano Lojas tem 2 caixas incluídos (a TopSpin usa 1).
- (19/10) Ideia combinada e não feita: aba "Despesas e resultado" na Visão geral do dono quando houver custos.
- Envio AUTOMÁTICO do pedido ao fornecedor (sem clique: WhatsApp da loja pela API/Evolution ou e-mail próprio) — decidir quando
  houver fornecedores reais.
- Dev: o dono de teste `dono@marketplace.test` usa a mesma senha do admin da "Teste Vendas" (`usuarios-teste.txt`); o fornecedor
  "Frigorífico Boa Carne" (#24) tem contato, e-mail e CNPJ fictícios (98.765.432/0001-98, o das notas de teste "distribuidora");
  pedidos de teste #24 (boleto), #26 (pago na entrega), #27 (NF-e nota-4 já ligada), #28 (enviado, não recebido).
  A nota de teste `~/projetos/marketplace-testes/notas/nota-4-boleto-2-parcelas.xml` já foi usada no dev (empresa 12).

### Para continuar num chat novo
- Ler este README (Funcionalidades + Últimas atualizações, começando por 23/10/2026 (mais recente, tarde), 23/10/2026 (manhã), 22/10/2026, 22/10/2026 (fim da noite), 22/10/2026 (noite, as duas),  22/10/2026 (fim da tarde), 22/10/2026 (tarde), 22/10/2026, 21/10/2026 (noite), 21/10, 20/10, 19/10 (tarde),
  19/10 e 17/10), a seção Stack, as Pendências acima e `docs/plano-ramo-e-grade.md`.
  Memórias do assistente em `~/.claude/projects/-home-ubuntu-projetos-marketplace/memory/`.
- **Infra** (20/10): disco 200 GB (~165 GB livres); PostgreSQL com 300 conexões, 2 GB de cache e arquivamento contínuo a cada
  2 min (compose); backup em `nuvem:` (Oracle) e `nuvem2:` (Cloudflare R2), conferir `~/backup/wal-ultimo.txt` ("R2 ok") e o card
  da área do dono → Sistema; plano de desastre em `docs/plano-de-desastre.md`; scripts em `infra/` (enviar-wal, recuperar-banco, restaurar-loja,
  configurar-r2) e cópias versionadas das
  rotinas da VM em `infra/vm/` (os originais rodam de `~/scripts` e `/etc/systemd/system`). Teste de estresse: `estresse/README.md`
  (rodar fora do horário das lojas; o ambiente isolado foi desmontado e se recria em ~10 min).
- **Clientes de teste na produção**: Mercadinho do Juvenal (#4, mercado, Rio, plano Básico R$ 2.149) e **TopSpin Store**
  (#5, ramo esporte, Porto Alegre, 1 caixa, seg–sex 9–18 e sáb 10–16, plano Lojas R$ 1.899, bot baixo/moderado ~6–7 vendas
  por dia útil que também abre/faz/entrega as OS de encordoamento; **histórico desde 01/08** com clientes, OS e financeiro). Arquivos da TopSpin (catálogo, financeiro, acesso) em
  `~/projetos/marketplace-testes/topspin/`. Clientes novos de outro ramo: `nova-empresa.sh ... --ramo esporte` ou a área do dono.
- **Ramos** (`Models/Empresa.cs` → `Ramos` e `core/ramos.ts`): toda diferença entre mercado e loja vai como campo do perfil
  do ramo (palavras, `balanca`, `validade`, `grade`, `servicos`, exemplos). Nunca escrever "mercado"/"arroz" fixo em tela nova.
- **Publicar**: `./publicar.sh` (backup `pg_dump -Fc` da produção antes de migration nova; depois conferir migration, log, app e demo).
  Histórico de loja de esporte: `./gerar-historico-loja.sh --empresa N --de AAAA-MM-DD --ate AAAA-MM-DD` (só dias sem venda);
  da demonstração: `./gerar-historico.sh`.
  O usuário autorizou commit, push e publicar sem perguntar.
- **Dev**: launch `.claude/launch.json` → `api-dev` (porta 5200, já com `MercadoPago__Url` e `PixBanco__BancoDoBrasil__*` dos
  simuladores), `web-dev` (4200), `mp-simulador` (5399, com devolução `/refund`), `bb-simulador` (5398, API Pix do BB), `bancos-simulador` (5397 https, os 7 bancos novos); `api-dev-bb-real` = a API de dev
  apontando para o ambiente de testes REAL do BB (no lugar do `api-dev`, mesma porta). Banco do dev = `marketplace_dev` (conferir `dotnet user-secrets list`);
  a API de dev **não** aplica migrations sozinha: `cd Marketplace.Api && dotnet ef database update`.
- **Empresa de teste no dev**: "Teste Vendas" (#12, 2 caixas, 190 produtos do Juvenal, Pix automático no simulador);
  usuários admin/gerente/caixa (e o dono de teste) em `~/projetos/marketplace-testes/vendas/usuarios-teste.txt`.
- **Loja de tênis no dev**: "Loja Teste Tênis" (#13, ramo esporte, 2 caixas, catálogo da TopSpin, bot SoVendas, cidade Porto
  Alegre, vendedor "Rafael" 3%, 60 clientes fixos, histórico 01/08–20/10, OS 50–52 de teste de garantia); admin em
  `usuarios-teste.txt`. Várias cordas e bolas estão zeradas lá de tanto teste do bot (repor pela entrada de estoque).
  Para rodar comando da API no banco de dev: `ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile -- <comando>`.
- Testes de navegador: o painel às vezes não desenha (fica oculto); dirigir a página por script (input + keydown Enter) funciona.
  Logar por `fetch('/api/auth/login')` e depois recarregar a página. Para simular queda de internet no dev: parar o `api-dev`.
- Ícones novos: acrescentar o nome em `marketplace-web/ferramentas/gerar-icones.py` e rodar o script (não editar `icones.ts`).
- n8n 2.x: `update:workflow` está obsoleto; usar `publish:workflow`/`unpublish:workflow` e reiniciar o container.
- **Clima** (20/10): dado do Open-Meteo (modelo, não estação); temperatura e chuva conferidas com METAR (SBRJ/SBGL em
  `https://aviationweather.gov/api/data/metar?ids=SBRJ,SBGL&hours=6&format=raw`), nuvens não. Nunca pôr regra fixa de
  clima nas instruções da IA: o efeito vem calculado e testado por `RelatoriosService.Clima`.
- **Onde paramos (23/10)**: Pix automático direto em **8 bancos** (BB conferido; Itaú, Bradesco, Santander, Inter, Sicoob,
  Sicredi e Efí prontos e publicados, testados só com o simulador `bancos-simulador`, "ainda não conferido" na tela). Para
  conferir um banco: credenciais de sandbox reais (o usuário cola na tela, nunca no chat) e, no 1º cliente, Pix de R$ 0,01.
  Nubank PJ e Caixa não têm API Pix pública.
- **Onde paramos (fim de 22/10)**: Pix automático direto no Banco do Brasil pronto e publicado; testado no ambiente de testes
  REAL do BB até gerar/consultar/cancelar (o pagamento simulado o BB bloqueia: fica para um Pix real de R$ 0,01 no 1º cliente
  BB). Dev de volta ao normal (loja #1 no Mercado Pago de teste; `api-dev` com os simuladores). O usuário tem a aplicação
  "Marcus - teste" no portal do BB (conta salário PF); as credenciais de teste passaram pelo chat → gerar novas antes de reusar.
  Antes disso ele estava analisando a TopSpin com o histórico (DRE, Relatórios, Clientes, Vendedores, Oficina, Compras):
  ajustes dessa análise continuam sendo um próximo passo provável.
- Próximos passos possíveis: as pendências acima (inclusive o que ficou da grade); envio automático do pedido ao fornecedor;
  acompanhar Juvenal e TopSpin; testar o caixa offline numa loja de verdade; abrir caixa sem internet; NFC-e
  (serviço fiscal, quando houver CNPJ); credenciais de produção do Mercado Pago; Pix pelo BB com o 1º cliente (R$ 0,01 real,
  certificado A1, `gw-app-key`?); conferir os 7 bancos novos com credenciais de sandbox reais (a Efí tem sandbox fácil de abrir); mais bancos no Pix automático (novo `Perfil` em `PixBancoCentral`); maquininha integrada (Mercado Pago Point pela mesma API de Orders, guardando
  NSU/bandeira/autorização — prepara a NFC-e).

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
| Rotinas no servidor | clima por cidade, cobranças, liquidação da maquininha, simulador da demonstração |
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
