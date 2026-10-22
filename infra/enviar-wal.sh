#!/bin/bash
# Envio CONTÍNUO do banco para a nuvem (redundância fase 1, 19/10/2026). Roda como root pelo backup-wal.timer,
# a cada 2 minutos (desde 20/10/2026; antes 5). Com o archive_timeout de 2 min: perda máxima de ~4 min.
#
# O PostgreSQL guarda cada pedaço fechado do registro de alterações (WAL, compactado) em wal-arquivo/ dentro do
# volume do banco (ver compose.yaml: archive_command). Este script leva os pedaços novos para a nuvem da Oracle,
# CRIPTOGRAFADOS pelo rclone (mesmo "nuvem:" do backup-nuvem.sh). Com a cópia base diária (basebackup.tar.gz em
# ~/backup/marketplace-auto) e estes pedaços, o infra/recuperar-banco.sh reconstrói o banco até o último minuto.
#
# Vai para as duas nuvens: nuvem: (Oracle) e nuvem2: (Cloudflare R2, outra empresa; infra/configurar-r2.sh).
# Só os pedaços novos desde o último envio bom de cada uma (marca em /var/lib/enviar-wal): assim não lista a nuvem
# inteira a cada 2 min (as nuvens cobram por requisição acima da cota grátis).
#
# Guarda: 8 dias na VM, 31 dias na nuvem (a nuvem tem as cópias base de 30 dias).
# Último resultado: ~/backup/wal-ultimo.txt
set -euo pipefail

ARQUIVO=/var/lib/docker/volumes/marketplace_marketplace_pgdata/_data/wal-arquivo
LOG=/home/ubuntu/backup/wal-ultimo.txt

MARCAS=/var/lib/enviar-wal
mkdir -p "$MARCAS"

# Envia para um destino os pedaços mais novos que a marca dele; a marca só anda se o envio deu certo. A nova marca
# fica 1 min no passado: um pedaço que o PostgreSQL termina de gravar bem na hora vai de novo na próxima (repetir não
# faz mal), em vez de ficar de fora.
enviar() {
  local destino=$1 marca="$MARCAS/$1" nova="$MARCAS/$1.nova" lista="$MARCAS/$1.lista"
  touch -d '-1 minute' "$nova"
  if [ -f "$marca" ]; then
    (cd "$ARQUIVO" && find . -maxdepth 1 -name '*.gz' -newer "$marca" -printf '%f\n') > "$lista"
  else
    (cd "$ARQUIVO" && find . -maxdepth 1 -name '*.gz' -printf '%f\n') > "$lista"
  fi
  if [ -s "$lista" ]; then
    rclone copy "$ARQUIVO" "$destino:marketplace-wal" --files-from "$lista" --no-traverse --transfers 4 \
      2> "/tmp/enviar-wal-$destino.erro" || { rm -f "$nova"; return 1; }
  fi
  mv "$nova" "$marca"
}

# Cada nuvem por conta própria: a Oracle fora do ar (ou a conta suspensa) não pode impedir o envio para o R2 — é
# justamente para esse caso que o R2 existe. Antes (até 22/10) uma falha na Oracle parava o script antes do R2.
falhou=0
if enviar nuvem; then oracle="Oracle ok"; else oracle="Oracle FALHOU: $(tail -1 /tmp/enviar-wal-nuvem.erro)"; falhou=1; fi
r2="sem R2"
if rclone listremotes | grep -qx 'nuvem2:'; then
  if enviar nuvem2; then r2="R2 ok"; else r2="R2 FALHOU: $(tail -1 /tmp/enviar-wal-nuvem2.erro)"; falhou=1; fi
fi

# Limpeza: na VM, o que tem mais de 8 dias; na nuvem, uma vez por dia (madrugada), o que tem mais de 31.
find "$ARQUIVO" -name '*.gz' -mtime +8 -delete
if [ "$(date +%H%M)" \< "0505" ] && [ "$(date +%H)" = "05" ]; then
  rclone delete nuvem:marketplace-wal --min-age 31d || true
  if rclone listremotes | grep -qx 'nuvem2:'; then rclone delete nuvem2:marketplace-wal --min-age 31d || true; fi
fi

situacao=$([ "$falhou" = 0 ] && echo ok || echo FALHOU)
echo "$(date '+%F %T') $situacao · $(find "$ARQUIVO" -name '*.gz' | wc -l) pedaços na VM ($(du -sh "$ARQUIVO" | cut -f1)) · último: $(ls -1 "$ARQUIVO" | grep '\.gz$' | tail -1) · $oracle · $r2" > "$LOG"
exit $falhou
chown ubuntu:ubuntu "$LOG"
