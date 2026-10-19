#!/bin/bash
# RECUPERAÇÃO do banco do Marcus até o último minuto (ou até um horário escolhido), a partir da cópia base diária
# (basebackup.tar.gz) + o registro contínuo de alterações (WAL) — redundância fase 1, 19/10/2026.
# Roda como root:
#
#   sudo infra/recuperar-banco.sh --teste
#        Reconstrói o banco À PARTE (contêiner temporário, volume marketplace-db-teste-dados) e compara com a
#        produção. NÃO toca na produção. Use para treinar e para conferir que os backups servem.
#   sudo infra/recuperar-banco.sh --teste --ate "2026-10-19 15:30"
#        Idem, parando nesse horário (ex.: logo antes de alguém apagar algo por engano).
#   sudo infra/recuperar-banco.sh --vm-nova [--ate "..."]
#        Numa VM NOVA (ver docs/plano-de-desastre.md): cria o volume do banco e reconstrói nele.
#        Depois: cd ~/projetos/marketplace && docker compose up -d db
#
#   --nuvem         busca tudo na nuvem (padrão: usa o que está na VM e, se não houver, a nuvem)
#   --manter-teste  no --teste, deixa o banco recuperado ligado na porta 5434 (a 5433 é do banco da Evolution) para olhar (apague depois com
#                   --limpar-teste)
#   --limpar-teste  apaga o contêiner e o volume de teste
set -euo pipefail

IMAGEM=postgres:18-alpine
VOL_PRODUCAO=marketplace_marketplace_pgdata
VOL_TESTE=marketplace-db-teste-dados
CONTEINER=marketplace-db-recuperando
LOCAL_BASES=/home/ubuntu/backup/marketplace-auto
LOCAL_WAL=/var/lib/docker/volumes/$VOL_PRODUCAO/_data/wal-arquivo

modo="" ate="" nuvem=0 manter=0
while [ $# -gt 0 ]; do
  case "$1" in
    --teste) modo=teste ;;
    --vm-nova) modo=vm-nova ;;
    --ate) ate="$2"; shift ;;
    --nuvem) nuvem=1 ;;
    --manter-teste) manter=1 ;;
    --limpar-teste)
      docker rm -f "$CONTEINER" >/dev/null 2>&1 || true
      docker volume rm "$VOL_TESTE" >/dev/null 2>&1 && echo "Banco de teste apagado." || echo "Não havia banco de teste."
      exit 0 ;;
    *) sed -n 2,22p "$0"; exit 1 ;;
  esac
  shift
done
[ -n "$modo" ] || { sed -n 2,22p "$0"; exit 1; }
[ "$(id -u)" = 0 ] || { echo "Rode com sudo."; exit 1; }

TMP=$(mktemp -d /var/tmp/recuperar-banco.XXXX)
trap 'docker rm -f "$CONTEINER" >/dev/null 2>&1 || true; rm -rf "$TMP"' EXIT
alvo_seg=$(date -d "${ate:-now}" +%s)

# ---------------------------------------------------------------- 1. a cópia base certa (a mais nova ANTES do alvo)
inicio_da_base() { tar -xzOf "$1" backup_label 2>/dev/null | sed -n 's/^START TIME: //p'; }
base=""
if [ "$nuvem" = 0 ] && ls "$LOCAL_BASES"/*/basebackup.tar.gz >/dev/null 2>&1; then
  for b in $(ls -1r "$LOCAL_BASES"/*/basebackup.tar.gz); do
    [ "$(date -d "$(inicio_da_base "$b")" +%s)" -lt "$alvo_seg" ] && { base=$b; break; }
  done
  fonte_wal=local
fi
if [ -z "$base" ]; then
  echo "▶ Buscando a cópia base na nuvem…"
  for dia in $(rclone lsf nuvem:marketplace --dirs-only | tr -d / | sort -r); do
    rclone copy "nuvem:marketplace/$dia/basebackup.tar.gz" "$TMP/base-$dia" 2>/dev/null || continue
    b="$TMP/base-$dia/basebackup.tar.gz"
    [ -f "$b" ] || continue
    if [ "$(date -d "$(inicio_da_base "$b")" +%s)" -lt "$alvo_seg" ]; then base=$b; break; fi
    rm -rf "$TMP/base-$dia"
  done
  fonte_wal=nuvem
fi
[ -n "$base" ] || { echo "ERRO: nenhuma cópia base anterior a ${ate:-agora}."; exit 1; }
echo "▶ Cópia base: $base (feita em $(inicio_da_base "$base"))"

# ---------------------------------------------------------------- 2. o registro contínuo (WAL)
if [ "$fonte_wal" = local ] && [ -d "$LOCAL_WAL" ]; then
  # Fecha o pedaço atual para a recuperação ir até agora (só quando a produção está nesta VM).
  docker exec marketplace-db psql -U marketplace -d postgres -qtAc "select pg_switch_wal()" >/dev/null 2>&1 && sleep 3 || true
  wal="$LOCAL_WAL"
else
  echo "▶ Buscando o registro contínuo na nuvem…"
  mkdir -p "$TMP/wal"; rclone copy nuvem:marketplace-wal "$TMP/wal" --transfers 8
  wal="$TMP/wal"
