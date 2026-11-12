#!/usr/bin/env bash
# Prints a cores.lock.json for review. It never edits the repo and never trusts a hash it computed
# itself: every download is compared with a digest published for it, and the script stops on any mismatch
# or when no published digest exists (then verify by hand).
#
#   scripts/cores/make-lock.sh <core-bin commit SHA> <xray version> <sing-box version> <mihomo version>
#   e.g. ... bc603e1ee3c6eb90f16fe9123f7df80bc02f8435 26.10.31 1.14.3 1.19.29   (versions without the "v")
#
# Needs: gh (logged in; used for the GitHub API), git, curl, sha256sum, unzip. No jq.
# Evidence per asset:
#   - GitHub's own recorded digest of the release asset (release API "digest"), compared with the download;
#   - Xray only: the maintainers' separate <asset>.dgst file, which must say the same SHA-256.
# The core-bin bundle has no upstream digest: it is checked against the git blob id GitHub records for
# that path at the pinned commit (so the file is exactly what the commit contains). It supplies only
# geo/rule data and EnableLoopback.exe: the cores in it are NOT used (see the provenance doc).
set -euo pipefail
[[ $# -eq 4 ]] || { sed -n '2,15p' "$0" >&2; exit 2; }
commit="$1"; xray="$2"; singbox="$3"; mihomo="$4"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || { echo "commit must be a full 40-character SHA" >&2; exit 2; }
for v in "$xray" "$singbox" "$mihomo"; do [[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "bad version: $v" >&2; exit 2; }; done

tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT

# asset <repo> <tag> <file>: downloads, compares with the published digest, prints the SHA-256.
asset() {
  local repo="$1" tag="$2" file="$3" want got dgst
  want=$(gh api "repos/$repo/releases/tags/$tag" --jq ".assets[] | select(.name==\"$file\") | .digest")
  [[ "$want" =~ ^sha256:[0-9a-f]{64}$ ]] || { echo "no published SHA-256 digest for $repo $tag $file" >&2; exit 1; }
  mkdir -p "$tmp/dl"
  gh release download "$tag" -R "$repo" -p "$file" -D "$tmp/dl" --clobber
  got=$(sha256sum "$tmp/dl/$file" | cut -d' ' -f1)
  [[ "sha256:$got" == "$want" ]] || { echo "MISMATCH $file: downloaded $got, published $want" >&2; exit 1; }
  if [[ "$repo" == "XTLS/Xray-core" ]]; then
    gh release download "$tag" -R "$repo" -p "$file.dgst" -D "$tmp/dl" --clobber
    dgst=$(sed -n 's/^SHA2-256= *//p' "$tmp/dl/$file.dgst")
    [[ "$dgst" == "$got" ]] || { echo "MISMATCH $file: maintainers' .dgst says $dgst" >&2; exit 1; }
  fi
  echo "ok $file $got" >&2
  echo "$got"
}

# bundle: the core-bin zip at the pinned commit, compared with the git blob id GitHub records for it.
bundle() {
  curl -fsSL --proto '=https' -o "$tmp/bundle.zip" "https://github.com/2dust/v2rayN-core-bin/raw/$commit/v2rayN-windows-64.zip"
  local want got
  want=$(gh api "repos/2dust/v2rayN-core-bin/contents/v2rayN-windows-64.zip?ref=$commit" --jq .sha)
  got=$(git hash-object "$tmp/bundle.zip")
  [[ "$got" == "$want" ]] || { echo "MISMATCH bundle: git blob $got, commit has $want" >&2; exit 1; }
  echo "ok bundle (git blob $got)" >&2
  sha256sum "$tmp/bundle.zip" | cut -d' ' -f1
}

b=$(bundle)
x64x=$(asset XTLS/Xray-core "v$xray" Xray-windows-64.zip)
x64s=$(asset SagerNet/sing-box "v$singbox" "sing-box-$singbox-windows-amd64.zip")
x64m=$(asset MetaCubeX/mihomo "v$mihomo" "mihomo-windows-amd64-v$mihomo.zip")
x86x=$(asset XTLS/Xray-core "v$xray" Xray-windows-32.zip)
x86s=$(asset SagerNet/sing-box "v$singbox" "sing-box-$singbox-windows-386.zip")
x86m=$(asset MetaCubeX/mihomo "v$mihomo" "mihomo-windows-386-v$mihomo.zip")

cat <<EOF
{
  "_comment": "The only core binaries a release may ship. Regenerate with scripts/cores/make-lock.sh and review every digest against the upstream release page.",
  "coreBin": { "commit": "$commit", "sha256": "$b" },
  "x64": {
    "xray": { "version": "$xray", "sha256": "$x64x" },
    "singBox": { "version": "$singbox", "sha256": "$x64s" },
    "mihomo": { "version": "$mihomo", "sha256": "$x64m" }
  },
  "x86": {
    "xray": { "version": "$xray", "sha256": "$x86x" },
    "singBox": { "version": "$singbox", "sha256": "$x86s" },
    "mihomo": { "version": "$mihomo", "sha256": "$x86m" }
  }
}
EOF
