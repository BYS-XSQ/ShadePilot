# Sharpness 视觉验证记录

状态：**接口验证通过，视觉效果未证实，按计划暂停后续自动化接入**

## 复现

在实际机器上对 Intel Arc B580 的 Output 0 执行：

    ShadePilot.DriverControl.exe --set 0 1 1 5 sharpness-backup.txt

结果：

    setResult=0x0
    readbackResult=0x0
    enabled=true
    filterType=1
    intensity=5

独立 --inspect 再次读取同样结果，说明设置确实被驱动保存并回读。

## 输出映射证据

- displayConfigFlags=3：按当前 IGCL 枚举含义为 Display Active + Display Attached。
- encoderConfigFlags=0：不是虚拟显示，也未标记内部显示。
- displayType=2：HDMI。
- featureEnabledFlags=2：当前仅报告 HD Audio 已启用；featureSupportedFlags=35 表示 HDR 是能力项但当前未启用，因此本次现象不能归因于 HDR 模式。
- capsResult=0x0，FilterType 1 的范围为 1..5。

因此目前没有证据表明是“写入了非活动输出”或“写入了虚拟输出”。

## 观察结果

将强度从 1 提高到 5 后，用户仍未观察到明显画面变化。

进一步使用数字边缘的像素点进行对比，1 与 5 仍未出现任何可见差异；因此不再将其归因于普通场景下的“效果太轻”。

## 结论边界

当前官方 IGCL 文档明确了 ctlGetSharpnessCaps、ctlGetCurrentSharpness、ctlSetCurrentSharpness 的调用和返回语义，但没有承诺所有显示链路、分辨率、缩放状态下都能产生肉眼可见的锐化效果。当前证据更接近以下两种可能：

1. 当前驱动/显示链路接受并保存设置，但实际显示管线没有呈现该滤镜；
2. 该滤镜只在特定缩放或内容条件下生效，当前桌面/游戏画面不足以观察。

## 缩放路径只读核对

为区分“无缩放路径导致滤镜不介入”与“驱动完全忽略设置”，在不改变分辨率和缩放的前提下，使用官方 ctlGetSupportedScalingCapability 与 ctlGetCurrentScaling 连续读取 3 次：

- Output 0 每次均为 scalingCaps=0x0、scalingCurrent=0x0、scalingEnabled=true、scalingType=1。
- 当前 IGCL 枚举中 CTL_SCALING_TYPE_FLAG_IDENTITY=1，官方注释为“不应用缩放”。
- Output 1 及后续输出的当前缩放读取返回 0x48000004，按官方枚举为 CTL_RESULT_ERROR_DISPLAY_NOT_ACTIVE，不是当前活动输出。

这组证据支持：当前实际活动的 Output 0 是 Identity/不缩放路径；因此显示输出 Sharpness 可能没有经过能观察到锐化的缩放阶段。它解释了“接口写入并回读成功但数字边缘无变化”的现象，但不能证明所有驱动版本或非原生分辨率都会如此。

另外，当前 IGCL 返回的 3D Feature 列表中没有 CTL_3D_FEATURE_SHARPENING_FILTER；因此不能把显示输出 Sharpness 误当作游戏 3D 锐化，也不能在当前证据下擅自改用未报告支持的 3D Set 接口。

## A/B 记录

用户切换游戏分辨率后，Output 0 仍连续报告 Identity。随后执行同一活动输出的强度 1 → 5 对比：

- 强度 1：setResult=0x0，readbackResult=0x0，readback intensity=1。
- 强度 5：setResult=0x0，readbackResult=0x0，readback intensity=5。
- 强度 5 后再次只读探针：scalingType=1，scalingEnabled=true，路径未改变。

因此本次 A/B 已证明接口写入和回读可靠，但游戏切换没有使 IGCL 看到非 Identity 缩放路径；若 1 与 5 视觉仍无差异，证据进一步指向当前驱动/输出管线未实际呈现该 Sharpness，而不是软件没有写进去。

在获得可重复的视觉证据前，不把该功能标记为“已生效”，也不进入自动应用、游戏 Profile 或后台常驻写入。

当前验证结果已经达到“接口可写，但本机链路视觉无效”的证据强度；后续如继续研究，应转为驱动版本/显示链路兼容性调查，不应直接扩大功能范围。

验证依据：[Intel IGCL Control API](https://intel.github.io/drivers.gpu.control-library/Control/api.html) 中的 Sharpness 能力、当前值和设置接口说明。