fi
echo "▶ Registro contínuo: $(ls "$wal" | grep -c '\.gz$') pedaços ($fonte_wal)"

# ---------------------------------------------------------------- 3. volume de destino
if [ "$modo" = vm-nova ]; then
  vol=$VOL_PRODUCAO
  if docker volume inspect "$vol" >/dev/null 2>&1; then
    echo "ERRO: já existe o volume $vol nesta VM. O --vm-nova é só para VM sem banco (nunca sobrescreve)."; exit 1
  fi
else
  vol=$VOL_TESTE
  docker volume rm "$vol" >/dev/null 2>&1 || true
fi
docker volume create "$vol" >/dev/null

# ---------------------------------------------------------------- 4. monta a cópia base e as instruções de recuperação
cp "$base" "$TMP/base.tar.gz"
alvo_conf=""
[ -n "$ate" ] && alvo_conf="recovery_target_time = '$(date -d "$ate" '+%F %T%z')'"
docker run --rm -v "$vol":/var/lib/postgresql -v "$TMP":/src:ro "$IMAGEM" sh -c "
  set -e; D=/var/lib/postgresql/18/docker
  mkdir -p \$D /var/lib/postgresql/wal-arquivo
  tar -xzf /src/base.tar.gz -C \$D
  touch \$D/recovery.signal
  cat >> \$D/postgresql.auto.conf <<EOF
# --- recuperação (infra/recuperar-banco.sh) ---
restore_command = 'test -f /wal-recuperar/%f.gz && gzip -dc /wal-recuperar/%f.gz > %p'
recovery_target_action = 'promote'
$alvo_conf
EOF
  chown -R postgres:postgres /var/lib/postgresql; chmod 700 \$D"

# ---------------------------------------------------------------- 5. aplica o registro até o fim (ou até o horário)
echo "▶ Reconstruindo…"
porta=(); [ "$modo" = teste ] && porta=(-p 127.0.0.1:5434:5432)
docker run -d --name "$CONTEINER" "${porta[@]}" -v "$vol":/var/lib/postgresql -v "$wal":/wal-recuperar:ro \
  "$IMAGEM" postgres -c archive_mode=off -c listen_addresses='*' -c max_connections=300 >/dev/null
# (max_connections igual ao da produção: o PostgreSQL não recupera com menos do que o original tinha.)
for _ in $(seq 1 300); do
  [ "$(docker inspect -f '{{.State.Running}}' "$CONTEINER")" = true ] || break   # parou: mostra o erro já
  [ "$(docker exec "$CONTEINER" psql -U marketplace -d postgres -qtAc 'select pg_is_in_recovery()' 2>/dev/null)" = f ] && break
  sleep 2
done
[ "$(docker exec "$CONTEINER" psql -U marketplace -d postgres -qtAc 'select pg_is_in_recovery()' 2>/dev/null)" = f ] \
  || { echo "ERRO: a recuperação não terminou. Log:"; docker logs --tail 30 "$CONTEINER"; exit 1; }
echo "▶ Banco reconstruído. $(docker logs "$CONTEINER" 2>&1 | grep -E 'last completed transaction|recovery stopping' | tail -1 | sed 's/.*LOG: *//')"

# ---------------------------------------------------------------- 6. confere
contar() {
  docker exec "$1" psql -U marketplace -d marketplace -qtAc "
    select 'Empresas', count(*)::text from \"Empresas\" union all select 'Produtos', count(*)::text from \"Produtos\"
    union all select 'Vendas', count(*)::text from \"Vendas\" union all select 'Itens de venda', count(*)::text from \"ItensVenda\"
    union all select 'Lançamentos financeiros', count(*)::text from \"MovimentosFinanceiros\"
    union all select 'Última venda', coalesce(max(\"DataHora\")::text, '-') from \"Vendas\"" 2>/dev/null
}
if [ "$modo" = teste ]; then
  echo; echo "Comparação (recuperado × produção agora):"
  paste -d'|' <(contar "$CONTEINER") <(contar marketplace-db | cut -d'|' -f2) \
    | awk -F'|' '{ printf "  %-24s %-30s %-30s %s\n", $1, $2, $3, ($2==$3 ? "igual" : "diferente") }'
  [ -n "$ate" ] && echo "  (com --ate, o recuperado para no horário pedido: diferenças depois dele são esperadas)"
  if [ "$manter" = 1 ]; then
    trap 'rm -rf "$TMP"' EXIT
    echo; echo "Banco recuperado ligado em 127.0.0.1:5434 (contêiner $CONTEINER). Apagar: sudo $0 --limpar-teste"
  else
    docker rm -f "$CONTEINER" >/dev/null; docker volume rm "$vol" >/dev/null
    echo; echo "Teste concluído; banco de teste apagado."
  fi
else
  contar "$CONTEINER" | sed 's/^/  /'
  docker rm -f "$CONTEINER" >/dev/null
  # Tira as linhas de recuperação: o banco segue normal no compose.
  docker run --rm -v "$vol":/var/lib/postgresql "$IMAGEM" sh -c \
    "sed -i '/# --- recuperação/,\$d' /var/lib/postgresql/18/docker/postgresql.auto.conf"
  echo; echo "Banco pronto no volume $vol. Agora: cd ~/projetos/marketplace && docker compose up -d db"
fi
