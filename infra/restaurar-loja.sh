#!/bin/bash
# RESTAURAR UMA LOJA SÓ (22/10/2026). O backup é do banco inteiro (todas as lojas juntas, cada linha com o número da loja
# em "EmpresaId"); a restauração é POR LOJA: as outras não são tocadas.
#
#   1. Reconstrói o banco À PARTE no horário pedido (infra/recuperar-banco.sh --teste --manter-teste: contêiner
#      marketplace-db-recuperando, porta 5434), a partir da cópia diária + o registro contínuo (VM ou nuvem).
#   2. Mostra, tabela por tabela, quantas linhas a loja tem AGORA e quantas tinha NO BACKUP (simulação: nada muda).
#   3. Com --gravar: backup do banco atual, para o serviço (só na produção), apaga as linhas DESSA loja e põe no lugar
#      as do backup — numa transação só (ou tudo, ou nada) —, liga o serviço e confere as contagens.
#
# Fica de fora (não volta no tempo): o cadastro da empresa (plano, horário, configurações), os logins dos usuários
# (senhas; use --com-usuarios para voltar também) e o que é da plataforma (planos, faturas, clima).
# O que a loja fez DEPOIS do horário pedido é perdido (por isso o backup antes de gravar).
#
# Roda como root:
#   sudo infra/restaurar-loja.sh --empresa 4 --ate "2026-10-22 14:00"            ← simulação (mostra o que muda)
#   sudo infra/restaurar-loja.sh --empresa 4 --ate "2026-10-22 14:00" --gravar   ← aplica
#   --usar-recuperado  usa o banco já reconstruído da simulação anterior (não reconstrói de novo)
#   --banco marketplace_dev   outro banco do mesmo servidor (ex.: testar no dev; aí o serviço não é parado)
#   --nuvem / --remoto nuvem2  de onde vem o backup (igual ao recuperar-banco.sh; padrão: o que está na VM)
#   --com-usuarios     volta também os usuários da loja (logins e senhas como estavam no horário)
set -euo pipefail

PRINCIPAL=marketplace-db
RECUPERADO=marketplace-db-recuperando
USUARIO=marketplace
DIR="$(cd "$(dirname "$0")" && pwd)"

empresa="" ate="" banco=marketplace gravar=0 usar=0 usuarios=0 origem=()
while [ $# -gt 0 ]; do
  case "$1" in
    --empresa) empresa="$2"; shift ;;
    --ate) ate="$2"; shift ;;
    --banco) banco="$2"; shift ;;
    --gravar) gravar=1 ;;
    --usar-recuperado) usar=1 ;;
    --com-usuarios) usuarios=1 ;;
    --nuvem) origem+=(--nuvem) ;;
    --remoto) origem+=(--remoto "$2"); shift ;;
    *) sed -n 2,26p "$0"; exit 1 ;;
  esac
  shift
done
[[ "$empresa" =~ ^[0-9]+$ ]] || { sed -n 2,26p "$0"; exit 1; }
[ "$(id -u)" = 0 ] || { echo "Rode com sudo."; exit 1; }
[ -n "$ate" ] || [ "$usar" = 1 ] || { echo "Informe --ate \"AAAA-MM-DD HH:MM\" (o momento ANTES do problema)."; exit 1; }

sql() { docker exec "$1" psql -U "$USUARIO" -d "$banco" -qtAX -v ON_ERROR_STOP=1 -c "$2"; }

# ---------------------------------------------------------------- 1. o banco no horário pedido, à parte
if [ "$usar" = 0 ]; then
  echo "▶ Reconstruindo o banco À PARTE até $ate (a produção não é tocada)…"
  "$DIR/recuperar-banco.sh" --teste --manter-teste --ate "$ate" "${origem[@]}" | grep -E '▶|ERRO|Banco recuperado'
