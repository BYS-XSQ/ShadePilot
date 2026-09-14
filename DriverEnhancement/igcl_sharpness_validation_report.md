# ShadePilot Intel IGCL Display Output Sharpness 验证报告

## 本轮范围

严格执行到“是否能进入真实 STRETCHED 缩放路径”为止。未进行 Sharpness MIN/MAX A/B，未修改 ShadePilot UI、游戏文件、Profile、Gamma、DDC/CI 或 3D 设置。

## 测试环境

- GPU：Intel Arc B580（前序 Probe 已确认）
- IGCL 实现版本：65537
- 活动输出：Output 0，HDMI，Active + Attached
- 当前显示时序：1920×1080 @ 179.998 Hz
- HDR：未启用（前序读取 featureEnabledFlags=2）

## SDK 枚举（当前固定 Header）

    Identity = 1
    Centered = 2
    Stretched = 4
    AspectRatioCenteredMax = 8
    Custom = 16

## Identity 基线

基线文件：F:\sp_driver_control_validation_20260826_scaler_gate\igcl_sharpness_test_baseline.json

    ctlGetSupportedScalingCapability ReturnCode = 0x0
    SupportedScaling = 31
    ctlGetCurrentScaling ReturnCode = 0x0
    Enable = true
    ScalingType = 1 (IDENTITY)
    PreferredScalingType = 8
    CustomScalingX = 0
    CustomScalingY = 0

Output 0 报告支持 Identity、Centered、Stretched、Aspect Ratio 和 Custom 五类缩放。

## STRETCHED 闸门测试

调用：

    ctlSetCurrentScaling(Output 0, Enable=true, ScalingType=4, HardwareModeSet=true)

返回：

    setResult = 0x0

但立即调用 ctlGetCurrentScaling 回读：

    ReturnCode = 0x0
    Enable = true
    ScalingType = 1 (IDENTITY)
    PreferredScalingType = 4
    CustomScalingX = 0
    CustomScalingY = 0

判定：

    reachedStretched = false

这表示驱动接受了 STRETCHED 请求并更新了 PreferredScalingType，但实际活动缩放模式没有离开 Identity。由于计划要求只有在真实 ScalingType 非 Identity 时才进入 Sharpness 测试，本轮在此停止，未执行任何 Sharpness A/B。

## 恢复结果

已将 PreferredScalingType 恢复为 8，并再次将当前 ScalingType 恢复为 1。最终回读：

    restoreResult = 0x0
    restored = true
    Enable = true
    ScalingType = 1
    PreferredScalingType = 8

本轮没有遗留临时 Scaling 设置。

## 最终结论

### Conclusion C

当前测试环境下无法验证 Display Sharpness 与 GPU Scaling 的关系：

    Unable to force Display Scaling path

虽然当前设备报告支持 STRETCHED，且 Set 返回成功，但实际活动路径始终为 Identity。因此不能据此宣布 Sharpness 无效，也不应继续进行后续 Sharpness A/B 或正式 ShadePilot 集成。
