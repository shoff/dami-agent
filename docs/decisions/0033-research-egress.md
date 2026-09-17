# ADR 0033 — Research egress: any public host, read-only, gated at the query

- **Decision:** The runtime may search the web and read public pages. A private SearXNG on
  loopback does the searching; a new `IResearchReader` seam fetches pages by plain GET from
  any host that resolves to a public address, never a private one, with every fetch metered
  and recorded like every other egress. A search query is text leaving the host and passes
  the disclosure gate first; what comes back is untrusted and labelled so. Both are off
  until `Research:Enabled` is set. A weekly `opportunity-scout` uses the same search to
  surface work, bounties and listings matching a profile Steve writes.
- **Date:** 2026-09-05
- **Status:** accepted — Steve: "I'll sign the egress widening, build the first slice."
- **Extends:** D-012 (privacy as an architectural boundary), ADR-0030 (the tool bundle)

## Context

Until today every byte that left this host went to a host on a list of two. That is the
right default for a system built around a person's private corpus, and it made "search the
internet" impossible by design. Steve asked whether Dami could search for ways the system
itself could earn, and the honest answer was that an agent without accounts cannot earn,
but the finding, filtering and drafting is exactly the proactive layer's job — if it can
read the web at all.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| Keep the allowlist; add search-engine hosts to it | No new seam | Reading a *result* means reading arbitrary hosts; an allowlist cannot express "public" | Does not solve the problem |
| A paid search API (Brave, Tavily) | Good results, no container | A key in the drop-in; the query still leaves; results still need reading | SearXNG gives the same for nothing and stays local |
| Let the frontier browse through Codex's own tools | Zero work | Egress the runtime cannot see, meter, gate or record | The opposite of D-012 |
| A public-only reader seam plus a gated search tool (chosen) | Read-only; private ranges refused by resolution, not by name; query gated; everything recorded; one switch | The gate is an 8B model judging a query; page text is untrusted input to the frontier | The costs are named and bounded |

## Evidence

- `dami-searxng` (image `searxng/searxng@sha256:55e1fa15…`) on `127.0.0.1:8888` answers
  `?format=json` in ~0.7 s once its resolvers were fixed (`--dns`; the default inherited
  only the slow first nameserver and every engine call began with a three-second timeout).
- `ResearchReaderTests`: `IsPublic` knows 10/8, 127/8, 172.16/12, 192.168/16, 169.254/16,
  100.64/10, multicast and `::1` from the internet; a private literal is refused before
  any request is sent; a redirect from a public host into `127.0.0.1:5810` is refused;
  disabled, blocked and non-http are refused; a page over the byte cap is refused; a read
  records `EgressCompleted`.
- `ResearchToolsTests`: a passed query is sent, a disguised one is sent disguised and the
  result says so, a withheld one is not sent, and research off never touches the gate.
- `Research_Holders_Should_Be_The_Pinned_Set`: who may hold `IResearchReader` or
  `ISearchEngine` is four named types.
- Full solution: 0 warnings, 0 errors, 1,741 tests in 21 assemblies.

## Consequences

- The boundary now has a door that is not host-listed. It is GET-only, refuses this
  network by address, is capped at 2 MB and 8,000 characters of text per page, is metered
  by the same egress budget, and writes `EgressRequested/Refused/Completed/Failed` events
  under the turn's trace. It is off by default and on by one drop-in line.
- The frontier gets `search_web` and `read_page`. The tool descriptions tell it the query
  leaves the machine and that page text is untrusted. The gate on the query is the same
  gate as on retrieved context, with the same ledger and corrections.
- The scout's queries and profile are Steve's own words in the drop-in, which is why they
  may go to SearXNG ungated — the same reasoning as the portrait prompt (ADR-0027).
- G27 extends the door with bounded reference traversal. `DeepResearchService` is a fifth
  pinned holder of `IResearchReader`; it cannot read profile stores or invoke a frontier
  model. It chooses links deterministically from the question, reference label/type, and
  URL, with explicit page, depth, per-page-reference, response-byte, time, redirect, and
  output limits. Every finding carries the complete URL path by which it was discovered.
- A research result taints its frontier turn. From that point the bundle accepts no more
  tool calls, including another URL or search query: even GET-only egress could encode
  local context into an attacker-influenced address. This confines prompt injection to
  the written answer rather than allowing page text to exercise another capability or
  exfiltrate through a follow-up request. A new user turn restores the normal tool set.
- J13 lets the user omit the starting URL. The frontier then chooses one to three public
  starting URLs before receiving untrusted content and supplies them in one `deep_research`
  call. All roots and their references share the existing page/depth budget; this does not
  unlock tool calls after a search or page response. A supplied URL still uses the original
  single-root path. Each rerun creates a separate dated artifact and retains the original
  report; automatic/supplied selection and starting URLs are archived with the run.
- Known website image, style, script and font assets are excluded from discovered
  candidates before spending the read budget. Source HTTP failures, refused reads and
  source timeouts are recorded as skipped reads;
  the remaining candidate branches continue, and the final model result includes the gaps.
  Explicit caller cancellation still stops the run. Unexpected runtime/storage failures
  retain the existing failed-run behavior and earlier saved findings.
- DNS policy and transport are joined: the reader resolves and validates every address,
  stores the chosen public address on the request, and the socket connects to that exact
  address with redirects, cookies, proxies, and connection reuse disabled. A second DNS
  answer therefore cannot rebind a validated hostname into loopback or a private network.
- SearXNG itself talks to Brave, DuckDuckGo, Wikipedia and whatever else its defaults
  enable. Those engines see the query and this host's address. That is the price of
  search and it is stated here rather than hidden.

## Reversal path

`Research:Enabled=false` closes the door on the next request; `docker rm -f dami-searxng`
removes the engine; the seam and the scout can be deleted without touching anything else.
