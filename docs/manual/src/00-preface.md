# Preface

<p class="lead">This is the complete manual for SharpPortico 1.5.0: a C# source generator that reads an
OpenAPI 3.0 or 3.1 document at build time and writes protobuf messages, a gRPC service, a C# client,
and — when you ask for it — a gateway that serves the same gRPC contract by calling the REST service
you already have.</p>

There are shorter documents in the repository: a README that fits on one page, a developer guide for
adoption, a reference for lookup. This one is meant to be read. It explains not only *what* the
generator emits, but why each rule exists, what the alternative would have cost, and what happens to a
contract that does not fit the model. If you need to defend a mapping decision in a design review, the
argument is in here.

## Who this is for

You will get the most out of this book if one of these is your situation.

- **You are adopting SharpPortico.** Part I is a five-minute path from a package reference to a running
  client, and it tells you exactly which files the build produces so nothing appears by surprise.
- **You own an OpenAPI document** that other teams consume and you need to know what a given construct
  becomes on the wire — before you commit to it. Parts II and III are the contract.
- **You are writing the service or the client.** Part IV covers the generated surface: messages,
  servers, clients, authentication.
- **You are putting gRPC in front of a legacy REST API.** Part V is proxy mode, including its JSON
  mapping, its cache, its key handling and its limits.
- **A build is failing, or a mapping lost something.** Part VII lists every diagnostic and every
  symptom I know about, with the fix.

## How the book is organised

| Part | Chapters | What it establishes |
| --- | --- | --- |
| I — Foundations | 1–3 | what the tool does, the five-minute path, what the build writes |
| II — Contracts | 4–6 | how a specification is declared, every option, the 3.0/3.1 split |
| **III — The mapping deep dive** | **7–17** | **the rules, stage by stage: scalars, names, shapes, arrays, enums, presence, operations, JSON, binary, losses** |
| IV — Generated code | 18–21 | messages, servers, clients, authentication in practice |
| V — Gateway | 22–23 | proxy mode end to end, and the JSON mapping it performs |
| VI — Command line | 24 | `sharpportico generate`, its report, its exit codes |
| VII — Diagnostics | 25–26 | every `SP` id, every symptom, and how to gate a build |
| VIII — Reference | 27–29 | the generated surface member by member, a worked example, a glossary |

Part III is the heart of the manual and can be read on its own. Each chapter there states the rule, shows
the input that exercises it, shows the output it produces, and names the loss when there is one.

## Conventions

**Code that is presented as output is output.** Where a block says *generated*, it was produced by
building this version of the generator against the input shown — not typed by hand to illustrate an
idea. The `.proto` blocks in this book are the text a consumer's `{Service}Proto.Text` constant holds
after a build, and CI compiles those same descriptors with `protoc`.

**The fixtures are part of the book.** `docs/manual/fixtures` holds the documents quoted in the mapping
chapters — `sink.yaml`, `edge.yaml`, `fleet-3.1.yaml` and `petstore.yaml` — together with the descriptors
1.5.0 produced from them. A rule in Part III can therefore be checked by reading both sides of it, and a
snippet can be regenerated with `sharpportico generate` without reconstructing the input.


**Options are named as they are declared** — `EnableProxyGeneration`, `EmitGoogleRpcStatusWrapper` — and
each one is given with its default. An option that the generator does not read is described as doing
nothing, because a manual that implies a switch works is worse than one that admits it does not.

**`SP` ids are quoted exactly** as the compiler and the CLI print them, so they can be searched in a
build log without guessing the punctuation.

**One version.** Everything here describes 1.5.0, the version in `Directory.Build.props` and on
nuget.org. Nothing is described "as of 1.4" or "coming soon"; where behaviour changed, the change is
named and the old behaviour is described only to explain an upgrade.

## The three claims

SharpPortico makes three promises, and each one is checked by a job in CI rather than by a sentence in
a document. They are repeated here because the rest of the manual assumes them.

| Claim | How it is verified |
| --- | --- |
| **Reflection-free, and NativeAOT-safe** | `samples/NativeAotExample` is published as a native image, the job fails if a managed assembly is left behind, and the published binary runs to `SMOKE TEST PASSED` |
| **The descriptor is real protobuf** | every sample's `.proto` is compiled by `protoc` (`.github/scripts/check-protos.sh`) |
| **The package works as installed** | CI packs the `.nupkg`, consumes it from a throwaway project, and runs the packed tool on both target frameworks |

The consequence for you is a specific kind of freedom: the generated messages are hand-written C# rather
than classes produced by `protoc` and reflection, so they work where reflection does not — trimmed
containers, single-file publishes, ahead-of-time compiled services.

## Reading paths

- **In a hurry:** chapter 2, then chapter 19 (servers) or 20 (clients).
- **Reviewing a contract:** chapter 7, then the chapter in Part III that matches each construct you use.
- **Deciding whether to put gRPC in front of a legacy API:** chapters 22 and 23, in that order.
- **Explaining a warning to a colleague:** chapter 25, which has an entry per id.
