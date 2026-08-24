# ADR 0019 — Approved self-authored tools execute only in an OS sandbox

- **Decision:** Verify self-authored C# source and tests in a fixed, package-free build
  envelope, then execute approved artifacts only in a separately launched bubblewrap
  process with bounded resources, no network namespace, no persistent writable mount,
  and no view of the repository or home directory.
- **Date:** 2026-08-24
- **Status:** accepted
- **Supersedes:** none

## Context

D-016 requires explicit human promotion before a self-authored tool reaches the live
registry and forbids write/delete capability in v1. ADR-0018 deliberately made staging
inert because arbitrary managed code cannot be confined inside the Host. Human review,
successful tests, source scans, and a declared `PureComputation` or `ReadOnly` profile
are evidence; none is a security sandbox. Loading an approved assembly into the Host
would give it the same database, filesystem, network, reflection, native-call, and
process authority as the interactive runtime.

The workstation has `/usr/bin/bwrap`, and an unprivileged Steve-owned smoke invocation
successfully created an isolated network namespace with read-only runtime mounts.
That provides an enforceable process boundary without turning a model declaration into
authority.

Promotion crosses three independently recoverable states: exact artifact verification,
one human resolution, and publication into the running registry. Approval must pin the
proposal ID and artifact version. Verification happens before an approval is offered,
so the human sees actual compiler/test evidence. Approval then authorizes only that
verified version; it does not authorize altered bytes or a future proposal.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Load the reviewed assembly into the Host | Lowest invocation overhead | Full Host privilege; no trustworthy .NET sandbox; unload and rollback are fragile | Violates the v1 authority limit |
| Approve after source review and static scans only | Simple | Reflection/native/process calls can evade blacklist-style review | Evidence is not confinement |
| Run every tool as an MCP server | Existing protocol/process boundary | Adds a server lifecycle and network transport; MCP remains an egress surface | Local fixed-envelope tools need neither |
| Fixed build plus bubblewrap process | OS-enforced filesystem/network boundary; exact artifact remains inspectable; process is killable | Linux-specific; process startup cost; requires carefully pinned mounts and limits | Chosen |

## Fixed verification and execution envelope

Trusted code generates the project, entry point, contracts, and package-source-clearing
configuration. Proposal input supplies only the already-staged `.cs` source and test
files. There are no proposal-controlled projects, analyzers, generators, packages,
build scripts, environment variables, or command arguments. Restore/build use no
package sources. Tests implement the fixed proposal-test contract and execute inside
the same sandbox used at runtime.

The runtime sandbox receives only the immutable verified output and one JSON input on
stdin. It sees the .NET runtime and required system libraries read-only, an ephemeral
tmpfs, a private process/network namespace, and no home, repository, database
credentials, host sockets, or persistent writable path. Capabilities are dropped;
wall-clock, memory, process-tree, and combined-output bounds are enforced outside the
sandbox. Cancellation kills the complete process tree. A promoted handler never
receives Host services.

The live registry publishes handler and schema dependencies before the retrievable
metadata entry. If publication fails, exact-instance rollback removes only this
activation. Durable promotion state records requested, denied, activated, or failed
outcomes and startup recovery retries verified+approved records lacking activation.

## Evidence

Architecture §7.6.5 and D-016 define tools as arbitrary persistent code and place the
human approval line at promotion. ADR-0018 records why managed in-process loading is
not confinement. On this host, `sudo -u steve -H bwrap` with read-only runtime mounts,
`--unshare-net`, a private `/proc`, and `/dev` successfully executed `/usr/bin/true`
with exit 0. Concrete build, escape, resource, recovery, and live invocation evidence
is required by F5c2/F5c3 before this mechanism is called complete.

## Consequences

Approval cannot make a tool a privileged native plugin. Self-authored tools are
normalized into the common registry but execute through a sandboxed handler. Startup
and per-call overhead are accepted for a v1 feature expected to be rare; correctness
and authority containment dominate latency here. Tools needing database, network, or
persistent write access must be implemented through the normal human-owned repository
workflow, not self-promotion.

The first two live F5b proposals are review artifacts only and do not conform to the
fixed runtime/test contracts. They remain valid immutable evidence but cannot be
promoted. F5c's live proof will stage a new conforming proposal.

## Reversal path

Disable the promotion endpoint and remove sandboxed entries from the dynamic registry;
the Host's built-in native/MCP/skill sources remain unchanged. Verified outputs are
derived and can be deleted/rebuilt from immutable proposals. Promotion, approval, and
event rows remain audit evidence. A future WASM runtime can implement the same verifier
and execution abstractions without rewriting staged artifacts or approval history.
