# ShadePilot Driver Enhancement — WPF 最小入口

状态：**已编译并完成启动烟雾测试；等待集中验收**

## 本阶段范围

- 在 WPF 新界面的“工具中心”加入独立的“驱动增强（实验）”卡片。
- 默认不探测、不写入、不自动应用；只有用户显式点击按钮才会启动 Probe 或 Control。
- 仅提供 Intel IGCL Sharpness 的最小入口：选择 Probe、选择控制器、读取能力、显式写入并回读、恢复备份。
- 不修改现有 Gamma、DDC/CI、软件饱和度、游戏检测和 Profile 逻辑。
- 读取状态现在额外显示活动输出标记和虚拟输出标记，避免将“接口成功”误判为“画面一定可见”。
- 写入 UI 不再硬编码 FilterType=1，而是根据当前 Probe 返回的滤镜类型列表动态选择。

## 编译验证

项目：`wpf/ShadePilot.Wpf.csproj`

命令：

```text
dotnet build wpf/ShadePilot.Wpf.csproj -c Release --nologo
```

结果：成功，0 个警告，0 个错误。

## 启动验证

- Release 可执行文件存在：`wpf/bin/Release/net8.0-windows/ShadePilot.Wpf.exe`
- 独立启动 5 秒，进程保持运行且未崩溃，随后由测试脚本关闭。
- 在保留用户设置的测试中，`StartMinimized=true`，主窗口句柄为 0，符合“启动后最小化到托盘”的现有设置；这不是窗口创建失败证据。
- 另一次可恢复的可见窗口测试临时将 `StartMinimized` 设为 `false`，5 秒后取得窗口句柄 `4982832`、标题为 `ShadePilot`，随后关闭进程并原样恢复配置；未观察到闪退。
- 启动路径不调用 IGCL Set，不改写显示参数。

## 追加验证

- 托管 ReadTest：成功，Arc B580、25 个输出、Output 0 当前 Intensity=5。
- WPF 可见启动烟雾测试：进程保持运行、窗口句柄有效、标题为 `ShadePilot`，随后正常关闭。

## 视觉效果边界

- 控制器写入并回读成功不代表当前显示链路一定有肉眼可见变化。
- 当前机器的实际验证记录为 Output 0、活动 HDMI、Intensity 5，但用户未观察到明显差异；该结果已记录在 `PHASE3_VISUAL_VALIDATION.md`，因此后续自动应用仍保持暂停。

## 集中验收点

1. 取消“启动后最小化到托盘”，打开工具中心，确认实验卡片可见。
2. 选择 `ShadePilot.DriverProbe.exe`，点击“读取能力”，确认能显示 Arc B580、输出数量和 Sharpness 原始结果。
3. 确认只有点击“写入并回读”才发生写入；未点击时不应改变画面。
4. 需要撤销时点击“恢复备份”，再用独立 `--inspect` 复核。

当前真实机器状态仍记录在 `PHASE3_STATUS.md`：Output 0 的 Sharpness 为 FilterType 1、Intensity 5；强度 1 → 5 A/B 均已回读成功但未观察到可见差异。A/B 备份位于 `F:/sp_driver_control_validation_20260826_ab_non_native/ab-backup.txt`。
