# Plano: ramo da loja + grade de tamanho e cor (pensando numa loja de tênis)

Escrito em 21/10/2026, **antes de programar**. Nada disto existe ainda no código.

## Objetivo

O Marcus atender outros ramos de varejo (o primeiro: loja de material esportivo de tênis — raquetes, calçados,
roupas, bolas, cordas, acessórios) **com o mesmo código, o mesmo servidor e as mesmas atualizações**, sem mudar nada
para quem já é mercado.

## Regras que valem para todas as fases

1. **Um sistema só.** Nada de cópia do projeto por ramo. Diferenças viram configuração da empresa (ramo + módulos).
2. **Mercado não percebe nada.** Toda migration só ACRESCENTA (colunas que aceitam vazio, tabelas novas). Produto sem
   grade continua exatamente como hoje.
3. **Cada fase vai para o ar sozinha**, com backup antes da migration, teste no dev, caixa offline conferido e o
   teste de estresse quando mexer no caixa ou nos relatórios.
4. **Cliente real antes da fase 2.** A grade só é programada depois da conversa da fase 0.

---

## Decisão central: cada tamanho/cor é um produto (SKU) agrupado num "modelo"

Hoje **tudo** se apoia no `ProdutoId`: item da venda (`ItemVenda`), estoque (`MovimentacaoEstoque`), lotes
(`LoteValidade`), pedido de compra, vínculo da NF-e (`VinculoFornecedorProduto`, o cProd do fornecedor), caixa
offline e relatórios. Há ~25 arquivos da API e várias telas lendo produto.

**Escolha:** cada combinação vendável — "Tênis Asics Gel-Resolution 10 · 42 · Branco/Azul" — é uma linha de
`Produtos`, com o seu código de barras, estoque e (se quiser) preço. Uma tabela nova **`Modelos`** agrupa as
variações para o cadastro, a tela de estoque e os relatórios.

| | SKU = produto + Modelo (escolhido) | Variação dentro do produto (descartado) |
|---|---|---|
| Venda, estoque, lotes, compras, NF-e, offline | **não mudam** (já são por ProdutoId) | todos ganham `VariacaoId` |
| Código de barras por tamanho | já existe (`CodigoBarras` único por empresa) | campo novo em outra tabela |
| Risco para os mercados | quase zero (`ModeloId` vazio = como hoje) | alto (mexe no caixa e no estoque) |
| Trabalho | cadastro, telas e relatórios | sistema inteiro |

É assim que fazem os sistemas de calçados e roupas: o fornecedor também manda a NF-e com um cProd e um EAN por
tamanho/cor, então a entrada pela nota encaixa direto.

### Modelo de dados (rascunho)

```
Modelos (novo)
  Id, EmpresaId, Nome ("Tênis Asics Gel-Resolution 10"), Referencia (do fornecedor, ex. "1041A375"),
  Marca, CategoriaId, FornecedorId, GradeId, PrecoVenda e PrecoCusto padrão, NCM/CFOP/origem padrão, Ativo

Grades (novo) — listas de tamanhos reaproveitáveis por empresa
  Id, EmpresaId, Nome ("Calçado BR 34–45", "Roupa PP–GG", "Empunhadura L0–L4", "Único"), Tamanhos (lista ordenada)

Produtos (+3 colunas, todas opcionais)
  ModeloId (vazio = produto simples, como hoje), Tamanho ("42"), Cor ("Branco/Azul")
  índice único (EmpresaId, ModeloId, Tamanho, Cor) quando ModeloId não for vazio
```

O `Nome` do produto continua preenchido ("Tênis Asics Gel-Resolution 10 42 Branco/Azul"), montado sozinho. Assim o
cupom, a busca, a NF-e e tudo o que já lê `Nome` seguem funcionando sem mudar.

Cor fica como **texto livre com sugestões** (as cores já usadas pela loja), e não como cadastro. Cada marca dá um
nome diferente para a mesma cor, e cadastro de cor vira burocracia.

---

## Fase 0 — Conversa com a loja (antes de qualquer código)

Perguntas para a loja de tênis:

