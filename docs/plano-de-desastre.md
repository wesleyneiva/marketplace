# Plano de desastre do Marcus

Redundância fase 1 (19/10/2026; 20/10: 2 min e segunda nuvem): **perda máxima de ~4 minutos de dados** e **volta em
30 a 60 minutos** numa VM nova, mesmo que esta VM **ou a conta inteira da Oracle** suma de vez. Testado em 19/10/2026:
reconstrução completa só com a nuvem em 6 segundos; em 20/10/2026, **só com o R2**: igual à produção até a última venda.

## O que existe

| O quê | Quando | Onde fica | Por quanto tempo |
|---|---|---|---|
| Registro contínuo do banco (WAL, compactado) | sempre; cada pedaço fecha em no máximo 2 min | VM (`wal-arquivo` no volume do banco) → as 2 nuvens a cada 2 min (`backup-wal.timer`) | VM 8 dias · nuvem 31 dias |
| Cópia base do PostgreSQL inteiro (`basebackup.tar.gz`) | todo dia 03:45 | `~/backup/marketplace-auto/AAAA-MM-DD` → nuvem 04:30 | VM 7 dias · nuvem 30 dias |
| Cópia "fotografia" do banco do Marcus (`marketplace.dump`) | todo dia 03:45 | idem | idem |
| Configuração da VM (`configuracao.tar.gz`): `marketplace.env`, chaves do DataProtection, token do túnel da Cloudflare, `.env` do banco, serviços e rotinas do systemd, `~/scripts` | todo dia 03:45 | idem | idem |
| Backup do n8n | todo dia 03:30 | `~/backup/n8n-auto` → nuvem | VM 7 · nuvem 30 |

Nuvem = **duas**, com os mesmos arquivos, **criptografados pelo rclone antes de sair da VM** (as mesmas 2 senhas
abrem as duas):
- `nuvem:` → Oracle Object Storage (bucket `backup-oracle-a1`, São Paulo, grátis até 20 GB) — mesma conta da VM.
- `nuvem2:` → Cloudflare R2 (bucket `marcus-backup`, grátis até 10 GB) — **outra empresa**: vale se a conta da
  Oracle for suspensa. Configurado por `sudo infra/configurar-r2.sh` (20/10/2026).

Ver: `sudo rclone lsd nuvem:` / `sudo rclone lsd nuvem2:` · último envio: `~/backup/nuvem-ultimo.txt` e
`~/backup/wal-ultimo.txt` (termina em "R2 ok"). Na área do dono → Sistema, o card "Backup contínuo" fica vermelho
se o envio parar.

Os caixas continuam vendendo sem o servidor (modo offline) e mandam as vendas quando ele volta: venda feita no
caixa durante a queda **não se perde**. O que pode se perder são os ~4 min **antes** da queda que o servidor já tinha
confirmado e ainda não tinham subido para a nuvem (vendas online, cadastros, lançamentos): o caixa não guarda de novo
o que o servidor confirmou. Essas vendas têm o cupom impresso e podem ser relançadas à mão.

## Guardar FORA da VM (gerenciador de senhas) — sem isto, nada abre

1. **As 2 senhas da criptografia do rclone** (`password` e `password2` do `/root/.config/rclone/rclone.conf`).
   Para ver, no terminal da VM (só você vê):
   `sudo bash -c 'for k in password password2; do v=$(grep "^$k = " /root/.config/rclone/rclone.conf | cut -d" " -f3); echo "$k: $(rclone reveal $v)"; done'`
2. **Chave de acesso da Oracle para o bucket** (Customer Secret Key: o ID e a chave secreta) e o **namespace**
   do Object Storage (aparece na página do bucket).
3. **Token do R2 da Cloudflare**: Account ID, Access Key ID e Secret Access Key (a secreta só aparece uma vez,
   ao criar o token).
4. Login da Oracle Cloud, do GitHub (repositório do Marcus) e da Cloudflare.

## Situação A — alguém apagou ou estragou dados (a VM está bem)

```bash
cd ~/projetos/marketplace
sudo infra/recuperar-banco.sh --teste --ate "2026-10-19 15:30" --manter-teste   # banco de antes do erro, à parte
```

O banco recuperado fica em `127.0.0.1:5434` (contêiner `marketplace-db-recuperando`) para conferir e copiar só o
que se perdeu (ex.: `pg_dump -h 127.0.0.1 -p 5434 -U marketplace -d marketplace -t '"Produtos"'`). Depois:
`sudo infra/recuperar-banco.sh --limpar-teste`. Trocar o banco inteiro da produção por um recuperado: fazer com
calma, com backup antes (parar o serviço, renomear o volume, `--vm-nova`, subir).

## Situação B — a VM morreu ou a conta sumiu (do zero, 30 a 60 min)

