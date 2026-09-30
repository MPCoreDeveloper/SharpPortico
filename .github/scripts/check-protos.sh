#!/usr/bin/env bash
#
# Compiles every sample's descriptor with protoc.
#
# The generator emits the descriptor as a string const and no compiler ever reads it, so a descriptor that
# stops being valid protobuf - a field name protoc rejects, an import left out - builds green and fails in
# the hands of whoever feeds {Service}Proto.Text to protoc, to a client generator or to a reviewer. This asks
# protoc's opinion of every descriptor the samples produce, so the build has to agree with the contract it
# ships before anyone else reads it.
#
# Usage: check-protos.sh <sharpportico-cli-dll> [protoc-include-dir] [protoc]
#
#   <sharpportico-cli-dll>  the built CLI (src/SharpPortico.Cli/bin/Release/<tfm>/SharpPortico.Cli.dll)
#   [protoc-include-dir]    protoc's own include directory, the one holding google/protobuf/*.proto.
#                           Defaults to /usr/include, where libprotobuf-dev puts them on Debian/Ubuntu; the
#                           protoc release zip carries the same files under include/.
#   [protoc]                the protoc binary to run. Defaults to protoc from PATH.
set -euo pipefail

cli=${1:?usage: check-protos.sh <sharpportico-cli-dll> [protoc-include-dir] [protoc]}
include=${2:-/usr/include}
protoc=${3:-protoc}

if [ ! -f "$cli" ]; then
    echo "CLI assembly not found: $cli" >&2
    exit 1
fi

if ! command -v "$protoc" >/dev/null 2>&1; then
    echo "protoc not found: $protoc" >&2
    exit 1
fi

if [ ! -d "$include" ]; then
    echo "protoc include directory not found: $include" >&2
    echo "the well-known types are in libprotobuf-dev on Debian/Ubuntu, and in include/ of the protoc release zip" >&2
    exit 1
fi

repository=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cli=$(cd "$(dirname "$cli")" && pwd)/$(basename "$cli")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# One descriptor per spec, each in its own directory: every sample names its contract petstore.yaml, so the
# three petstore descriptors would otherwise overwrite each other and the last one would stand in for all.
specs=$(find "$repository/samples" -mindepth 3 -path '*/openapi/*' \
    \( -name '*.yaml' -o -name '*.yml' -o -name '*.json' \) | sort)

compiled=0
while IFS= read -r spec; do
    [ -n "$spec" ] || continue

    # samples/<Sample>/openapi/<spec>.<ext> -> <Sample>.<spec>
    relative=${spec#"$repository"/}
    key=$(printf '%s' "$relative" | sed -E 's|^samples/||; s|/openapi/|.|; s|\.[a-z]+$||; s|/|.|g')
    dir=$work/$key
    mkdir -p "$dir"

    echo "== $relative"
    dotnet "$cli" generate "$spec" --out "$dir"

    proto=$(find "$dir" -maxdepth 1 -name '*.proto' -print -quit)
    if [ -z "$proto" ]; then
        echo "the CLI wrote no descriptor for $relative" >&2
        exit 1
    fi

    # --descriptor_set_out is what makes this a compile of the whole file: protoc resolves every import
    # (google/protobuf/struct.proto and friends, hence -I "$include"), checks every name and builds the
    # descriptor it would hand to a code generator.
    descriptor=$dir/descriptor.pb
    "$protoc" -I "$dir" -I "$include" --descriptor_set_out="$descriptor" "$proto"
    echo "   protoc: $(basename "$proto") ok, $(wc -c < "$descriptor" | tr -d ' ') descriptor bytes"

    compiled=$((compiled + 1))
done <<< "$specs"

if [ "$compiled" -eq 0 ]; then
    echo "no spec was found under samples/*/openapi" >&2
    exit 1
fi

echo "$compiled descriptor(s) compiled by $("$protoc" --version)"
