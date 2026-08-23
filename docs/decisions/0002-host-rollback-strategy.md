# ADR 0002 — Host rollback via Timeshift rsync snapshots on ext4

- **Decision:** Satisfy the Phase 1 rollback requirement with scheduled Timeshift rsync snapshots of the system on the existing ext4 root, rather than reinstalling onto Btrfs subvolumes.
- **Date:** 2026-08-22
- **Status:** accepted — implemented 2026-08-22; the rehearsed restore below is still outstanding
- **Supersedes:** none. This fills a gap rather than reversing a decision — no document specifies rollback for an ext4 host.

## Context

Phase 1's exit condition is "stable host, GPU compute verified, **rollback available**." Rollback is not available today, and every document that discusses it assumes a filesystem this machine does not have.

The charter (§5.1, Phase 1) assumed Btrfs with Snapper, following from openSUSE. D-003 kept "snapshot strategy still required" as a line item when it moved to Debian but did not say what the strategy is. The architecture document's Phase 1 says "Btrfs with snapshot tooling, or LVM snapshots" — an either/or that was never resolved.

The running host is ext4 on `nvme0n1p3` with no Btrfs subvolumes and no LVM. Both documented options require a reinstall or a repartition. The second matters: **this machine multiboots** — Windows and a Fedora Btrfs install sit on the other NVMe — so any repartitioning runs straight into the hard rule requiring explicit per-disk confirmation, on a disk holding other operating systems.

Rollback also is not one requirement. Three separate things get conflated:

1. **Host rollback** — undo a kernel or NVIDIA driver update that breaks graphical login or CUDA. This is what Phase 1 means and what this ADR addresses.
2. **Database rollback** — recover the event store, corpus, and ledgers. That is `pg_dump`/PITR and belongs to the still-open backup decision. A filesystem snapshot of a running Postgres is not a backup.
3. **Application rollback** — revert a bad deployment. That is git plus versioned self-contained .NET publishes.

Solving (1) does not solve (2) or (3), and this ADR does not claim to.

## Alternatives considered

| Option | Strengths | Weaknesses | Why not chosen |
|---|---|---|---|
| **Timeshift, RSYNC mode, ext4** | Already installed. No repartition, no reinstall, no risk to the other operating systems on this machine. Restores from the live USB already on hand. Hardlink-based, so incrementals are cheap. Excludes `/home` by default, which is the correct default here. | Not atomic — snapshotting a running system can catch a torn state. Slower than CoW for both snapshot and restore. Consumes root filesystem space, so a full root breaks the safety net. Not bootable-to-snapshot; restore is a deliberate operation from a live session. | — proposed choice |
| **Reinstall onto Btrfs subvolumes + Timeshift BTRFS mode** | Atomic CoW snapshots, near-instant, `@`/`@home` layout is what Timeshift's Btrfs mode expects. Matches every existing document. | Requires reinstalling the host that ADR-0001 just proposed keeping, and re-verifying GPU, desktop, audio, and .NET. Btrfs plus the proprietary NVIDIA driver is a well-trodden path but adds a variable to an already-working system. | The reinstall cost is the whole objection, and it lands on a host that currently works |
| **LVM thin snapshots** | Filesystem-agnostic, atomic, well understood. | The root partition is a plain ext4 partition, not an LV. Converting means a repartition on a multiboot disk — the highest-risk option here for the least distinctive benefit. | Repartition risk on a disk shared with other operating systems |
| **No host snapshots; rebuild from configuration-as-code** | Forces provisioning to be reproducible, which is valuable on its own merits and will be needed regardless. | Provisioning scripts do not exist yet. Recovery is measured in hours, during which the unattended services are down. Does not satisfy Phase 1 as written. | Correct long-term complement, insufficient as the answer today |

## Evidence

```
$ findmnt -no SOURCE,FSTYPE,OPTIONS /
/dev/nvme0n1p3 ext4 rw,relatime,errors=remount-ro

$ df -h /
/dev/nvme0n1p3  1.4T   26G  1.3T   2% /

$ timeshift --list
Mode   : RSYNC
Status : No snapshots on this device
First snapshot requires: 0 B
No snapshots found

$ which timeshift btrfs lvs
/usr/bin/timeshift  /usr/bin/btrfs  /usr/sbin/lvs
```

Timeshift is installed and in RSYNC mode by default, and has never been configured or run — the safety net Mint ships is currently doing nothing. Root has 1.3 T free against 26 G used, so snapshot capacity is not a constraint; a full-system rsync snapshot with hardlinked incrementals is comfortably affordable.

`lsblk` confirms the multiboot layout the repartitioning options would have to disturb: `nvme1n1` carries a 1015 G NTFS Windows volume and an 845 G Btrfs Fedora install; `nvme0n1` carries a 433 G NTFS volume alongside the Mint root and the EFI system partition.

**Honest limitation, stated rather than buried:** rsync snapshots of a live system are not atomic, and this is a genuine weakness against Btrfs CoW rather than a technicality. It is acceptable for the failure this must survive — a kernel or driver update taken in a controlled window, where the pre-update state is quiescent by construction — and it is not acceptable as a database backup, which is why (2) above stays a separate decision.

## Consequences

**Easier.** Phase 1 can close without touching a partition table. The recovery path uses the Mint live USB that is already present. Controlled update windows for kernel and NVIDIA driver become viable, which is the specific mitigation the charter's risk register asks for.

**Harder.** Restores are manual and slower than a CoW rollback, and require booting the live USB. Root filesystem space must be monitored, because a full root silently disables the safety net.

**Locked in.** Nothing structural. Timeshift can switch to BTRFS mode later if the host is ever reinstalled onto subvolumes; snapshots taken under RSYNC mode do not carry over, but they are disposable by nature.

**Implemented 2026-08-22.** Timeshift configured in RSYNC mode against the root device
`fe233bc7-f9ce-4937-8f07-ef2e79ac1b3a`, scheduled daily, weekly, and on boot with
retention 5/3/3, excluding `/home`, `/root`, `/var/lib/docker`, and the apt cache. First
snapshot `2026-08-22_20-17-07` took 299 s and occupies 22 G; verified to contain `/etc`,
`/usr`, and `/var` with the excluded trees present only as empty stubs.

**Still required, and this ADR is not fully satisfied without it: one restore actually
rehearsed from the live USB.** An untested restore is not a rollback path, it is an
assumption, and acceptance-suite item 13 exists precisely because this project does not
accept unverified success claims. It needs a reboot and cannot be done from a session on
the running host.

**Two limits to state plainly.** Snapshots live at `/timeshift` on the same physical
device as the root filesystem, so they protect against a bad update and not against
drive failure — which is the failure this ADR set out to address, but the distinction
should not be blurred later. And **the database is not covered**: the cluster's data
directory sits under `/home/steve/Data`, which is excluded. That is correct — an rsync
copy of a live data directory is not a backup — but it means database recovery rests
entirely on a `pg_dump` schedule that does not yet exist.

## Reversal path

Trivial in the direction that matters: if snapshots prove inadequate, the host can be reinstalled onto Btrfs subvolumes at any point before Phase 2 puts real data on the machine, and Timeshift switches to BTRFS mode with no other change. After that point the reinstall cost includes migrating a populated PostgreSQL instance, so — as with ADR-0001 — the decision to reverse should be made before Phase 2, not during it.
