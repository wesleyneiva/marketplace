#!/usr/bin/env bash
# Cadastra um CLIENTE novo (empresa + administrador com senha provisória) no Marketplace publicado.
# Uso:
#   ./nova-empresa.sh --nome "Mercado do Zé" --subdominio mercadoze --caixas 2 \
#                     --admin-email ze@mercadoze.com.br --admin-nome "José da Silva"
# Usa a mesma configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
# Lê o arquivo linha a linha, como o systemd faz (com "source" do bash, os ";" da conexão do banco
# cortariam a linha e o programa tentaria entrar no banco como "root").
sudo --preserve-env=HOME bash -c '
  while IFS= read -r linha; do
    [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"
  done < /etc/marketplace/marketplace.env
  cd "$0" && exec dotnet Marketplace.Api.dll nova-empresa "$@"' "$APP" "$@"
