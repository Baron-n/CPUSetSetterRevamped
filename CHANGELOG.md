# Changelog

## Revamped

### v2.8.0

#### Bug fixes
- **CPU Set / Affinity integrity.** Fixed cases where a stale or narrow Affinity silently constrained an applied CPU Set (and vice versa), making a CPU Set behave like an Affinity. A failed cross-clear is now surfaced in the log instead of being hidden, a "clear" is only reported as success when both restriction types are actually gone, an empty CPU Set mask now releases existing threads' per-thread CPU Set pins, and the reapply loop now detects and repairs a drifted restriction of the other type. Also fixed a potential crash in the CPU Set read-back and a wrong-handler unsubscribe in the mask editor.
- **Fixed an OS handle leak** where both the trace and polling process listeners fired for the same process start, leaving the duplicate's process handle open.

### v2.5.0

#### New features
- **No CPU usage polling while in the system tray.** When the app is minimized to the tray, per-process CPU usage, per-core usage and live sorting are fully paused (not just slowed down), so the app uses no measurable CPU in the background. Updates resume as soon as the window is shown again.

#### Bug fixes
- **Version display and update check fixed.** The app previously misread the released version as a development build, showing "Dev" in the Settings tab and never checking for updates. It now reports the real version (e.g. `v2.0.0`) and runs the update check against the GitHub releases page.

### v1.0.0

#### New features
- **Priority class per rule.** Each program rule now has a Priority setting (Idle / Below Normal / Normal / Above Normal / High / Realtime). It is applied immediately when the rule matches, and the OS privilege needed for High/Realtime is enabled on demand.
- **Mask editor grouped by CPU topology.** The mask editor now lays out cores by die/CCX (labeled "CCD n" on AMD, "Die n" elsewhere) and visually boxes the SMT threads of each physical core, so you can see at a glance which logical processors share a core. Falls back to a simple split when die detection isn't available.
- **Instant process start/stop events.** The process list now updates the moment a process starts or exits (via Windows ETW trace events) instead of every 5 seconds, so rules apply sooner.
- **Always run as administrator.** The app now requires elevation so every feature works reliably (CPU Sets on other processes, the ETW event trace, and priority changes all need admin rights). Launching shows a single UAC prompt; the auto-start task already runs elevated, so no extra prompt at logon.

#### UI
- **Priority column** in the Rules tab, with a dropdown when creating/editing a rule.

#### Internal
- CPU Set fixes, per-core usage heatmap, and live restriction read-back (see below).

### CPU Set fixes
- **CPU Sets now apply to existing threads.** The process default CPU Set only constrains threads created *after* it is set, so threads already running never moved. CPU Set Setter Revamped now pins every existing thread of the process to the CPU Set as well (`SetThreadSelectedCpuSets`), and releases them when the mask is cleared.
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
