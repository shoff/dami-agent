# Handoff — 2026-08-30 (updated later the same day)

State at the end of a long session, updated by the session that continued it. Read
`CLAUDE.md`, `docs/onboarding.md`, and `docs/workstation-runbook.md` §7 first; this file
only covers what is in flight.

## Right now

- Branch `main`. The three commits below plus the G5a client slice are committed;
  Steve asked for the push on 2026-08-30.
- Build **0 warnings, 0 errors**. Tests **1295 passing, 21/21 assemblies**.
- The G5a client slice (below) builds and its tests pass. Nothing has run against a
  live host with the flag on — see "What remains".

## Earlier commits in this batch

| Commit | What |
|---|---|
| `bd9bb5b` | Provision the first-party OIDC clients, which nothing ever did (G5a) |
| `10a3fb0` | Fitness domain and Hermes import, phase 1 (H9) |
| `cc0033a` | Discord gateway over a new egress channel (M1, ADR-0024/0025) |

## The G5a client slice

**CLI device flow** (previous session):

- `Dami/src/Dami.Authentication/DeviceFlow.cs` — pure RFC 8628 parsing. 9 tests.
- `Dami/src/Dami.Authentication/DeviceLogin.cs` — the polling driver.
- `Dami/src/Dami.Authentication/DamiTokenStore.cs` — `~/.config/dami/token.json`, 0600. 9 tests.
- `Dami/src/Dami.Gateway.Cli/LoginCommands.cs` — `dami login` / `logout` / `whoami`.
- Tests: `DeviceFlowTests.cs`, `DamiTokenStoreTests.cs`.
- Modified: `CommandRouter.cs`, `Program.cs` (a stored token beats
  `Authentication:AccessToken`), the auth test csproj.

**Correction:** the previous handoff claimed the three verbs were routed before dispatch.
They were not — `DispatchAuthAsync` existed and nothing called it, so `dami login` printed
usage. Wired now, and smoke-tested: `dami whoami` answers "Not logged in", `dami login`
reaches the host and reports that device authorization is not enabled.

**GUI PKCE flow** (this session):

- `Dami/src/Dami.Authentication/PkceFlow.cs` — pure RFC 7636: verifier, S256 challenge,
  redirect/state parsing. 8 tests, including the Appendix B vector.
- `Dami/src/Dami.Authentication/PkceLogin.cs` — the driver. No browser and no listener:
  the host has no HTML login page, so the GUI collects credentials, POSTs them with the
  authorization request, and reads the code out of the Location header instead of
  following it. 6 tests against a scripted authority, including state-mismatch and
  challenge/verifier proof.
- `Dami/src/Dami.Gui/GuiTokens.cs` — `~/.config/dami/gui-token.json`, separate from the
  CLI's because the registrations differ.
- `Dami/src/Dami.Gui/LoginWindow.cs` + `MainWindow.Login.cs` — on open, the window probes
  `/events` past the stream end; only a 401 raises the modal login. Success applies the
  token to both the runtime and task-board clients and stores it.
- `DamiAuthenticationOptions.DEFAULT_GUI_REDIRECT_URI` — single source for the registered
  redirect.

**Bootstrap identity** (this session):

- `Dami/src/Dami.Authentication/DamiIdentityProvisioner.cs` + `BootstrapIdentitySeeder.cs`
  — `dami_auth."AspNetUsers"` is **empty** and no code path ever created a user, so with
  the flag on every login on every client would have failed at the password check. Same
  green-around-a-hole shape as the client registrations. The seeder creates
  `Authentication:BootstrapUsername` with `Authentication:BootstrapPassword` (secret
  config only — `Authentication__BootstrapPassword`, two underscores) if absent, and
  never touches an existing account or its password. 4 tests against real Postgres.

## What remains before the flag goes on

Verified against the live database: `dami-cli` and `dami-gui` registrations exist in
`dami_auth."OpenIddictApplications"`; `AspNetUsers` is empty until the seeder runs with
bootstrap config. Turning `Authentication:Enabled` on also requires persistent PKCS#12
signing and encryption certificates (`ServiceCollectionExtensions.ReadOptions` refuses
ephemeral keys outside the Testing environment) and `AllowInsecureLoopback` for the
http issuer. None of that exists on the deployed host yet.

