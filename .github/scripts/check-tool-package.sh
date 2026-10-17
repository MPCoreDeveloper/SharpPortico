#!/usr/bin/env bash
#
# Install the packed CLI the way a user does, and ask it the questions this release claims it can answer.
#
# `dotnet pack` proves that a .nupkg exists. It does not prove that NuGet can install it - a tool package
# carries one folder per target framework under tools/, and the packer decides which - nor that the installed
# copy runs and answers: the CLI tests drive Program.RunAsync from the *project*, so nothing else in the
# pipeline ever executes the package. A tool can therefore pack green, install, and answer nothing, and the
# release reaches the feed before anyone notices.
#
# So: install into a temporary tool path, run the text summary, run the document with --out, and run a
# contract that must be refused - the three answers a gate uses. A mismatch exits non-zero.
#
# Usage: check-tool-package.sh <artifacts-directory>
set -euo pipefail

artifacts="${1:?usage: check-tool-package.sh <artifacts-directory>}"
artifacts="$(cd "$artifacts" && pwd)"

# NuGet is a native tool, so on Git Bash for Windows the /d/... form that cd accepts is not a path it can read.
# -m keeps the drive letter and uses forward slashes, which both worlds accept. A no-op on Linux.
if command -v cygpath >/dev/null 2>&1; then
  artifacts="$(cygpath -m "$artifacts")"
fi

package="$(find "$artifacts" -maxdepth 1 -name 'SharpPortico.Cli.*.nupkg' | sort -V | tail -1)"
if [ -z "$package" ]; then
  echo "No SharpPortico.Cli package in $artifacts" >&2
  exit 1
fi

version="$(basename "$package" .nupkg)"
version="${version##SharpPortico.Cli.}"

# The document is JSON, and a shell is the wrong tool to read it. Pick the first interpreter that can import
# json: on Linux that is python3, on Git Bash for Windows `python3` is often the Store alias that cannot run.
interpreter=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import json' >/dev/null 2>&1; then
    interpreter="$candidate"
    break
  fi
done
if [ -z "$interpreter" ]; then
  echo 'A Python 3 interpreter is required to read the report' >&2
  exit 1
fi

# Everything below runs from the temporary directory with relative paths, because the dotnet CLI is a native
# tool and does not resolve MSYS paths like /tmp/... on Git Bash for Windows.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cd "$work"

echo "Installing SharpPortico.Cli $version from $artifacts"
if ! dotnet tool install SharpPortico.Cli \
  --tool-path tool \
  --add-source "$artifacts" \
  --version "$version" \
  --ignore-failed-sources > install.log 2>&1; then
  echo 'The packed tool does not install' >&2
  cat install.log >&2
  exit 1
fi

tool="./tool/sharpportico"
[ -x "$tool" ] || tool="./tool/sharpportico.exe"
if [ ! -x "$tool" ]; then
  echo "The installed tool is not in $work/tool" >&2
  ls -l tool >&2
  exit 1
fi

cat > probe.yaml <<'EOF'
openapi: 3.0.3
info:
  title: Probe
  version: 1.0.0
paths:
  /tickets:
    post:
      operationId: CreateTicket
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/Ticket' }
      responses:
        '200':
          description: ok
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Ticket' }
components:
  schemas:
    Ticket:
      type: object
      properties:
        id: { type: string }
EOF

# The refusal fixture: an operation derives `SearchRequest`, a name a component schema already declares, so
# the generator refuses the contract with SP2005 - the case the document format exists for.
cat > collides.yaml <<'EOF'
openapi: 3.0.3
info: { title: Search, version: 1.0.0 }
paths:
  /search:
    post:
      operationId: Search
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/SearchRequest' }
      responses:
        '200':
          description: ok
components:
  schemas:
    SearchRequest:
      type: object
      properties:
        text: { type: string }
EOF

# 1. The text summary a person reads; its shape is the compatibility surface this release promised to keep.
text_status=0
"$tool" generate probe.yaml > summary.txt 2> summary.err || text_status=$?
if [ "$text_status" -ne 0 ] || [ -s summary.err ]; then
  echo "The text summary failed (exit $text_status)" >&2
  cat summary.txt summary.err >&2
  exit 1
fi
for line in 'service:    ProbeService' 'namespace:  ProbeService' 'package:    probeservice' \
            'messages:   3' 'enums:      0'; do
  if ! grep -qxF "$line" summary.txt; then
    echo "The text summary does not contain '$line'" >&2
    cat summary.txt >&2
    exit 1
  fi
done

# 2. The document, and the descriptor it names.
json_status=0
"$tool" generate probe.yaml --format json --out contracts > report.json 2> report.err || json_status=$?
if [ "$json_status" -ne 0 ]; then
  echo "The document mode failed (exit $json_status)" >&2
  cat report.err >&2
  exit 1
fi
if [ -s report.err ]; then
  echo 'The document mode wrote to standard error; standard output holds the document and nothing else' >&2
  cat report.err >&2
  exit 1
fi

"$interpreter" - report.json <<'PY'
import json, os, sys

with open(sys.argv[1], encoding="utf-8") as handle:
    report = json.load(handle)

if set(report) != {"formatVersion", "spec", "service", "services", "counts", "diagnostics", "descriptor"}:
    sys.exit(f"the document has {sorted(report)}")
if report["formatVersion"] != 1:
    sys.exit(f"formatVersion is {report['formatVersion']!r}")
if report["service"] != {"name": "ProbeService", "namespace": "ProbeService", "package": "probeservice"}:
    sys.exit(f"service is {report['service']!r}")
rpcs = [rpc["name"] for service in report["services"] for rpc in service["rpcs"]]
if rpcs != ["CreateTicket"]:
    sys.exit(f"the RPC surface is {rpcs!r}")
if report["counts"] != {"messages": 3, "enums": 0}:
    sys.exit(f"counts are {report['counts']!r}")
if report["diagnostics"] != []:
    sys.exit(f"a 3.0 probe reported {report['diagnostics']!r}")
descriptor = report["descriptor"]["path"]
if not descriptor.endswith("probe.proto") or not os.path.isfile(descriptor):
    sys.exit(f"the document names {descriptor!r}, which is not the file --out wrote")
PY

# 3. A refusal: exit code 1, no identity, the reason inside the document.
refusal_status=0
"$tool" generate collides.yaml --format json > refusal.json 2> refusal.err || refusal_status=$?
if [ "$refusal_status" -ne 1 ]; then
  echo "A refused contract exited $refusal_status, not 1" >&2
  cat refusal.err >&2
  exit 1
fi
if [ -s refusal.err ]; then
  echo 'A refusal wrote to standard error; the document carries the reason' >&2
  cat refusal.err >&2
  exit 1
fi

"$interpreter" - refusal.json <<'PY'
import json, sys

with open(sys.argv[1], encoding="utf-8") as handle:
    report = json.load(handle)

if "service" in report:
    sys.exit("a refused contract has no model, so it must not name a service")
if report["services"] or report["counts"] != {"messages": 0, "enums": 0}:
    sys.exit(f"a refused contract reported a mapping: {report!r}")
errors = [diagnostic for diagnostic in report["diagnostics"] if diagnostic["severity"] == "error"]
if not errors:
    sys.exit(f"the refusal is not in the document: {report['diagnostics']!r}")
print(f"      refusal: {errors[0]['id']}")
PY

echo "The packed tool installs, summarises, reports and refuses: $version"
