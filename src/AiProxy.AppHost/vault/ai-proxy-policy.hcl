path "secret/data/ai-proxy/*" {
  capabilities = ["create", "read", "update", "delete", "list"]
}

path "secret/metadata/ai-proxy/*" {
  capabilities = ["read", "delete", "list"]
}