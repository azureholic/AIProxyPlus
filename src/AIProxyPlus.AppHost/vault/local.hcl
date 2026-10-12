# Local development only. Vault caps token TTLs at 32 days by default, so without this the dev token created by
# bootstrap.sh silently expires after a month and the API starts getting 'invalid token' from Vault. A 10 year
# default and maximum keeps the token valid for the life of the local volume.
default_lease_ttl = "87600h"
max_lease_ttl     = "87600h"

ui = true
disable_mlock = true

storage "file" {
  path = "/vault/file/data"
}

listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = true
}