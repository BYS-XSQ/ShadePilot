# Intel Sharpness experiment archive

This folder is the Phase 1 research entry point. The original native sources remain in place because the historical CMake project references them directly; they are not compiled into the shipping .NET provider or shown in the WPF UI.

| Retained evidence | Source location |
|---|---|
| Original IGCL read-only probe | `../../../Intel/ShadePilot.DriverProbe/main.cpp` |
| Sharpness write/restore experiment tool | `../../../Intel/ShadePilot.DriverProbe/driver_control.cpp` |
| CCD/scaling validation tool | `../../../Intel/ShadePilot.DriverProbe/ccd_probe.cpp` and `scaling_validation.cpp` |
| Initial sharpness report | `../../../igcl_sharpness_validation_report.md` |
| Round 2 CCD/scaling report | `../../../igcl_scaling_round2_validation_report.md` |
| Final 1→3→5 A/B report | `../../../igcl_sharpness_final_ab_report.md` |

Product status: `Intel Display Sharpness = KnownIneffective`. These artifacts are retained for research and incident evidence only; they are not a release feature.
