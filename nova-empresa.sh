#!/usr/bin/env bash
# Cadastra um CLIENTE novo (empresa + administrador com senha provisória) no Marketplace publicado.
# Uso:
#   ./nova-empresa.sh --nome "Mercado do Zé" --subdominio mercadoze --caixas 2 \
#                     --admin-email ze@mercadoze.com.br --admin-nome "José da Silva"
# Usa a mesma configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
sudo --preserve-env=HOME bash -c 'set -a; . /etc/marketplace/marketplace.env; set +a; cd "$0" && exec dotnet Marketplace.Api.dll nova-empresa "$@"' "$APP" "$@"
