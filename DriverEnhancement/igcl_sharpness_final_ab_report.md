# ShadePilot IGCL Display Output Sharpness 一次序列验证报告

## 本次执行范围

按最新指令，在不改写 ShadePilot 正式滤镜核心、不加入 UI 的前提下，完成一次 `1 → 3 → 5` 锐化强度序列。每个值均执行 IGCL Set 后立即 Get 回读；实验前先建立 Windows CCD 临时 `1280×720 Source → 1920×1080 Target` 和 IGCL `STRETCHED` 状态，结束后恢复。

## 环境与 Gate

- 日期：2026-08-26
- 适配器：`Intel(R) Arc(TM) B580 Graphics`
- IGCL 版本：`65537`
- 驱动版本原始值：`9007199261368992`
- Display Output：0，HDMI，1920×1080 @ 179.998 Hz
- Sharpness Caps：`FilterType=1`，最小 `1`，最大 `5`，步长 `1`
- Caps/Current 读取：均返回 `0x0 (SUCCESS)`
- Windows CCD Gate 0：原始 CCD 原封不动 `SDC_VALIDATE` 返回 `0x0`
- 720p 候选 Gate 0：同步修改 SOURCE、DESKTOP_IMAGE 和 path scaling 后返回 `0x0`
- 临时 APPLY：返回 `0x0`；Gate 1（Source 1280×720、Desktop Image 1280×720、Target 1920×1080、Path Scaling=STRETCHED）通过
- IGCL `ctlSetCurrentScaling`：返回 `0x0`，回读 `ScalingType=STRETCHED / 4`

实验入口证据：

`F:\sp_driver_control_validation_20260826_round2\sharpness_ab_135_apply.stdout.txt`

## 1 → 3 → 5 Set/Get 结果

| 序列 | Set 返回 | Get 返回 | 回读值 | 证据 |
|---:|---|---|---:|---|
| 1 | `0x0` | `0x0` | `FilterType=1, Intensity=1` | `sharpness_ab_135_set_1.txt` |
| 3 | `0x0` | `0x0` | `FilterType=1, Intensity=3` | `sharpness_ab_135_set_3.txt` |
| 5 | `0x0` | `0x0` | `FilterType=1, Intensity=5` | `sharpness_ab_135_set_5.txt` |

三次调用均满足请求值与回读值一致，说明在本次临时 `STRETCHED` 状态下，Display Output Sharpness 的写入接口和读取接口都可用。

## 视觉效果结论

用户实际观察结果：在同一画面下对比 1、3、5，肉眼未观察到变化。因而本次结果应区分为：驱动接口接受并回读了三个强度值，但当前显示链路没有呈现可感知的锐化差异；不能据此宣称画面已变清晰。

本次未执行 FilterType=1 的第二轮重复序列，也未执行 Identity 对照组；按严格范围不自行扩展。

## 恢复结果

- CCD 最终回读：Source=`1920×1080`，Desktop Image=`1920×1080`，Target=`1920×1080`，Path Scaling=`1 (IDENTITY)`。
- IGCL 最终回读：`ScalingType=1 (IDENTITY)`，`PreferredScalingType=8 (ASPECT_RATIO_CENTERED_MAX)`，读取返回 `0x0`。
- Sharpness Display Output 0 最终回读：`FilterType=1`，`Intensity=5`，读取返回 `0x0`。

最终证据：

- `F:\sp_driver_control_validation_20260826_round2\post-sharpness-135-final-ccd.json`
- `F:\sp_driver_control_validation_20260826_round2\sharpness_ab_135_final_igcl_verified.json`
- `F:\sp_driver_control_validation_20260826_round2\sharpness_ab_135_final_restore_backup.txt`

## 结论

本次“直接 1→3→5”验证已完成并通过 Set/Get 技术检查；用户实测 1、3、5 肉眼无变化。系统状态已恢复到实验前记录的 1080p、Identity 缩放和 Sharpness=5。当前证据指向“控制接口生效但可见效果未呈现”，后续若继续应优先检查输出链路、驱动策略或该显示器是否实际使用该锐化路径。
