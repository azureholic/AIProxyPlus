#!/bin/sh
set -eu

export VAULT_ADDR="http://127.0.0.1:8200"

local_state_path="/vault/file"
unseal_key_path="$local_state_path/unseal-key"
status_path="/tmp/vault-status.json"
init_path="/tmp/vault-init.json"
development_token="dev-only-root-token"

mkdir -p "$local_state_path/data"
umask 077

vault server -config=/vault/config/local.hcl &
vault_pid=$!

terminate_vault() {
    kill -TERM "$vault_pid" 2>/dev/null || true
    wait "$vault_pid" 2>/dev/null || true
}

trap terminate_vault INT TERM

while true; do
    status_code=0
    vault status -format=json > "$status_path" 2>/dev/null || status_code=$?

    if [ "$status_code" -eq 0 ] || [ "$status_code" -eq 2 ]; then
        break
    fi

    if ! kill -0 "$vault_pid" 2>/dev/null; then
        wait "$vault_pid"
        exit $?
    fi

    sleep 1
done

if grep -q '"initialized": false' "$status_path"; then
    vault operator init -key-shares=1 -key-threshold=1 -format=json > "$init_path"
    sed -n '/"unseal_keys_b64"/{n;s/^[[:space:]]*"\([^"]*\)".*/\1/p;}' "$init_path" > "$unseal_key_path"
    initial_root_token=$(sed -n 's/^[[:space:]]*"root_token": "\([^"]*\)".*/\1/p' "$init_path")

    if [ ! -s "$unseal_key_path" ] || [ -z "$initial_root_token" ]; then
        echo "Vault initialization output could not be parsed." >&2
        terminate_vault
        exit 1
    fi

    vault operator unseal "$(cat "$unseal_key_path")" > /dev/null
    VAULT_TOKEN="$initial_root_token" vault secrets enable -path=secret kv-v2 > /dev/null
    VAULT_TOKEN="$initial_root_token" vault policy write ai-proxy-local /vault/config/ai-proxy-policy.hcl > /dev/null
    VAULT_TOKEN="$initial_root_token" vault token create \
        -id="$development_token" \
        -orphan \
        -policy=ai-proxy-local \
        -no-default-policy > /dev/null
    VAULT_TOKEN="$initial_root_token" vault token revoke -self > /dev/null
    rm -f "$init_path"
elif [ -s "$unseal_key_path" ]; then
    vault operator unseal "$(cat "$unseal_key_path")" > /dev/null
else
    echo "Vault is initialized but its local unseal key is missing." >&2
    terminate_vault
    exit 1
fi

rm -f "$status_path"
wait "$vault_pid"