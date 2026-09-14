# ShadePilot Driver Enhancement — Sharpness Write Gate

状态：**接口写入与回读完成；A/B 视觉验证未观察到效果，按计划停止**

无参数双击行为已修正为默认执行只读 `--inspect`，并在交互式控制台中等待按键，不再因用法退出造成“闪退”观感。

控制台提示已改为 ASCII，避免 Windows 控制台代码页造成中文乱码。

## 已完成

- 独立 ShadePilot.DriverControl.exe
- --inspect：只读枚举 Adapter/Display，并读取 Sharpness Caps/Current
- --set：显式参数、范围校验、写入前备份、写入后回读确认
- --restore：读取备份并回写，写入后回读确认
- 只在 --set 或 --restore 路径调用 ctlSetCurrentSharpness
- 不接入 WPF、不修改 Gamma/DDC/CI、不安装驱动、不修改注册表

## 编译与只读实测

cmake -S ShadePilot.DriverProbe -B F:\sp_driver_control_build -G "Visual Studio 18 2026" -A x64 -DCMAKE_VS_GLOBALS=TrackFileAccess=false
cmake --build F:\sp_driver_control_build --config Release --parallel 2

结果：成功，ShadePilot.DriverControl.exe 和 ShadePilot.DriverProbe.exe 均生成。

真实机器只读检查：

Mode=read-only
Outputs=25
Output=0
Sharpness Caps=0x0
FilterType=1
Range=1..5
Step=1
Current=0

越界保护测试使用强度 6，结果为退出码 3，提示超出官方范围，且没有生成备份文件；因此未进入 Set 调用。

## 第一次实际验证前的确认项（已完成）

实际写入前已确认以下项目：

1. 目标是否为 Intel Adapter 0 的 Display Output 0；
2. 是否允许临时启用 Sharpness FilterType 1；
3. 测试强度是否使用 1（官方最小值）；
4. 是否接受写入前生成 sharpness-backup.txt，并在回读失败时停止。

## 第一次实际写入结果

用户授权后执行：

```text
ShadePilot.DriverControl.exe --set 0 1 1 1 sharpness-backup.txt
```

结果：`setResult=0x0`、`readbackResult=0x0`；独立二次读取确认 Output 0 为 `enabled=true`、`filterType=1`、`intensity=1`。原始状态已备份为：

`F:\sp_driver_control_validation_20260826\sharpness-backup.txt`

后续已完成强度 1 → 5 的同屏 A/B；两次写入与回读均成功，但用户未观察到可见差异。当前状态为 FilterType 1、Intensity 5；如需恢复，可使用该备份执行 `--restore`。

## 托管层封装

已新增 `IntelSharpnessControlProvider`，统一封装显式 Set/Restore 调用，后续 UI 和 Profile 不直接启动控制器。托管写入护栏测试使用越界强度 6，返回 `Succeeded=False`、退出码 `3`，并确认没有进入写入路径。
