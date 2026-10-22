#!/bin/bash
# TREINO de restaurar uma loja (22/10/2026). Só no banco de DEV (marketplace_dev), loja #13 "Loja Teste Tênis"
# (a TopSpin não existe no dev). A produção não é tocada. Passo a passo em docs/plano-de-desastre.md → Treino B.
#
#   infra/treino-restaurar-loja.sh foto       ← guarda como a loja está (rodar ANTES do estrago)
#   infra/treino-restaurar-loja.sh estragar   ← o "acidente": zera o estoque de tudo e apaga clientes
#   infra/treino-restaurar-loja.sh conferir   ← compara a loja de agora com a foto
#
# No meio: sudo infra/restaurar-loja.sh --banco marketplace_dev --empresa 13 --ate "<horário da foto>" (e --gravar).
set -euo pipefail

BANCO=marketplace_dev
EMPRESA=13
FOTO=/home/ubuntu/backup/treino-loja-foto.txt

sql() { docker exec marketplace-db psql -U marketplace -d "$BANCO" -qtAX -v ON_ERROR_STOP=1 -c "$1"; }
retrato() {
  sql "select 'Produtos:         ' || count(*) from \"Produtos\" where \"EmpresaId\" = $EMPRESA
       union all select 'Estoque somado:   ' || coalesce(sum(\"EstoqueAtual\"), 0) from \"Produtos\" where \"EmpresaId\" = $EMPRESA
       union all select 'Clientes:         ' || count(*) from \"Clientes\" where \"EmpresaId\" = $EMPRESA
       union all select 'Vendas:           ' || count(*) from \"Vendas\" where \"EmpresaId\" = $EMPRESA"
}

case "${1:-}" in
  foto)
    { echo "Foto de $(date '+%F %H:%M:%S')"; retrato; } | tee "$FOTO"
    echo; echo "Guardada. Anote o horário acima, ESPERE 1 MINUTO e rode: $0 estragar" ;;
  estragar)
    [ -f "$FOTO" ] || { echo "Tire a foto antes: $0 foto"; exit 1; }
    sql "begin;
      update \"Produtos\" set \"EstoqueAtual\" = 0 where \"EmpresaId\" = $EMPRESA;
      delete from \"Clientes\" c where \"EmpresaId\" = $EMPRESA
        and not exists (select 1 from \"Vendas\" v where v.\"ClienteId\" = c.\"Id\")
        and not exists (select 1 from \"OrdensServico\" o where o.\"ClienteId\" = c.\"Id\")
        and not exists (select 1 from \"LancamentosFiado\" f where f.\"ClienteId\" = c.\"Id\");
      commit;"
    echo "💥 $(date '+%H:%M:%S') — estoque zerado e clientes apagados na loja #$EMPRESA (dev). Como ficou:"; retrato ;;
  conferir)
    [ -f "$FOTO" ] || { echo "Não há foto ($FOTO)."; exit 1; }
    retrato > /tmp/treino-agora.txt
    printf "  %-28s %s\n" "$(head -1 "$FOTO")" "Agora"
    paste -d'|' <(tail -n +2 "$FOTO") /tmp/treino-agora.txt | while IFS='|' read -r antes agora; do
      marca="ok"; [ "$antes" = "$agora" ] || marca="DIFERENTE"
      printf "  %-28s %-10s %s\n" "$antes" "${agora##* }" "$marca"
    done
    rm -f /tmp/treino-agora.txt
    if [ "$(tail -n +2 "$FOTO")" = "$(retrato)" ]; then echo "✅ Igual à foto: restauração certa."; else echo "⚠ Diferente da foto."; fi ;;
  *) sed -n 2,9p "$0"; exit 1 ;;
esac