Order: Steve sets bootstrap username/password (out of band) and the certificates, enables
the flag, restarts, then verify CLI (`dami login`), GUI (modal login), and Discord
together. That is board item G5a4.

## What is deployed vs committed

**`/opt/dami` is behind.** ADR-0025 is committed but not deployed, so **Discord still
refuses memory-derived answers** and `status` / `help` do not work. To catch up:

```bash
cd /home/steve/dev/dami-agent && ./tools/deploy.sh   # no sudo needed
sudo systemctl restart dami-host                      # sudo: Steve only
```

The agent has had no sudo since the temporary password expired on 08-27.

## Discord (M1) — working

Live and connected. The bot answers. Three defects found on first contact and fixed:
discarded WebSocket close codes, RESUME to the generic gateway instead of
`resume_gateway_url`, and backoff resetting on a connection that died instantly —
together, 24 identifies/minute against a limit of one per five seconds.

The original failure was `4014`: **MESSAGE CONTENT** was never enabled in the developer
portal. It is now.

ADR-0024 introduced `IEgressChannel`. **ADR-0025 supersedes its refusal rule**: the test
is who receives the content, not what it contains, because D-012 protects the profile from
*others* and the reader here is Steve. Recorded cost: Discord Inc. holds memory-derived
answers. A future channel with a different reader passes `recipientIsDataSubject: false`
and gets ADR-0024's behaviour back.

## Fitness (H9) — phase 1 done, phases 2 and 3 not started

Migration 036, six `dami.fitness_*` tables. Imported 234 events (140 cardio, 73
resistance, 21 weight), 318 sets, 22 exercises from Hermes at `192.168.4.23`
(`sbadmin`/`sbadmin`). Excluded rows are all Hermes soft-deletes; nothing orphaned.

`tools/import-hermes-fitness.sh` is idempotent. `tools/reconcile-hermes-fitness.sh`
compares both databases and exits non-zero on drift — verified by deleting a row and
watching three checks fail.

**Hermes is still the writer.** Steve's position: Dami has not earned system-of-record
yet. Phase 2 is parallel write plus nightly reconciliation; phase 3 is cutover.

`HERMES_PGPASSWORD` is required by both scripts and is not in the repo.

## Codex's lane

`5c0e1ff` — a Claude commit — **swept up Codex's whole OIDC slice** under an unrelated
message. Runbook §7 warns about exactly this and names `7d3b508` as the prior instance.
Recorded in `work-log.md`. **Stage explicitly by path.**

Codex's server side is close to done and better than a first read suggests: a
`FallbackPolicy` denies by default and maps method to scope. `RUNTIME_READ` appearing
unreferenced is *not* a gap — the fallback covers it.

## Known problems

1. **Unexplained transient.** One `Dami.Host.Tests` failure during a full-solution run,
   not reproduced in five subsequent runs, name not captured. Suspected cross-assembly
   database contention. Not resolved.
2. **Architecture tests were largely vacuous** — `AssemblyProbe` silently skips assemblies
   it cannot load, and the project referenced only two. Fixed for the new egress rule;
   **the other architecture tests still have this hole.**
3. **Cinnamon died twice on 08-30** (~11:46 and ~12:11), killing keyboard input both
   times. No segfault, no coredump, cause unknown. Recovered with `cinnamon --replace`.
4. **The GUI has a real GPU crash**: `Dami.Gui[440218]: segfault ... in
   libnvidia-glcore.so.595.84` on 08-28. Avalonia's GL against the 595.84 driver.

## Traps this session hit

- `pkill -f <pattern>` matches the agent's own shell — killed it three times. Kill by PID.
- Config keys need **two** underscores: `Discord__Token`. One binds to nothing, silently.
- `install -m 0600 /dev/null <file>` **truncates** on re-run; it emptied the token file.
- `/etc/systemd/system/dami-host.service.d/override.conf` is world-readable (0644), which
  is why the Discord token lives in `/etc/dami/discord.env` at 0600 instead.
- Comparing `timestamptz` as text across two psql sessions reports false drift; use epochs.

## Suggested next step

The flag-on verification described in "What remains" — it needs Steve for the secrets,
the certificates, and the restart. That is board item G5a4.
