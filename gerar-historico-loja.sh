#!/usr/bin/env bash
# Gera o PASSADO de uma LOJA DE ESPORTE em teste (vendas sem mexer no estoque, clientes, OS e financeiro).
# Só preenche dias sem nenhuma venda. Contas fixas copiadas das da implantação (menos a mensalidade do Marcus) e o DAS.
# Uso:
#   ./gerar-historico-loja.sh --empresa 5 --de 2026-08-01 --ate 2026-10-20
# Faça backup do banco antes. Usa a configuração do serviço (/etc/marketplace/marketplace.env, que só o root lê).
set -euo pipefail
APP="$HOME/servicos/marketplace/app"
# Lê o arquivo linha a linha, como o systemd faz (o "source" do bash cortaria a conexão do banco nos ";").
sudo --preserve-env=HOME bash -c '
  while IFS= read -r linha; do
    [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"
  done < /etc/marketplace/marketplace.env
  cd "$0" && exec dotnet Marketplace.Api.dll gerar-historico-loja "$@"' "$APP" "$@"
