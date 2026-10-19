#!/bin/bash
# Envio CONTÍNUO do banco para a nuvem (redundância fase 1, 19/10/2026). Roda como root pelo backup-wal.timer,
# a cada 5 minutos.
#
# O PostgreSQL guarda cada pedaço fechado do registro de alterações (WAL, compactado) em wal-arquivo/ dentro do
# volume do banco (ver compose.yaml: archive_command). Este script leva os pedaços novos para a nuvem da Oracle,
# CRIPTOGRAFADOS pelo rclone (mesmo "nuvem:" do backup-nuvem.sh). Com a cópia base diária (basebackup.tar.gz em
# ~/backup/marketplace-auto) e estes pedaços, o infra/recuperar-banco.sh reconstrói o banco até o último minuto.
#
# Guarda: 8 dias na VM, 31 dias na nuvem (a nuvem tem as cópias base de 30 dias).
# Último resultado: ~/backup/wal-ultimo.txt
set -euo pipefail

ARQUIVO=/var/lib/docker/volumes/marketplace_marketplace_pgdata/_data/wal-arquivo
LOG=/home/ubuntu/backup/wal-ultimo.txt

if ! rclone copy "$ARQUIVO" nuvem:marketplace-wal --include '*.gz' --transfers 4 2> /tmp/enviar-wal.erro; then
  { echo "$(date '+%F %T') FALHOU ao enviar:"; cat /tmp/enviar-wal.erro; } > "$LOG"
  exit 1
fi

# Limpeza: na VM, o que tem mais de 8 dias; na nuvem, uma vez por dia (madrugada), o que tem mais de 31.
find "$ARQUIVO" -name '*.gz' -mtime +8 -delete
if [ "$(date +%H%M)" \< "0505" ] && [ "$(date +%H)" = "05" ]; then
  rclone delete nuvem:marketplace-wal --min-age 31d || true
fi

echo "$(date '+%F %T') ok · $(find "$ARQUIVO" -name '*.gz' | wc -l) pedaços na VM ($(du -sh "$ARQUIVO" | cut -f1)) · último: $(ls -1 "$ARQUIVO" | grep '\.gz$' | tail -1)" > "$LOG"
chown ubuntu:ubuntu "$LOG"
