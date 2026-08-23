# Dami Core — Planning Documents

Four documents, read in this order.

| File | What it is | When to read it |
|---|---|---|
| `Dami-Core-Project-Charter.md` | The original charter. Superseded in parts. | Historical context. Sections 12 (migration inventory) and 19 (reference links) remain current. |
| `Dami-Core-Architecture.md` | What to build. Systems, data, transport, capability registry, solution structure, phases. | Start here. It opens with a **Resume here** block. |
| `Dami-Core-Decisions-and-Requirements.md` | Why it looks that way. 26 decision records, requirements register, rejected options, open questions. | When you disagree with something in the architecture, or forget why it was chosen. |
| `Dami-Core-Operating-Rules.md` | The repository's AGENTS.md, a ~120-rule library, Dami's self-manual, and the verification protocol. | Phase 3, when the repo gets created. Part II is copy-paste ready. |

## Where the charter is superseded

- **Host OS** — Debian 13 + Cinnamon, not openSUSE Tumbleweed (D-003)
- **Memory provider** — custom PostgreSQL + pgvector, not Honcho, not Weaviate (D-007, D-009)
- **Transport** — custom async TCP/UDP behind `ITransport`, not SignalR (D-013)
- **Phase order** — data and proactive work precede the GUI, which moves to Phase 7 (D-022)
- **Framing** — the product is a continuous modeling system, not a request/response agent (D-001)

## Immediate next action

Phase 1 host validation, then the transport framing layer as the first code written.
See `Dami-Core-Architecture.md` §7.5.5 for the build order.
