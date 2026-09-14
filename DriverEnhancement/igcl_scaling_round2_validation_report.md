# ShadePilot IGCL Display Sharpness 第二轮验证报告

## Latest Follow-up（Gate 0 + virtual-mode 同步）

按追加要求，先对当前 CCD 数组原封不动执行 Gate 0：

```text
SetDisplayConfig(SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG |
                 SDC_VIRTUAL_MODE_AWARE | SDC_VIRTUAL_REFRESH_RATE_AWARE)
return = 0x0 (SUCCESS)
```

Gate 0 成功后，按本机 SDK 实际返回的索引同步构造候选：

- `sourceModeInfoIdx=1`：SOURCE `1920×1080 → 1280×720`
- `desktopModeInfoIdx=2`：`PathSourceSize=1280×720`，DesktopImageRegion/Clip=`0,0,1280,720`
- `targetModeInfoIdx=0`：TARGET 保持 `1920×1080`
- `DISPLAYCONFIG_PATH_TARGET_INFO.scaling=DISPLAYCONFIG_SCALING_STRETCHED (3)`
- Path Flags 保持 `0x9`，Adapter/Source ID/Target ID/刷新率均保持原值

修正后的 720p 候选 `SDC_VALIDATE` 返回：

```text
0x0 (SUCCESS)
```

随后按下一步授权执行了一次临时 `SDC_APPLY`，并由独立 15 秒守护进程负责恢复；未使用 `SDC_SAVE_TO_DATABASE`。

## Test Environment

- 日期：2026-08-26
- 独立工具：`ShadePilot.CcdProbe.exe`
- 工具路径：`F:\sp_driver_control_build\Release\ShadePilot.CcdProbe.exe`
- 测试目录：`F:\sp_driver_control_validation_20260826_round2`
- 规则：Gate 0 → 临时 `SDC_APPLY` → CCD 回读 → IGCL Scaling；Sharpness A/B 仍未执行。

## Original CCD Configuration

连续 3 次 `probe` 成功，三份 JSON 的 SHA256 均为：

`081EA85A38A6042B328969D6139212367D58DA66446899E9550E9FBD0F31C319`

- PathCount：1
- ModeCount：3
- Active Path：0
- Path Flags：9（Active + `DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE`）
- Adapter LUID：`0:65837`（十六进制输出 `0:1012D`）
- Source ID：0，Source ModeInfoIdx：1
- Target ID：41015，Target ModeInfoIdx：0
- Output：HDMI（Windows SDK 当前枚举值 5）
- Windows Path Scaling：1
- Refresh：41558000 / 230880（约 180 Hz）
- Source：1920×1080，PixelFormat=4，Position=(0,0)
- Target active：1920×1080
- Target total：2080×1110
- Target pixel rate：415580000
- ModeInfo[2]：DESKTOP_IMAGE，ID=41015

为兼容当前系统的虚拟模式路径，查询使用了
`QDC_ONLY_ACTIVE_PATHS | QDC_VIRTUAL_MODE_AWARE | QDC_VIRTUAL_REFRESH_RATE_AWARE`。
`pCurrentTopologyId` 按可选参数传入 NULL；此前传入指针时 Windows 返回 `0x57`。

## Original IGCL Scaling

只读 IGCL 基线读取工具返回成功（`igcl_scaling_round2_igcl_baseline.json`）：

- Display：Type=2，DisplayConfigFlags=3，Timing=1920×1080 @ 179.998 Hz
- Scaling Caps：`0x0`，SupportedScaling=31
- Current Scaling Read：`0x0`
- Enable：`true`
- Current Scaling：`IDENTITY / 1`
- Preferred Scaling：`ASPECT_RATIO_CENTERED_MAX / 8`
- Custom X/Y：0/0
- HardwareModeSet：`false`

## 720p Configuration Validation（修正后）

Gate 0 通过后，基于当前完整 path/mode 数组复制，并同步修改 virtual-mode 所需的 SOURCE、DESKTOP_IMAGE 与 path scaling；Target ModeInfo[0] 保持 `1920×1080`，其他 Adapter、ID、输出、刷新率、Path Flags 保持不变。

