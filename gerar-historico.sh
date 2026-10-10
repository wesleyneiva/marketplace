#!/usr/bin/env bash
# Refaz o PASSADO da empresa de DEMONSTRAÇÃO no Marketplace publicado: vendas (sem mexer no estoque) e o
# financeiro delas (caixas lançados, depósitos, boletos de fornecedor, despesas fixas, maquininha).
# Dias que já têm vendas de verdade (PDV ou simulador ao vivo) ficam de fora.
# Uso:
#   ./gerar-historico.sh --dias 60 --substituir true
# Faça backup do banco antes. Usa a configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
# Lê o arquivo linha a linha, como o systemd faz (o "source" do bash cortaria a conexão do banco nos ";").
sudo --preserve-env=HOME bash -c '
  while IFS= read -r linha; do
    [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"
  done < /etc/marketplace/marketplace.env
  cd "$0" && exec dotnet Marketplace.Api.dll gerar-historico "$@"' "$APP" "$@"
