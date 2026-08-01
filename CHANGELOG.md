# Changelog

## Revamped

### CPU Set fixes
- **CPU Sets now apply to existing threads.** The process default CPU Set only constrains threads created *after* it is set, so threads already running never moved. CPU Set Setter now pins every existing thread of the process to the CPU Set as well (`SetThreadSelectedCpuSets`), and releases them when the mask is cleared.
- **Affinity and CPU Sets no longer conflict.** When a mask is applied, the other restriction type is always cleared first. Previously a leftover Affinity could produce an empty intersection with a CPU Set, leaving threads stuck on the Affinity's cores.
- **"Clear mask on close" works.** Clearing now removes both restriction types instead of failing with a NotImplementedException.
- **Per-core usage display corrected.** Windows reports a stale "ideal processor" for threads confined to a CPU Set (it keeps claiming the E-cores while the thread actually runs on a P-core). The per-core usage readout now redistributes such misplaced usage into the cores of the active CPU Set, so the heatmap matches reality.
- **Live verification read-back.** The process details row now shows the restriction that is *actually* applied (CPU Set IDs / cores, or Affinity mask) read straight from the OS, so you can confirm a mask really took effect.

### UI
- **Per-core CPU usage heatmap.** Click a process row to expand per-core usage bars in its details, colored by load (idle gray to red). Click the row again or press "Hide" to collapse.
- **Only-used-cores filter.** Idle cores are filtered out of the per-core view so the bars stay compact and readable.
- **Busiest cores summary** shown next to the per-core header.
- **Live sorting** by CPU usage.

### Internal
- Minor code/comment cleanup.