调用：

```text
SetDisplayConfig(
  ..., 
  SDC_VALIDATE |
  SDC_USE_SUPPLIED_DISPLAY_CONFIG |
  SDC_VIRTUAL_MODE_AWARE |
  SDC_VIRTUAL_REFRESH_RATE_AWARE
)
```

Gate 0 实际返回：

```text
0x0 (SUCCESS)
```

同步后的 720p 候选实际返回：

```text
0x0 (SUCCESS)
```

验证 JSON：`F:\sp_driver_control_validation_20260826_round2\igcl_scaling_round2_gate0_720p_validation.json`

Gate 0 JSON：`F:\sp_driver_control_validation_20260826_round2\igcl_scaling_round2_gate0_720p_validation.json.gate0.json`

历史记录：未同步 DESKTOP_IMAGE/path scaling 的旧候选曾返回 `0x57`；该候选未 Apply。

## 720p Apply Result

`0x0 (SUCCESS)`。

APPLY 前启动 15 秒恢复守护；未使用 `SDC_NO_OPTIMIZATION`，因为首次 APPLY 后 Gate 1 已成功。

## Actual Source Mode After Apply

`1280×720`。

`PathSourceSize=1280×720`，DesktopImageRegion/Clip=`0,0,1280,720`。

## Actual Target Mode After Apply

`1920×1080` active；total=`2080×1110`，pixelRate=`415580000`，刷新率约 180Hz。

Windows Path Scaling=`DISPLAYCONFIG_SCALING_STRETCHED / 3`，Path Flags=`0x9`。

## IGCL Scaling After Apply

实验模式下只读回读：`Return=0x0`，`ScalingType=IDENTITY / 1`，`Preferred=ASPECT_RATIO_CENTERED_MAX / 8`，SupportedScaling=`14`。

随后调用既有 IGCL `ctlSetCurrentScaling`：`SetResult=0x0`；回读为 `ScalingType=STRETCHED / 4`、`Preferred=STRETCHED / 4`、SupportedScaling=`30`。

## STRETCHED Gate

`Reached = YES`。

真实状态同时满足：Windows Source=`1280×720`、Target=`1920×1080`、Path Scaling=`STRETCHED`、IGCL `ScalingType=4`。

状态快照：`F:\sp_driver_control_validation_20260826_round2\igcl_round2_stretched_state.json`

## Sharpness A/B

`NOT EXECUTED`。按计划在 Gate 2 成功后暂停，未自动切换 Sharpness。

Gate 2 期间 Sharpness 只读探针结果：

- IGCL 版本：65537
- Adapter：Intel(R) Arc(TM) B580 Graphics
- Display Output 0：Caps=`0x0`
- Sharpness filter：FilterType=1，范围 1–5，Step=1
- Current：FilterType=1，Intensity=5

## 900p Verification

`NOT EXECUTED`。按计划在 720p Gate 2 成功后暂停。

## Restore Result

CCD：15 秒守护恢复后回读为 Source=`1920×1080`、Target=`1920×1080`、DESKTOP_IMAGE=`1920×1080`、Path Scaling=1；父进程执行了恢复兜底，返回 `0x0`。

IGCL：实验中的 STRETCHED 已恢复为 `ScalingType=IDENTITY / 1`、`PreferredScalingType=ASPECT_RATIO_CENTERED_MAX / 8`，最终恢复命令与回读均返回 `0x0`。

最终 CCD 回读：`F:\sp_driver_control_validation_20260826_round2\post-apply-final-ccd.json`；最终 IGCL 回读：`F:\sp_driver_control_validation_20260826_round2\igcl_round2_final_baseline_verified_v2.json`。

## Conclusion

本轮已通过 Gate 0、临时 APPLY、Gate 1 和 Gate 2：当前 Arc B580 + 当前驱动上，Windows CCD 可以建立真实 `1280×720 Source → 1920×1080 Target`，并且 IGCL `ctlSetCurrentScaling` 可回读 `STRETCHED / 4`。按计划已在 Sharpness A/B 前暂停；所有 CCD 与 IGCL 状态已恢复到基线。Sharpness 是否产生视觉效果仍未测试。