**Se foi a conta da Oracle que caiu**, a VM nova vai para outro provedor (Hetzner, DigitalOcean, AWS, Magalu
Cloud…) e os backups vêm do **R2**: no passo 3 crie `r2`/`nuvem2` em vez de `oracle`/`nuvem` e, nos passos 4 e 5,
troque `nuvem:` por `nuvem2:` e use `--remoto nuvem2`. O resto é igual.

1. **VM nova**: Ubuntu (ARM ou x86), 4 núcleos / 8 GB ou mais, disco de 100 GB. Liberar só o SSH.
2. **Programas**:
   ```bash
   sudo apt-get update && sudo apt-get install -y git rclone docker.io docker-compose-v2   # ou Docker CE oficial
   sudo usermod -aG docker ubuntu                                                          # sair e entrar de novo
   # .NET 10 SDK: https://dot.net/v1/dotnet-install.sh  ·  Node 24 (nvm)  ·  cloudflared: pacote .deb da Cloudflare
   ```
3. **Acesso à nuvem** (rclone, como root): criar `oracle` (S3, provider Other, endpoint
   `https://<namespace>.compat.objectstorage.sa-saopaulo-1.oraclecloud.com`, região `sa-saopaulo-1`, ID e chave
   secreta) e `nuvem` (crypt sobre `oracle:backup-oracle-a1`, com as **2 senhas** guardadas):
   ```bash
   sudo rclone config create oracle s3 provider Other access_key_id <ID> secret_access_key <SECRETA> \
        endpoint https://<namespace>.compat.objectstorage.sa-saopaulo-1.oraclecloud.com region sa-saopaulo-1
   sudo rclone config create nuvem crypt remote oracle:backup-oracle-a1 password <SENHA1> password2 <SENHA2>
   sudo rclone lsd nuvem:          # tem que listar n8n, marketplace e marketplace-wal
   ```
   Pelo R2 (conta da Oracle fora): as mesmas 2 senhas, com o token do R2:
   ```bash
   sudo rclone config create r2 s3 provider Cloudflare access_key_id <ID> secret_access_key <SECRETA> \
        endpoint https://<ACCOUNT_ID>.r2.cloudflarestorage.com region auto no_check_bucket true
   sudo rclone config create nuvem2 crypt remote r2:marcus-backup password <SENHA1> password2 <SENHA2>
   sudo rclone lsd nuvem2:
   ```
4. **Código e configuração**:
   ```bash
   mkdir -p ~/projetos && git clone <repositório do Marcus> ~/projetos/marketplace
   DIA=$(sudo rclone lsf nuvem:marketplace --dirs-only | sort | tail -1)
   sudo rclone copy "nuvem:marketplace/${DIA}configuracao.tar.gz" /tmp/
   sudo tar -xzf /tmp/configuracao.tar.gz -C /        # env, chaves, token do túnel, .env, serviços, ~/scripts
   sudo chown -R ubuntu:ubuntu ~/projetos ~/scripts ~/.local/share/marketplace
   ```
5. **Banco até o último minuto** (busca a cópia base e o registro contínuo na nuvem):
   ```bash
   cd ~/projetos/marketplace
   sudo infra/recuperar-banco.sh --vm-nova --nuvem                    # pela Oracle
   sudo infra/recuperar-banco.sh --vm-nova --nuvem --remoto nuvem2    # ou pelo R2
   docker compose up -d db
   ```
6. **Sistema no ar**:
   ```bash
   cd ~/projetos/marketplace/marketplace-web && npm ci && cd ..
   ./publicar.sh
   sudo systemctl daemon-reload
   sudo systemctl enable --now marketplace cloudflared backup-marketplace.timer backup-nuvem.timer backup-wal.timer
   ```
   O túnel da Cloudflare usa o mesmo token: **app.wnlabs.com.br volta a apontar para a VM nova sozinho**.
7. **Conferir**: `curl http://127.0.0.1:5100/api/saude`, abrir https://app.wnlabs.com.br, ver a última venda e
   `~/backup/wal-ultimo.txt` depois de 2 min. Se a VM nova não tiver o `r2`/`nuvem2` (ou o `nuvem`), criar o que
   falta: os backups voltam a ir para as duas nuvens sozinhos.

Fora do Marcus (n8n, Evolution, site PROMO FEET): o n8n tem backup próprio em `nuvem:n8n`; os demais se reinstalam.

## Treino (uma vez por mês, 1 minuto)

```bash
cd ~/projetos/marketplace && sudo infra/recuperar-banco.sh --teste --nuvem
cd ~/projetos/marketplace && sudo infra/recuperar-banco.sh --teste --nuvem --remoto nuvem2   # e pelo R2
```

Tem que dizer "igual" em tudo, menos as vendas dos últimos minutos (ainda não subiram). Se falhar, os backups
não servem: corrigir na hora.

## Próximo passo (fase 2)

Segunda VM em outro provedor com o banco copiado em tempo real e o mesmo túnel da Cloudflare: volta em 2 a 5
minutos, perdendo segundos. Fazer junto com o primeiro cliente pagante.
