# ADR 0018 — The graphical client is Avalonia

- **Decision:** Build the desktop client in Avalonia (.NET), not Tauri/React. Reverses the charter §5.2 "current leader" on evidence the charter could not have had.
- **Date:** 2026-08-24
- **Status:** accepted by Steve
- **Supersedes:** charter §5.2's Tauri/React lean. §8.3's required Avalonia prototype is now built rather than pending.

## What the charter argued, and what survives it

§5.2 gave four reasons for Tauri/React: a rich graph and animation ecosystem;
node/edge/layout/minimap/zoom capability; smaller deployment than Electron; and
keeping C# ownership of the runtime. It named Avalonia's strengths — "a C#-only
stack, native .NET data binding, direct hardware/OS access, and lower conceptual
complexity" — against a single risk: *"the additional work required for a
sophisticated animated execution graph."*

Reason three argues against Electron, not Avalonia. Reason four is neutral: the
runtime stays C# either way. So the case rested entirely on the graph, and that risk
was never checked against the framework.

**It does not hold.** `Avalonia.Base` ships a full animation system — `Avalonia.Animation`,
`Avalonia.Animation.Easings`, `Avalonia.Animation.Animators`, `KeyFrame`, `Easing`,
`Transition`, `CrossFade`, `PageSlide`, plus a compositor with `CompositionAnimation`
and `ImplicitAnimations`. Verified by unpacking the 12.1.1 package, not from memory.
And the graph-widget gap the charter actually meant has a direct answer it named
itself: **Nodify.Avalonia, at 2.0.0**.

## What the charter could not have known

- **The workload is not the one §8.3 specified.** That spike assumes "at least 500
  nodes" with live frame-rate updates. Real traces are four to six events, and the
  measured stream rate is **1.22 events per minute** at 413 bytes each (ADR-0017).
- **It is a tree, not a routed DAG.** Execution structure is nested parent/child
  spans. Tree layout is a fraction of the work general graph layout is — and the
  difference *is* the risk the charter named.
- **The runtime became a client-agnostic HTTP API** (G5) and a second client was
  proven against it (I2, the CLI). Either framework works; nothing is blocked.

## The reasons that decided it

1. **One language.** The client references `Dami.Contracts` directly instead of
   hand-mirroring every model in TypeScript, where drift is silent.
2. **One toolchain.** No npm tree beside the .NET one, in a project whose stated
   purpose was removing opaque framework weight.
3. **The same guardrails.** The GUI compiles under the identical analyzers, banned
   APIs, and architecture tests as everything else — this proved itself immediately:
   the Avalonia template failed the build on missing XML docs and `this.`
   qualification, and had to be brought up to the codebase's standard.
4. **Maintainer preference.** Steve dislikes React and maintains this daily. For a
   personal assistant, that is not a soft factor.

## What would have changed the answer

A genuinely animated avatar (L6) would favour the web ecosystem. It is undecided and
gated behind voice — which is a reason to keep the client's boundary clean, not a
reason to choose React now.

## Consequences

`Dami.Gui` lives in the solution rather than a separate repository: §8.3 required
separation specifically *if it uses React/Tauri*, and the contract-sharing benefit is
the point of choosing .NET. Electron remains the fallback the charter named, now
unreachable without reversing this.
