# Intel DriverLab (research only)

`ShadePilot.DriverLab.exe probe [output-directory]` is the only supported command in this phase. It uses the official local IGCL Header/wrapper and performs capability and current-state queries only.

It writes `intel_3d_capability_report.json`, `intel_3d_capability_report.md`, and `logs/driver-enhancement.log`.

There is no `test-feature` command yet. The executable contains no IGCL Set call. Do not package the legacy Sharpness controllers into a ShadePilot release.
