#!/usr/bin/env bash
# Creates Glide's update signing key (ECDSA P-256) for S-02. Run it once, yourself, on a trusted machine
# (Git Bash has openssl). The public half goes into UpdateSignatureKey.cs; the private half becomes the UPDATE_SIGNING_KEY secret of the release
# environment and is never committed.
#
#   scripts/signing/new-update-signing-key.sh <folder outside any git checkout>
set -euo pipefail

out="${1:-}"
if [[ -z "$out" ]]; then
  echo "usage: $0 <folder outside any git checkout>" >&2
  exit 2
fi
check="$out"
while [[ ! -d "$check" ]]; do check="$(dirname "$check")"; done
if git -C "$check" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "refusing: $out is inside a git checkout, the private key must never be committed" >&2
  exit 1
fi
priv="$out/glide-update-signing-key.pem"
pub="$out/glide-update-signing-public.pem"
if [[ -e "$priv" || -e "$pub" ]]; then
  echo "refusing: $out already holds a key; a new key would lock out every installed Glide" >&2
  exit 1
fi

mkdir -p "$out"
(umask 077; openssl ecparam -name prime256v1 -genkey -noout -out "$priv")
openssl ec -in "$priv" -pubout -out "$pub" 2>/dev/null

# Self-check: sign and verify a sample with the new pair.
sample="$(mktemp)"; echo "glide" > "$sample"
openssl dgst -sha256 -sign "$priv" -out "$sample.sig" "$sample"
openssl dgst -sha256 -verify "$pub" -signature "$sample.sig" "$sample" >/dev/null
rm -f "$sample" "$sample.sig"

echo "Private key (keep offline, back it up twice): $priv"
echo "Public key  (embed in UpdateSignatureKey.PublicKeyPem):  $pub"
echo
cat "$pub"
