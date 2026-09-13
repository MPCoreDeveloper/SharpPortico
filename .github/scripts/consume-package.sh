#!/usr/bin/env bash
#
# Consume the packed package the way a stranger does.
#
# This repository's own tests and samples reference the generator as a *project*, with
# OutputItemType="Analyzer" - that is the project path, and it exercises the generator without ever touching
# NuGet's package assets. The rule that decides whether a package's generator runs at all (analyzers/ is
# handed to the compiler; lib/ is only a reference) exists solely on the package path. A package can
# therefore be completely inert while every test in the repository is green, which is exactly what happened
# to 1.0.0: its generator was packed into lib/, so a plain PackageReference generated nothing - silently, with
# a successful build and no diagnostics.
#
# So: build a project in a temporary directory, with the packed package as its only source of the generator,
# and reference a generated type. If nothing is generated, this fails.
#
# Usage: consume-package.sh <artifacts-directory>
set -euo pipefail

artifacts="${1:?usage: consume-package.sh <artifacts-directory>}"
artifacts="$(cd "$artifacts" && pwd)"

# NuGet is a native tool, so on Git Bash for Windows the /d/... form that cd and find accept is not a path it
# can read. -m keeps the drive letter and uses forward slashes, which both worlds accept. A no-op on Linux.
if command -v cygpath >/dev/null 2>&1; then
  artifacts="$(cygpath -m "$artifacts")"
fi

package="$(find "$artifacts" -maxdepth 1 -name 'SharpPortico.SourceGenerator.*.nupkg' | sort -V | tail -1)"
if [ -z "$package" ]; then
  echo "No SharpPortico.SourceGenerator package in $artifacts" >&2
  exit 1
fi

version="$(basename "$package" .nupkg)"
version="${version##SharpPortico.SourceGenerator.}"

consumer="$(mktemp -d)"
trap 'rm -rf "$consumer"' EXIT
cd "$consumer"

echo "Consuming SharpPortico.SourceGenerator $version from $artifacts"

cat > NuGet.config <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="packed" value="$artifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

mkdir -p openapi
cat > openapi/probe.yaml <<'EOF'
openapi: 3.0.3
info:
  title: Consumer
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

cat > consumer.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <SharpPorticoServiceName>ConsumerService</SharpPorticoServiceName>
    <SharpPorticoNamespace>Consumer.Generated</SharpPorticoNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SharpPortico.SourceGenerator" Version="$version" />
    <PackageReference Include="Google.Protobuf" Version="3.36.0" />
    <PackageReference Include="Grpc.Core.Api" Version="2.66.0" />
    <PackageReference Include="Grpc.Net.Client" Version="2.66.0" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="openapi/*.yaml" />
  </ItemGroup>

</Project>
EOF

# Referencing the generated client is the assertion: if the generator did not run, this does not compile.
cat > Program.cs <<'EOF'
using Consumer.Generated;

Console.WriteLine(typeof(ConsumerServiceClient).FullName);
EOF

dotnet build consumer.csproj -c Release -v minimal