fi
docker inspect -f '{{.State.Running}}' "$RECUPERADO" 2>/dev/null | grep -q true \
  || { echo "ERRO: o banco recuperado não está ligado (rode sem --usar-recuperado)."; exit 1; }

nome_agora=$(sql "$PRINCIPAL" "select \"Nome\" from \"Empresas\" where \"Id\" = $empresa")
nome_backup=$(sql "$RECUPERADO" "select \"Nome\" from \"Empresas\" where \"Id\" = $empresa" || true)
[ -n "$nome_agora" ] || { echo "ERRO: empresa $empresa não existe no banco $banco."; exit 1; }
[ -n "$nome_backup" ] || { echo "ERRO: a empresa $empresa ainda não existia no horário pedido."; exit 1; }
echo; echo "Loja: #$empresa $nome_agora · banco: $banco · backup de: ${ate:-(o já recuperado)}"

# ---------------------------------------------------------------- 2. as tabelas da loja e as colunas de cada uma
excluir="'AspNetUsers'"; [ "$usuarios" = 1 ] && excluir="''"
tabelas=$(sql "$PRINCIPAL" "select table_name from information_schema.columns where table_schema = 'public'
  and column_name = 'EmpresaId' and table_name not in ($excluir) order by table_name")
colunas() { sql "$1" "select string_agg(format('%I', column_name), ',' order by ordinal_position)
  from information_schema.columns where table_schema = 'public' and table_name = '$2'"; }

printf "\n  %-26s %12s %12s\n" "Tabela" "Agora" "No backup"
total_agora=0 total_backup=0
declare -A COLS
for t in $tabelas; do
  agora=$(sql "$PRINCIPAL" "select count(*) from \"$t\" where \"EmpresaId\" = $empresa")
  if [ -n "$(sql "$RECUPERADO" "select 1 from information_schema.tables where table_schema='public' and table_name='$t'")" ]; then
    backup=$(sql "$RECUPERADO" "select count(*) from \"$t\" where \"EmpresaId\" = $empresa")
    # Só as colunas que existem nos dois (o banco de agora pode ter colunas novas de uma migration depois do backup).
    comuns=$(comm -12 <(colunas "$PRINCIPAL" "$t" | tr , '\n' | sort) <(colunas "$RECUPERADO" "$t" | tr , '\n' | sort) | paste -sd,)
  else
    backup=0; comuns=""   # tabela criada depois do backup: a loja fica sem linhas nela
  fi
  COLS[$t]=$comuns
  total_agora=$((total_agora + agora)); total_backup=$((total_backup + backup))
  [ "$agora" = 0 ] && [ "$backup" = 0 ] && continue
  # Mesmo número de linhas pode esconder conteúdo mudado (ex.: preços alterados): compara uma "impressão digital".
  marca=""
  if [ "$agora" != "$backup" ]; then marca="← muda"
  elif [ -n "$comuns" ]; then
    impressao="select md5(coalesce(string_agg(x::text, '|' order by x::text), '')) from (select $comuns from \"$t\" where \"EmpresaId\" = $empresa) x"
    [ "$(sql "$PRINCIPAL" "$impressao")" = "$(sql "$RECUPERADO" "$impressao")" ] || marca="← conteúdo mudou"
  fi
  printf "  %-26s %12s %12s %s\n" "$t" "$agora" "$backup" "$marca"
done
printf "  %-26s %12s %12s\n" "TOTAL" "$total_agora" "$total_backup"

if [ "$gravar" = 0 ]; then
  echo; echo "Simulação: nada foi mudado. Para aplicar (usa este mesmo banco recuperado):"
  echo "  sudo $0 --empresa $empresa --banco $banco --usar-recuperado --gravar"
  echo "Para apagar o banco recuperado sem aplicar: sudo $DIR/recuperar-banco.sh --limpar-teste"
  exit 0
fi

# ---------------------------------------------------------------- 3. aplica
quando=$(date +%F-%H%M)
copia="/home/ubuntu/backup/$banco-antes-restaurar-loja-$empresa-$quando.dump"
echo; echo "▶ Backup do banco atual: $copia"
docker exec "$PRINCIPAL" pg_dump -U "$USUARIO" -Fc "$banco" > "$copia"
chown ubuntu:ubuntu "$copia"

TMP=$(mktemp -d /var/tmp/restaurar-loja.XXXX)
parar_servico=0; [ "$banco" = marketplace ] && parar_servico=1
religar() { [ "$parar_servico" = 1 ] && systemctl start marketplace || true; docker exec "$PRINCIPAL" rm -rf /tmp/restaurar-loja || true; rm -rf "$TMP"; }
trap religar EXIT

echo "▶ Copiando as linhas da loja no backup…"
for t in $tabelas; do
  [ -n "${COLS[$t]}" ] || continue
  docker exec "$RECUPERADO" psql -U "$USUARIO" -d "$banco" -qtAX -v ON_ERROR_STOP=1 \
    -c "\\copy (select ${COLS[$t]} from \"$t\" where \"EmpresaId\" = $empresa) to stdout" > "$TMP/$t.copy"
done
docker exec "$PRINCIPAL" mkdir -p /tmp/restaurar-loja
docker cp "$TMP/." "$PRINCIPAL:/tmp/restaurar-loja/" >/dev/null

{
  echo "begin;"
  # As chaves estrangeiras são conferidas no fim por nós (contagens); durante a troca, a ordem das tabelas não importa.
  echo "set local session_replication_role = replica;"
  for t in $tabelas; do
    echo "delete from \"$t\" where \"EmpresaId\" = $empresa;"
    [ -n "${COLS[$t]}" ] && echo "copy \"$t\" (${COLS[$t]}) from '/tmp/restaurar-loja/$t.copy';"
  done
  echo "commit;"
} > "$TMP/aplicar.sql"
docker cp "$TMP/aplicar.sql" "$PRINCIPAL:/tmp/restaurar-loja/aplicar.sql" >/dev/null

if [ "$parar_servico" = 1 ]; then echo "▶ Parando o serviço (as outras lojas ficam ~1 min sem o site; o caixa segue offline)…"; systemctl stop marketplace; fi
echo "▶ Trocando as linhas da loja #$empresa (transação única)…"
docker exec "$PRINCIPAL" psql -U "$USUARIO" -d "$banco" -q -v ON_ERROR_STOP=1 -f /tmp/restaurar-loja/aplicar.sql
religar; trap - EXIT

# ---------------------------------------------------------------- 4. confere
echo "▶ Conferindo…"
erros=0
for t in $tabelas; do
  [ -n "${COLS[$t]}" ] || continue
  a=$(sql "$PRINCIPAL" "select count(*) from \"$t\" where \"EmpresaId\" = $empresa")
  b=$(sql "$RECUPERADO" "select count(*) from \"$t\" where \"EmpresaId\" = $empresa")
  [ "$a" = "$b" ] || { echo "  DIFERENTE: $t (agora $a, backup $b)"; erros=1; }
done
# Linhas que apontam para algo que não existe mais (deveria ser zero).
orfas=$(sql "$PRINCIPAL" "select count(*) from \"ItensVenda\" i where i.\"EmpresaId\" = $empresa
  and not exists (select 1 from \"Vendas\" v where v.\"Id\" = i.\"VendaId\")")
[ "$orfas" = 0 ] || { echo "  ATENÇÃO: $orfas itens de venda sem a venda."; erros=1; }
"$DIR/recuperar-banco.sh" --limpar-teste >/dev/null
if [ "$erros" = 0 ]; then
  echo; echo "✅ Loja #$empresa ($nome_agora) restaurada para ${ate:-o horário recuperado}. As outras lojas não foram tocadas."
  echo "   Desfazer: restaurar $copia (pg_restore)."
else
  echo; echo "⚠ Restaurada com diferenças (acima). O banco de antes está em $copia."
  exit 1
fi
