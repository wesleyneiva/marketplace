#!/bin/bash
# Backup diário do banco do Marketplace (roda como root pelo backup-marketplace.timer, às 03:45).
# Guarda em ~/backup/marketplace-auto/AAAA-MM-DD:
#   marketplace.dump  → cópia completa do PostgreSQL (formato "custom" do pg_dump: compactado)
#   basebackup.tar.gz → cópia base de TODO o PostgreSQL (para recuperar até o último minuto: infra/recuperar-banco.sh)
#   resumo.txt        → quantas linhas cada tabela tinha (para conferir de relance)
#   configuracao.tar.gz → o que NÃO está no banco nem no GitHub e faz falta numa VM nova (lista completa no tar abaixo):
#                         etc/marketplace/marketplace.env (senha do banco, chaves, endereços) e
#                         home/ubuntu/.local/share/marketplace/chaves (DataProtection: abre os tokens do Mercado Pago
#                         guardados criptografados e assina o login). Só o dono lê (600); na nuvem vai criptografado.
# Mantém os últimos 7 dias.
#
# Restaurar (CUIDADO: substitui o banco atual):
#   sudo systemctl stop marketplace
#   docker exec -i marketplace-db pg_restore -U marketplace -d marketplace --clean --if-exists < marketplace.dump
#   sudo systemctl start marketplace
# Restaurar a configuração numa VM nova (antes de ligar o serviço):
#   sudo tar -xzf configuracao.tar.gz -C /     (recria /etc/marketplace/marketplace.env e a pasta das chaves)
set -euo pipefail

BASE=/home/ubuntu/backup/marketplace-auto
MANTER=7
DESTINO="$BASE/$(date +%F)"
PARCIAL="$DESTINO.parcial"   # só vira a pasta do dia se tudo der certo

rm -rf "$PARCIAL"
mkdir -p "$PARCIAL"
chmod 700 "$BASE" "$PARCIAL"

# 1. Cópia consistente com o sistema no ar (o pg_dump lê uma "fotografia" do banco)
docker exec marketplace-db pg_dump -U marketplace -d marketplace -Fc > "$PARCIAL/marketplace.dump"

# 1b. Cópia BASE do banco inteiro (redundância fase 1, 19/10/2026): com os pedaços do registro de alterações que o
#     enviar-wal.sh leva à nuvem a cada 5 min, o infra/recuperar-banco.sh reconstrói o banco até o último minuto.
docker exec marketplace-db pg_basebackup -U marketplace -D - -Ft -z -X none -c fast > "$PARCIAL/basebackup.tar.gz" 2>/dev/null
gzip -t "$PARCIAL/basebackup.tar.gz" || { echo "Backup suspeito: cópia base corrompida" >&2; exit 1; }

# 2. Confere se o arquivo abre e tem os dados das tabelas
TABELAS=$(docker exec -i marketplace-db pg_restore --list < "$PARCIAL/marketplace.dump" | grep -c "TABLE DATA" || true)
if [ "$TABELAS" -lt 10 ]; then
    echo "Backup suspeito: só $TABELAS tabelas com dados no arquivo" >&2
    exit 1
fi

# 3. Resumo: linhas por tabela (estimativa rápida do PostgreSQL, atualizada antes)
docker exec marketplace-db psql -U marketplace -d marketplace -qtA -c "ANALYZE" \
    -c "SELECT format('%-22s %8s', relname, n_live_tup) FROM pg_stat_user_tables ORDER BY relname" \
    > "$PARCIAL/resumo.txt"

# 4. Configuração e chaves (fora do banco e do Git)
# Desde 19/10/2026 também o que uma VM NOVA precisa (docs/plano-de-desastre.md): token do túnel da Cloudflare,
# senha do banco do compose (.env), os serviços e rotinas do systemd e os scripts de backup.
tar -czf "$PARCIAL/configuracao.tar.gz" -C / etc/marketplace/marketplace.env home/ubuntu/.local/share/marketplace/chaves \
    etc/cloudflared/token home/ubuntu/projetos/marketplace/.env etc/systemd/system/marketplace.service etc/systemd/system/cloudflared.service \
    $(cd / && ls -d etc/systemd/system/backup-*.service etc/systemd/system/backup-*.timer) home/ubuntu/scripts
tar -tzf "$PARCIAL/configuracao.tar.gz" | grep -q "chaves/key-" || { echo "Backup suspeito: sem as chaves" >&2; exit 1; }

chown -R ubuntu:ubuntu "$BASE"
chmod 600 "$PARCIAL/configuracao.tar.gz" "$PARCIAL/basebackup.tar.gz"
rm -rf "$DESTINO"
mv "$PARCIAL" "$DESTINO"

# 5. Apaga os mais antigos, mantendo os últimos $MANTER dias
ls -1d "$BASE"/20??-??-?? | head -n -"$MANTER" | xargs -r rm -rf

echo "Backup ok: $TABELAS tabelas, $(du -sh "$DESTINO" | cut -f1) em $DESTINO"