- Quantos produtos? Quanto disso tem grade (calçado, roupa) e quanto é item simples (bola, corda, overgrip)?
- Como numeram: BR, US, EUR? Tamanho de empunhadura em raquete (L0–L4)? Roupa PP–GG ou número?
- Preço muda por tamanho? (Normalmente não. Às vezes o infantil é mais barato.)
- Os fornecedores mandam NF-e com EAN por tamanho? Quais marcas (Asics, Wilson, Head, Babolat, Mizuno…)?
- Fazem encordoamento? Quantas raquetes por semana, com que prazo? Cobram por corda + mão de obra?
- Como é a troca hoje (prazo, vale-troca, troca por outro número)?
- Têm vendedor com comissão? Vendem parcelado em quantas vezes?
- Já têm sistema? Dá para exportar a planilha de produtos e o estoque?
- Têm loja on-line ou vendem pelo Instagram/WhatsApp?

O que sair daqui pode mudar a ordem das fases 3 a 5.

---

## Fase 1 — Ramo da empresa (pequena, sem risco) — FEITA em 21/10/2026

**Como ficou:** `Empresa.Ramo` (migration `RamoDaEmpresa`), `Ramos` em `Models/Empresa.cs` (categorias padrão e nome
do negócio) e `core/ramos.ts` no Angular (`AuthService.ramo()`: palavras, `balanca`, `validade`, nome da etiqueta).
O ramo é escolhido em Área do dono → Clientes (novo/editar) e aparece como selo na lista e na ficha. Para esporte, ficam
escondidos: PLU no produto, seções de balança nas Configurações, "perecível" no produto e na entrada da NF-e, cartão e
lista de validade no painel, aba Validades do estoque (o produto que já controla validade continua mostrando). Insights IA
fala "loja de material esportivo" e ganha pontos de atenção do ramo. **Módulos por ramo: não precisou** — o fiado já está
desligado para todos e as etiquetas servem para loja (o formato aparece como "Prateleira"); o módulo `grade` da fase 2
é que vai nascer ligado no esporte.


**O que é:** um campo `Empresa.Ramo` ("mercado" | "esporte", com espaço para outros). É escolhido no cadastro do
cliente na área do dono e pode ser trocado depois.

**O que o ramo decide:**

- **Módulos ao criar o cliente.** No esporte, Etiquetas de gôndola e Fiado vêm desligados. Continua dando para mudar
  à mão na área do dono → Módulos liberados.
- **O que aparece nas telas.**
  - **Balança/PLU:** escondida no cadastro, na importação e nas configurações. Hoje "balança" aparece 28 vezes nas
    telas.
  - **Validade/lotes:** desligada por padrão. Já é opcional por produto (`ControlaValidade`); só não aparece como
    sugestão.
- **Palavras.** Um arquivo `ramos.ts` no Angular e um `Ramos.cs` na API com os termos que mudam: "mercado" → "loja",
  "gôndola" → "prateleira" etc. São ~37 arquivos das telas com "mercado"; passar só os que o cliente vê.
- **Insights IA.** O texto que hoje diz "consultor… do mercadinho" (`InsightsService`) passa a usar o ramo. Para
  esporte, entram outros pontos de atenção: grade quebrada, curva de tamanhos, sazonalidade de torneio e férias,
  encordoamento parado. Continua valendo a regra do clima: só o efeito que os dados daquela loja comprovarem.
- **Clima.** Fica. O relatório já só afirma o que passou no teste estatístico.
- **Demonstração.** Mais tarde, uma demo de loja de esporte (simulador com outro catálogo). Não é preciso para o
  primeiro cliente.

**Migration:** uma coluna (`Ramo`, padrão "mercado"). Todos os clientes atuais ficam "mercado" e nada muda.

**Esforço:** ~1 sessão.

---

## Fase 2 — Grade de tamanho e cor (o trabalho de verdade) — FEITA em 21/10/2026 (sem conversa com loja, a pedido)

**Como ficou:** `Models/Modelo.cs` (Tamanhos/Cores em arrays, na ordem da matriz) + `Produto.ModeloId/Tamanho/Cor`
(migration `GradeEServicos`); `ModelosController` (listar, detalhar, criar com combinações, editar repassando às
variações, acrescentar tamanho/cor, entrada em grade, desativar); código interno EAN-13 "20…" (`CodigoInterno`; a
etiqueta de balança agora cede ao código exato de produto). Telas: Produtos → **Modelos com grade** (`/produtos/modelos`),
modelo novo com matriz e estoque inicial, matriz do estoque com entrada em grade. Caixa: buscar pelo nome mostra o modelo
uma vez e abre a escolha de tamanho/cor (também offline); bipar o código vai direto. Planilha: colunas Modelo, Tamanho,
Cor, Marca e Serviço. **Serviços** (`Produto.Servico`): vendem sem estoque e ficam fora de estoque baixo/posição/compras.
Bot: `PerfilEsporte` (movimento, cesta, tamanhos do meio, pagamento). Ficou para depois: NF-e "criar como variação",
pedido de compra em matriz, relatórios por modelo/curva de tamanhos, preço próprio marcado por variação.


