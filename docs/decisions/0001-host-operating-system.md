# ADR 0001 — Host operating system is Linux Mint 22.3

- **Decision:** Adopt Linux Mint 22.3 "Zena" (Cinnamon, Ubuntu 24.04 `noble` base) as the Dami Core host, reversing D-003's choice of Debian 13.
- **Date:** 2026-08-22
- **Status:** proposed
- **Supersedes:** D-003 in `docs/dami-core-decisions-and-requirements.md` (which itself reversed the charter's §5.1 preference for openSUSE Tumbleweed)

## Context

The host OS has now been decided three times without the machine ever agreeing:

- The charter (§5.1) chose **openSUSE Tumbleweed with GNOME**, on the argument that a rolling distribution suits an experimental AI-development workstation.
- **D-003** reversed that to **Debian 13 with Cinnamon**, on the argument that the components which must be current — the .NET SDK and the NVIDIA driver — come from Microsoft's and NVIDIA's own repositories, the Python/CUDA stack is pinned per-service regardless of host, and what the host must actually supply is a kernel and desktop that do not break on a machine running unattended scheduled services.
- The workstation is running **Linux Mint 22.3**, installed on the internal NVMe.

This ADR does not introduce a new argument. It records that D-003's *reasoning* is sound and its *conclusion* was not carried out, and asks whether the running system is the decision or an interim state. Phase 1's exit condition cannot be assessed against a host that no document names.

**This is written as `proposed`, not `accepted`, deliberately.** Whether Mint is the intended host or a convenience install made during hardware validation is Steve's call, not an inference available from the filesystem. Accepting this ADR settles it; rejecting it means Phase 1 includes a reinstall.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| **Linux Mint 22.3 (running)** | Ubuntu 24.04 LTS base, supported to 2029. Every vendor repo the project needs publishes `noble`. Cinnamon, which D-003 already wanted. X11 by default. Timeshift preinstalled. Already installed and working — GPU, desktop, network, .NET all verified. | Mint's own package layer and Update Manager policy sit between the user and Ubuntu. Snapd blocked by apt pinning. Desktop packages older than a rolling distro. | — proposed choice |
| **Debian 13 + Cinnamon (D-003)** | The recorded decision. Most conservative base. No downstream vendor layer. | Requires a reinstall of a working machine to obtain a base that differs mainly in package-layer politics. `noble` vs `trixie` availability must be re-verified for PGDG, Microsoft, Docker. | Reinstall cost is real and the benefit over an Ubuntu LTS base is small |
| **openSUSE Tumbleweed + GNOME (charter §5.1)** | Current everything, strong Btrfs/Snapper integration, openQA-tested packages. | Rolling updates on an unattended always-on host. GNOME, which D-003 moved away from. All vendor repo paths change. | Already rejected by D-003, and no new evidence overturns that |
| **Ubuntu 24.04 LTS proper** | Identical base to Mint with no downstream layer; the most direct target for every vendor repo. | Loses Cinnamon and Timeshift defaults; still a reinstall. | Same reinstall cost as Debian for a smaller delta than Mint already provides |

## Evidence

```
$ cat /etc/os-release
NAME="Linux Mint"; VERSION="22.3 (Zena)"; ID=linuxmint; ID_LIKE="ubuntu debian"

$ uname -r
6.14.0-37-generic

$ findmnt -no SOURCE,FSTYPE /
/dev/nvme0n1p3 ext4                    # an installed system, not a live session

$ nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv
NVIDIA GeForce RTX 4080, 16376 MiB, 595.84

$ dotnet --list-sdks
10.0.400 [/usr/share/dotnet/sdk]
```

The vendor-repository argument that D-003 rests on was exercised directly today rather than assumed. Ubuntu's `noble` archive offers `gh` 2.45.0 (March 2024); GitHub's own apt repo offers 2.98.0 (released 2026-08-20). Adding `https://cli.github.com/packages stable main` with `arch=amd64` installed the current version without complaint. The same pattern is what PostgreSQL (PGDG), Microsoft (.NET), NVIDIA, and Docker all publish for `noble`. The host's own package age is not the constraint D-003 said it wasn't.

Also observed, and relevant to two open decisions:

- The graphical session is **X11** (`loginctl` shows session `c2` on `tty7`). The charter's open decision 2 asks "whether Xorg is required for any computer-control path" — on this host X11 is the default rather than something to arrange.
- `/etc/apt/preferences.d/nosnap.pref` is present. Mint pins snapd out. Nothing in the plan wants a snap, but anything that only ships as one will need another path.

No benchmark distinguishes these options on the work Dami Core actually does. That work is .NET on bare metal, Postgres from PGDG, and CUDA inside pinned containers — none of which is sensitive to the host's desktop package age. **This is a judgment call about reinstall cost versus adherence to a written decision, and there is no measurement that decides it.** Saying otherwise would be dressing up a preference.

## Consequences

**Easier.** No reinstall; Phase 1 continues from a host with a verified GPU, desktop, network, and .NET SDK. Timeshift ships preinstalled, which gives ADR-0002 a distro-native path. Cinnamon and X11 are what D-003 and the computer-control path respectively wanted.

**Harder.** A third recorded position on host OS. Anyone reading D-003 in isolation will build against Debian assumptions, so D-003 must carry a pointer to this ADR. Mint's Update Manager applies its own policy to kernel and package updates, which needs an explicit setting for an unattended host rather than a default. Snapd is unavailable without undoing the pin.

**Locked in.** An Ubuntu `noble` package base until the next Mint major release. `apt` repository lines throughout any provisioning script or documentation will say `noble`.

**Cost.** Effectively zero to accept, since it describes the running system. The cost is entirely on the reject branch: a reinstall, plus re-verification of GPU, driver, desktop, audio, and .NET.

## Reversal path

Cheap while the repository holds only documents and no service is deployed. Reversal is a reinstall onto `nvme0n1p3` plus re-running Phase 1 validation — hours, not days, and no application code depends on the distribution (D-003 made the same point and it still holds).

That cost rises sharply once systemd units, a PGDG Postgres instance with real data, and pinned inference containers exist on this host. **If this ADR is going to be rejected, reject it before Phase 2 puts data on the machine.**
