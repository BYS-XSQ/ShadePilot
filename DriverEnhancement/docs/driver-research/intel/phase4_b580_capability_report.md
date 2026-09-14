# ShadePilot Driver Enhancement — Intel Arc B580 Phase 4 Report

Date: 2026-08-26  
Mode: read-only `ShadePilot.DriverLab.exe probe`  
Phase boundary: complete through Intel capability discovery only. No formal Driver Enhancement UI, no game profile wiring, and no driver Set operation was added.

## Verification evidence

- DriverLab build: `F:\sp_driverlab_build\Release\ShadePilot.DriverLab.exe`
- Output root: `F:\sp_driverlab_validation_20260826`
- Three same-build reads: `run2`, `run3`, `run4`
- Each exited `0`; IGCL initialization, adapter enumeration and 3D capability enumeration all returned `0x0`.
- Comparing the three JSON files after removing their timestamp field produced no difference.
- DriverLab calls `ctlGetSupported3DCapabilities` and, only for non-custom values, `ctlGetSet3DFeature` with `bSet=false`. It contains no `ctlSetCurrent*` and no `bSet=true` path.

## A. Existing code integration points

Existing ShadePilot behavior remains centered on:

- `DisplayPreset.cs`: monitor DDC/CI, Gamma/software colour, game detection and persisted game rules.
- `wpf/MainWindow.Full.cs`: display, style and tools UI. The former Intel Sharpness experiment card is now excluded from the shipping UI.
- `DriverEnhancement/Managed`: new vendor-neutral contracts with no WPF dependency.
- `DriverEnhancement/Intel/igcl-upstream`: fixed local official IGCL Header/wrapper source used for the native test tools.

The future Profile integration point is the existing game-rule path, but it is intentionally untouched in this phase.

## B. New Driver Enhancement architecture

Added under `DriverEnhancement/Managed`:

- `IDriverEnhancementProvider`: Initialize, adapter/capability/state reads, explicit Set, snapshot, restore, validation and shutdown contracts.
- `DriverFeatureCapability`: vendor, availability, read/write/restore flags, scope, restart/per-app/live-change flags, ranges, values, verification level and reason.
- `DriverFeatureState`, `DriverSnapshot`, `DriverAdapterInfo` and typed result models.
- `DriverEnhancementService`, `DriverProfileManager`, `DriverRestoreManager`: dependency-free future boundaries; none is instantiated by the WPF application yet.
- `IntelIgclProvider`: an intentional phase-boundary placeholder. Its Set/Restore methods return blocked results; actual discovery remains in DriverLab.

Availability and verification are separate. In particular, an API that reports a feature is not promoted to `EffectVerified` merely because it can be queried.

## C. Current B580 capability result

Hardware/API returned:

- Adapter: `Intel(R) Arc(TM) B580 Graphics`
- PCI Device ID: `57867`
- IGCL requested and supported version: `65537`
- Driver version raw: `9007199261368992`
- `ctlGetSupported3DCapabilities`: `0x0`, 10 raw features returned; 9 of the product-report rows are current-driver capabilities and one is a historical/archived classification.

| Feature | Capability | Current read | Per-app / Live | Phase-4 status |
|---|---|---|---|---|
| Frame Limit | Supported; Int32 30–300, step 1, default 60 | `0x0`: disabled, value 0 | Yes / Yes | `ReadVerified` |
| Low Latency | Supported; Enum mask 3, default 0 | `0x40000014` (data not found) | Yes / Yes | `ApiExposed`; not read-verified |
| Texture Filtering Quality | Not returned | Not queried | No / No | Unsupported |
| CMAA | Supported; Enum mask 0, default 0 | `0x40000014` | Yes / No | `ApiExposed`; not read-verified |
| MSAA | Not returned | Not queried | No / No | Unsupported |
| Gaming Flip Modes | Supported; Enum mask 111, default 1 | `0x0`: value 1 | Yes / Yes | `ReadVerified`; research only |
| Adaptive Sync Plus | Not returned | Not queried | No / No | Unsupported |
| App Profiles | Supported; Custom payload size 16 | Not queried safely | Yes / No | `ApiExposed` |
| Global Or Per-App | Supported; Enum mask 0 | `0x40000014` | Yes / No | `ApiExposed`; not read-verified |
| Live State | Supported; Custom payload size 28 | Not queried safely | Yes / Yes | `ApiExposed` |
| Frame Generation | Supported; Enum mask 0 | `0x40000014` | Yes / Yes | `ApiExposed`; prohibited by this product phase |
| Frame Generation Control | Not returned | Not queried | No / No | Unsupported |
| Prebuilt Shader Download | Supported; Bool default false | `0x0`: disabled | No / No | `ReadVerified`; not a current product candidate |

`0x40000014` is preserved as the raw driver result. DriverLab does not invent a current value, allocate undocumented custom payloads, create profiles, or issue a Set call to obtain more information.

Machine-readable and human-readable raw reports are in:

- `F:\sp_driverlab_validation_20260826\run4\intel_3d_capability_report.json`
- `F:\sp_driverlab_validation_20260826\run4\intel_3d_capability_report.md`
- `F:\sp_driverlab_validation_20260826\run4\logs\driver-enhancement.log`

## D. Intel controls excluded from formal candidates

| Control | Classification | Reason |
|---|---|---|
| Intel Display Sharpness | `KnownIneffective` | B580 test path accepted and read back 1, 3, 5, but the user observed no visible difference. |
| Anisotropic Filtering | `KnownRemoved` | Not returned on this driver; excluded by the current product policy. |
| Adaptive Tessellation | `KnownRemoved` | Not returned on this driver; excluded by the current product policy. |

The historical probes and reports remain referenced in `tools/driver-probe/intel/ARCHIVE.md`; no shipping UI exposes them.

## E. Next-stage candidates — not executed

1. **Frame Limit** is the only immediate candidate. It is supported, has a concrete range, supports per-app/live change and has a successful current-state read. A later dedicated test may use explicit Backup → Set 60 → Get → game FPS observation → Set 120 → Get → Restore.
2. **Low Latency** is a conditional research candidate, not ready for Set/Get validation. Capability is reported but a global read currently returns `0x40000014`; the next stage must first establish a documented read path without creating or modifying a profile.
3. **Gaming Flip Modes** is technically read-verified, but remains probe-only because it can affect compatibility, VRR and frame pacing. It is not approved for a product Set test.

No other B580 feature satisfies the evidence threshold for a formal test in this phase. This report deliberately stops here.
