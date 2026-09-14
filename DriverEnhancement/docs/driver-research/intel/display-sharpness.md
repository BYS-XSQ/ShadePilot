# Intel Display Sharpness — Archived

Status: `KnownIneffective` on the tested Intel Arc B580 and current driver path.

The prior experiment established Windows 1280×720 Source → 1920×1080 Target stretched scaling and read back IGCL `ScalingType=STRETCHED / 4`. IGCL Display Output Sharpness FilterType 1 accepted and read back 1, 3 and 5.

The user compared the same picture at 1, 3 and 5 and observed no visible difference. API read/write support must not be represented as a user-visible image-enhancement feature.

Evidence retained:

- `../../../igcl_sharpness_validation_report.md`
- `../../../igcl_scaling_round2_validation_report.md`
- `../../../igcl_sharpness_final_ab_report.md`

The shipping UI must not display Intel Display Sharpness. Future DriverLab reports may list it only as internal `KnownIneffective` research metadata.