Vale para qualquer ramo: mercado também pode usar, por exemplo "Refrigerante X" em 350 ml / 600 ml / 2 L. Liga por
empresa pelo módulo novo `grade` (padrão: ligado no esporte, desligado no mercado).

### 2a. Cadastro

- **Produtos → "Novo modelo com grade":**
  - nome, referência, marca, categoria, fornecedor, preço e custo;
  - escolhe a grade e marca as cores;
  - o sistema mostra a **matriz tamanho × cor**, desmarca-se o que a loja não trabalha, e ele cria os SKUs.
- **Código de barras por SKU:** digitado, bipado ou **gerado pelo sistema** (EAN interno começando com "2", como a
  balança já faz) para quem não tem EAN do fornecedor. Imprimir etiqueta com nome curto, tamanho, cor, preço e código.
- **Editar o modelo** (preço, nome, categoria) vale para todas as variações, menos as que tiverem preço próprio
  marcado.
- **Acrescentar tamanho ou cor** depois cria só os SKUs novos.
- **Lista de produtos:** o modelo aparece numa linha só ("Gel-Resolution 10 · 14 variações · 23 pares") e abre a
  matriz.

### 2b. Estoque

- Na posição do estoque, o modelo aparece **como matriz**, com quantidade por tamanho × cor e destaque nos tamanhos
  zerados (**"grade quebrada"**).
- Ajuste, inventário e movimentação continuam por SKU, que é o que já existe.
- O estoque mínimo pode ser definido no modelo e copiado para os tamanhos ("manter 1 par de cada número").

### 2c. Caixa (PDV) e caixa offline

- **Bipar o código:** já funciona, porque o código é do SKU.
- **Busca por nome:** se o resultado for um modelo, abre a escolha de tamanho e cor (só mostra o que tem estoque; o
  resto aparece apagado). É só tela: a venda segue com o `ProdutoId` do SKU.
- **Cupom:** "Tênis Asics Gel-Resolution 10 42 Branco/Azul". O nome já vem montado.
- **Caixa offline:** o cache de produtos ganha `ModeloId`/`Tamanho`/`Cor` para a escolha funcionar sem internet.
  Testar com a internet desligada, como foi feito em 16/10.

### 2d. Compras e NF-e

- **Entrada pela NF-e:** a nota de calçado vem com uma linha por tamanho (cProd + EAN). O `VinculoFornecedorProduto`
  já liga cProd → SKU. O que falta é, para item novo, **"criar como variação do modelo…"**, com o tamanho e a cor
  sugeridos pelo texto da nota (ex.: "TENIS GEL-RESOLUTION 10 BCO/AZ 42"); a pessoa confirma.
- **Pedido de compra:** digita a quantidade **na matriz** do modelo (uma linha por SKU por baixo).
- **Compras automáticas:** continuam por SKU. Ganham a opção de sugerir pela **curva de tamanhos** (quanto cada
  número vende) em vez do mínimo fixo. Isso entra depois do primeiro mês de vendas da loja.

### 2e. Importação por planilha

- Colunas novas opcionais: **Modelo / Referência**, **Tamanho**, **Cor**. As linhas com a mesma referência viram um
  modelo só. A planilha de mercado continua igual, porque as colunas são opcionais.
- Prévia mostrando "12 modelos, 140 variações".

### 2f. Relatórios

- Mais vendidos **por modelo** (soma das variações), com abertura por tamanho e cor.
- **Curva de tamanhos:** o quanto cada número vende por categoria (ex.: masculino, 40–42 = 55%). É o que orienta a
  compra da próxima coleção.
- **Grade quebrada:** modelos que ainda têm estoque mas sem os números que mais vendem.
- **Giro e encalhe por modelo:** quanto tempo parado e quanto dinheiro está ali.
- A DRE e o financeiro não mudam: venda é venda.

**Migration:** tabelas `Modelos` e `Grades` e 3 colunas em `Produtos`, tudo opcional.

**Esforço:** ~5 a 7 sessões. Divisão sugerida: cadastro + estoque (2), caixa + offline (1–2), NF-e + pedido +
importação (1–2), relatórios (1).

