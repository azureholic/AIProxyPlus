path "secret/data/aiproxyplus/*" {
  capabilities = ["create", "read", "update", "delete", "list"]
}

path "secret/metadata/aiproxyplus/*" {
  capabilities = ["read", "delete", "list"]
}