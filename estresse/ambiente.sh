#!/usr/bin/env bash
# Ambiente ISOLADO do teste de estresse. Não toca na produção (banco "marketplace", serviço "marketplace")
# nem no dev (banco "marketplace_dev"): usa o banco "marketplace_estresse" no mesmo PostgreSQL e a API
# compilada em Release na porta 5400 (só 127.0.0.1), sem simulador, sem n8n e sem demonstração.
#
#   ./estresse/ambiente.sh montar      compila, cria o banco e liga a API (aplica as migrations)
#   ./estresse/ambiente.sh iniciar     liga a API (já montada)
#   ./estresse/ambiente.sh parar       desliga a API
#   ./estresse/ambiente.sh status      API no ar? tamanho do banco?
#   ./estresse/ambiente.sh desmontar   desliga, APAGA o banco marketplace_estresse e a pasta ~/estresse
set -euo pipefail
RAIZ=$(cd "$(dirname "$0")/.." && pwd)
PASTA="$HOME/estresse"
PORTA=5400
BANCO=marketplace_estresse
ENV="$PASTA/estresse.env"
PID="$PASTA/api.pid"

psql_() { docker exec marketplace-db psql -U marketplace -d "${2:-postgres}" -tAc "$1"; }

no_ar() { curl -s -o /dev/null -m 3 -w "%{http_code}" "http://127.0.0.1:$PORTA/api/saude" | grep -q 200; }

iniciar() {
  [[ -f "$ENV" ]] || { echo "Ambiente não montado: rode './estresse/ambiente.sh montar'."; exit 1; }
  if [[ -f "$PID" ]] && kill -0 "$(cat "$PID")" 2>/dev/null; then echo "API de estresse já está no ar (pid $(cat "$PID"))."; return; fi
  (
    # Linha a linha, como o systemd (com "source", os ";" da conexão do banco cortariam a linha).
    while IFS= read -r linha; do [[ $linha =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] && export "$linha"; done < "$ENV"
    cd "$PASTA/app" && nohup dotnet Marketplace.Api.dll >> "$PASTA/api.log" 2>&1 &
    echo $! > "$PID"
  )
  printf "Ligando a API de estresse na porta %s" "$PORTA"
  for _ in $(seq 1 150); do no_ar && { echo " — no ar."; return; }; printf "."; sleep 2; done
  echo; echo "A API não respondeu. Últimas linhas de $PASTA/api.log:"; tail -20 "$PASTA/api.log"; exit 1
}

parar() {
  if [[ -f "$PID" ]] && kill -0 "$(cat "$PID")" 2>/dev/null; then
    kill "$(cat "$PID")"; sleep 2; echo "API de estresse desligada."
  else echo "API de estresse não estava no ar."; fi
  rm -f "$PID"
}

case "${1:-}" in
  montar)
    mkdir -p "$PASTA/app" "$PASTA/chaves" "$PASTA/resultados"
    echo "▶ 1/3 Compilando a API (Release)…"
    dotnet publish "$RAIZ/Marketplace.Api" -c Release -o "$PASTA/app" --nologo -v q
    echo "▶ 2/3 Banco $BANCO…"
    psql_ "select 1 from pg_database where datname='$BANCO'" | grep -q 1 || docker exec marketplace-db createdb -U marketplace "$BANCO"
    echo "▶ 3/3 Configuração ($ENV)…"
    # A conexão da produção trocando SÓ o nome do banco. Trava de segurança: sem o nome novo, não segue.
    CONEXAO=$(sudo grep '^ConnectionStrings__Marketplace=' /etc/marketplace/marketplace.env | cut -d= -f2- \
      | sed -E "s/(Database=)marketplace(;|$)/\1$BANCO\2/")
    if [[ "$CONEXAO" != *"Database=$BANCO"* ]]; then echo "ERRO: não consegui apontar a conexão para $BANCO. Nada foi ligado."; exit 1; fi
    umask 077
    cat > "$ENV" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:$PORTA
ConnectionStrings__Marketplace=$CONEXAO
Banco__MigrarAoIniciar=true
DataProtection__Pasta=$PASTA/chaves
Simulador__Ativo=false
Demonstracao__Ativa=false
Publico__RedeInternaSoDono=false
Publico__HostClientes=127.0.0.1
Integracao__ChaveApi=$(head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9')
Integracao__N8nInsightsUrl=http://127.0.0.1:9/sem-n8n-no-estresse
DOTNET_CLI_TELEMETRY_OPTOUT=1
DOTNET_NOLOGO=1
EOF
    iniciar
    echo "Pronto. Próximo passo: node estresse/mercados.mjs 50   (cria 50 mercados de teste)";;
  iniciar) iniciar;;
  parar) parar;;
  status)
    if no_ar; then echo "API de estresse: no ar (http://127.0.0.1:$PORTA)"; else echo "API de estresse: fora do ar"; fi
    psql_ "select 1 from pg_database where datname='$BANCO'" | grep -q 1 \
      && echo "Banco $BANCO: $(psql_ "select pg_size_pretty(pg_database_size('$BANCO'))") · mercados de teste: $(psql_ "select count(*) from \"Empresas\" where \"Subdominio\" like 'estresse%'" "$BANCO" 2>/dev/null || echo '?')" \
      || echo "Banco $BANCO: não existe";;
  desmontar)
    parar
    psql_ "drop database if exists $BANCO with (force)" >/dev/null && echo "Banco $BANCO apagado."
    rm -rf "$PASTA" && echo "Pasta $PASTA apagada (resultados inclusos: copie antes o que quiser guardar).";;
  *) sed -n 2,11p "$0"; exit 1;;
esac