---

## Fase 3 — Troca e vale-troca

Em calçado e roupa, troca de número é rotina, e hoje só existe cancelamento/estorno da venda.

- **Troca no caixa:**
  - busca a venda (pelo cupom ou pelo cliente);
  - marca os itens que voltam (entram no estoque);
  - bipa os novos;
  - paga ou recebe só a diferença.
- **Vale-troca:** se o cliente não leva nada na hora, gera um crédito com código. Ele é usado como forma de pagamento
  numa venda futura e tem validade.
- **Prazo de troca** configurável (ex.: 30 dias) e motivo da troca (tamanho, defeito, gosto), que vira relatório.
- **Financeiro:** a troca sem diferença não é receita nova, e o vale-troca é um passivo até ser usado. A DRE precisa
  tratar isso certo (partidas dobradas, como o resto do financeiro).

**Esforço:** ~2 sessões.

---

## Fase 4 — Cliente e vendedor

- **Cliente:** já existe a tabela `Cliente` (do fiado, hoje desligado).
  - Reaproveitar: CPF opcional na venda (já vai ser exigido na NFC-e acima de certo valor), telefone e aniversário.
  - Histórico de compras.
  - Para esporte: **"raquete e corda que usa"**, para a fase 5.
- **Vendedor:** na venda, escolher o vendedor (o caixa pode ser outra pessoa).
  - Relatório de vendas por vendedor e **comissão** (% por vendedor, com opção por categoria).
  - O fechamento do mês vira lançamento a pagar no financeiro.
- **Parcelamento:** a forma "cartão de crédito" passa a guardar em quantas vezes, para o relatório e para a
  conciliação da maquininha.

**Esforço:** ~2 sessões.

---

## Fase 5 — Ordem de serviço: encordoamento

O diferencial de loja de tênis. Também serve depois para outros serviços (ajuste, conserto).

- **Abrir a OS:**
  - cliente;
  - raquete (marca/modelo, apelido);
  - corda (produto do estoque, que sai do estoque ao concluir);
  - tensão (ex.: 24/22 lb);
  - observações e prazo;
  - preço = corda + mão de obra (um produto "serviço", sem estoque).
- **Situações:** recebida → encordoando → **pronta** (opcional: avisar o cliente pelo WhatsApp quando a Evolution
  estiver ligada ao Marcus) → entregue. Cobra no caixa na entrega, ou antes.
- **Histórico por cliente:** "da última vez: RPM Blast 1.25, 24/22 lb". Isso faz o cliente voltar.
- **Painel:** OS atrasadas e prontas sem retirar.

**Esforço:** ~2 sessões.

---

## Como garantir que nada quebra para os mercados

- `ModeloId` vazio = produto simples. Todas as telas e consultas novas tratam os dois casos, e o mercado nunca vê
  "grade" se o módulo estiver desligado.
- Testes antes de cada publicação:
  - venda online e offline de produto simples e de SKU com grade;
  - entrada de NF-e de mercado (nota de teste já usada) e de calçado (criar uma nota de teste com tamanhos);
  - importação da planilha de mercado sem as colunas novas.
- Teste de estresse depois da fase 2 (mexe na busca do caixa e nos relatórios).
- Backup `pg_dump` antes de cada migration (como sempre), e migrations só aditivas.

## Ordem e esforço total

| Fase | O quê | Sessões | Depende de |
|---|---|---|---|
| 0 | Conversa com a loja | — | — |
| 1 | Ramo da empresa | ~1 | — |
| 2 | Grade de tamanho e cor | ~5–7 | fase 0 |
| 3 | Troca e vale-troca | ~2 | fase 2 |
| 4 | Cliente, vendedor, parcelas | ~2 | — |
| 5 | Encordoamento (OS) | ~2 | fase 4 (cliente) |

**Mínimo para a loja de tênis começar a usar:** fases 1 + 2 + 3 (~8–10 sessões). As fases 4 e 5 entram com a loja
já usando.

## Decisões em aberto (para o usuário)

1. A fase 1 pode começar já (não depende da loja)? Ou espera a conversa?
2. Preço por tamanho: permitir desde o início ou só preço único por modelo?
3. Código de barras interno (EAN "2…") para quem não tem do fornecedor: ok?
4. Plano/preço para esse ramo: o mesmo plano Básico ou um plano próprio (área do dono → Planos)?
5. Demo de loja de esporte: só quando houver interesse comercial?
