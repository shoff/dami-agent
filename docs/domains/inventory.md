# Domain inventory — draft for Steve's sign-off (K1)

**Status: draft, 2026-08-25, Claude.** K1's acceptance criterion is *"docs/domains lists
the sources per domain (health, civic, network, estate, workshop) with Steve's sign-off"*.
This is the half that can be measured from this desk: what the corpus already holds per
domain, what this host can observe on its own, and what only Steve can name. The
sign-off column is his.

Method: regex counts over `dami.observations.body` (7,104 rows; 6,995 from Hermes memory,
45 from `dami chat`, 33 Hermes threads, 13 devlogs), then random samples of each match set
read by hand. A count is a ceiling, not a domain: the samples say what the hits are.

| Domain | Corpus signal | What the hits actually are | Sources this host can read now | Needs from Steve | Privacy class | Recommendation |
|---|---|---|---|---|---|---|
| **Health** | 301 rows | Real. Diagnoses, the AVR surgery, appointments, vitals, INR — already extracted to `health_events` (K2, 84 facts, correctable) | Notes and chat turns (the existing collector) | Nothing to start; later: whether any export from a patient portal or wearable should be a source | LocalOnly, maximally sensitive (see `health-privacy-review.md`) | **Done as a domain.** Next value is a second source, and that is his call |
| **Network** | 94 rows | Mostly one cluster, March 2026: the Mac mini losing Screen Sharing/SSH after an update, DNS resolving to `privatelink`, split-tunnel VPN, Azure endpoints timing out. That is **work-network troubleshooting**, not the home network, and some of it names work hosts | This host: `ip`/`nmcli` (one Wi-Fi uplink `192.168.4.0/22`, wired ports down, docker bridge), `journalctl`, the sidecar ports, PostgreSQL; the Mac mini over SSH once a key exists (G10) | Whether the *home* network is a domain he wants modelled (router/AP, NAS, the Mac, this box) and where its truth lives (UniFi? OPNsense? nothing?) | LocalOnly; **work-network facts are private-domain data and stay out of the first slice per the hard rules** | Build a **host-and-home** network collector from local state only; do not extract from the March cluster |
| **Civic** | 3 rows | False positives: a concealed-carry permit mention, a bar in Lakeville, a "permit" in another sense. No civic content exists in the corpus | Nothing | Everything: which county/city, which feeds (council agendas, assessor, elections), whether he wants any of it | LocalOnly for anything tied to his address; public feeds Egressable | **Not startable** without sources; ask, do not guess |
| **Estate** | 4 rows | False positives ("appraisal" of the system's work, "property" in a coding sense). No house, mortgage, insurance, or maintenance content | Nothing | Whether documents exist (PDFs, a folder, email) and where; maintenance log if any | LocalOnly | **Not startable** without sources |
| **Workshop** | 2 rows | Nothing about tools or projects in the physical sense | Nothing | Whether there is a workshop at all in the sense the charter meant, and what would be recorded (tools, materials, projects, consumables) | LocalOnly | **Not startable**; may be the wrong name for what he wants |
| *(not in the charter)* Vehicles | 7 rows | Passing mentions | — | — | — | Noted only because the count was non-zero; no case to build |
| *(not in the charter)* Finance | 2 rows | Nothing | — | — | — | Same |

What the corpus is mostly about, by Hermes's own categories: technical 2,634 · project
1,002 · intent 878 · decision 825 · personal 394 · emotional 307 · preference 255. The
charter's domain list was written before the corpus was read; three of its five domains
have no data behind them yet, and the two that do (health, and a "network" that is really
work) were not chosen by count.

## Questions for Steve (the sign-off)

1. **Network**: home network as a domain — yes or no? If yes, what is its source of truth?
2. **Civic**: which jurisdiction, and which feeds, if any?
3. **Estate**: where do house documents live, and is a maintenance log wanted?
4. **Workshop**: does this domain exist? If so, what is in it?
5. Is **work** (Azure, the day job's systems) a domain Dami should model at all, given the
   hard rule against private-domain data in the first slice? The corpus already carries it
   from Hermes; the question is whether a collector should ever read it.

Until these are answered, H9 (domain collectors) and K4 (remaining domains) have exactly
one buildable item: a local-state network collector for this host, which needs no source
Steve has not already provided.
