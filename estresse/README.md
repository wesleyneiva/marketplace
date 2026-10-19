# Teste de estresse do Marcus

Mede quantos mercados a VM aguenta com o sistema inteiro rodando: muitos caixas vendendo ao mesmo tempo, gerentes
abrindo painel, relatórios, DRE e estoque, e o caixa fazendo as checagens de segundo plano (saúde, sessão, catálogo
offline). Roda num **ambiente isolado**: banco `marketplace_estresse` e API em Release na porta 5400 (só 127.0.0.1).
A produção (`marketplace`, app.wnlabs.com.br) e o dev (`marketplace_dev`) não são tocados.

> O ambiente isolado divide a VM com a produção: durante o teste, a produção fica mais lenta.
> **Rode fora do horário dos mercados** (de madrugada ou com as lojas fechadas).

## Passo a passo

```bash
./estresse/ambiente.sh montar                 # compila, cria o banco e liga a API de estresse (1ª vez ~3 min)
node estresse/mercados.mjs 100                # 100 mercados de teste (3 caixas, ~180 produtos, 5 usuários cada)
node estresse/historico.mjs --dias 90 --vendas-dia 400   # passado de cada mercado (≈160 MB por mercado)
node estresse/carga.mjs --etapas 10,25,50,100 --minutos 5 --acelerar 10
./estresse/ambiente.sh parar                  # desliga a API de estresse (o banco fica para a próxima rodada)
./estresse/ambiente.sh desmontar              # apaga tudo (banco de estresse e ~/estresse)
```

O relatório sai em `~/estresse/resultados/AAAA-MM-DD-HH-MM.html` (e `.json`).

## Como ler o resultado

- **Equivale a (mercados reais)** = mercados ativos × aceleração. Com `--acelerar 10`, 50 mercados de teste fazem
  o tráfego de 500 mercados de verdade (vendas, checagens e telas, tudo 10x mais rápido).
- **Venda p95**: 95% das vendas responderam abaixo desse tempo. Bom: abaixo de 300 ms. Limite: 800 ms.
- **Erros**: sem resposta, erro 5xx ou barrado pelo limite (429). Recusa de regra de negócio (ex.: estoque) não conta.
- **Aprovada** = venda p95 < 800 ms, erros < 0,5% e CPU média < 75%. A última etapa aprovada é o limite da VM.

## O que cada mercado de teste faz

| Quem | O que faz (no ritmo real; a aceleração multiplica) |
|---|---|
| 3 operadores de caixa | abrem o caixa e vendem 1 venda/min cada (1 a 12 itens, peso em KG, Pix 40%, débito 30%, crédito 15%, dinheiro 15% com troco); `/api/saude` a cada 10 s, `/api/auth/eu` a cada 1 min, catálogo offline a cada 10 min |
| 1 gerente | a cada ~2 min abre: painel (35%), relatórios de 30 dias (20%), vendas do dia (20%), posição do estoque (15%) ou DRE (10%) |

Cada mercado usa IPs próprios (cabeçalho `CF-Connecting-IP`, como a Cloudflare manda): valem os limites de login
(10/min por IP) e de 300 pedidos/min por IP da internet. O estoque dos mercados de teste é "infinito" e sem
vencidos, porque o teste mede velocidade e não falta de produto.

## Não cobre (ainda)

- O caminho pela internet (Cloudflare Tunnel até app.wnlabs.com.br): o teste bate direto na API.
- Pix automático (Mercado Pago), insights da IA (n8n/Gemini), NF-e e cancelamentos com estorno.
- Queda e volta da internet no caixa (a fila offline fica no navegador).
