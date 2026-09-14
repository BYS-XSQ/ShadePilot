# ShadePilot Driver Enhancement — Phase 1 Status

状态：**Phase 1 验收通过；停在阶段边界，未进入 Phase 2**

## 固定的官方 SDK

- Intel IGCL 官方仓库 commit：`b6c462933502e13d1537dd5024949a51be30e63d`
- Header：`igcl-upstream/include/igcl_api.h`
- Wrapper：`igcl-upstream/Source/cApiWrapper.cpp`
- 系统 runtime：`C:\Windows\System32\ControlLib.dll`
- Probe 独立目录：`ShadePilot.DriverProbe`

Probe 只执行初始化和读取，不调用任何 `ctlSet...` 函数；现有 ShadePilot 滤镜、Gamma、UI 和配置系统没有接入或改写。

## 编译证据

已安装并确认完整的 Visual Studio Community 2026：

- 安装路径：`F:\VS`
- MSVC：`14.51.36231`，编译器版本：`19.51.36256.0`
- CMake：`4.3.1`
- x64 工具链和 CMake 工具组件：已安装
- `isComplete=true`、`isLaunchable=true`、`isRebootRequired=false`

使用 Visual Studio 18 2026、x64、Release 编译成功：

```text
cmake -S ShadePilot.DriverProbe -B F:\sp_shadeprobe_build_final -G "Visual Studio 18 2026" -A x64 -DCMAKE_VS_GLOBALS=TrackFileAccess=false
cmake --build F:\sp_shadeprobe_build_final --config Release --parallel 2
```

产物：`F:\sp_shadeprobe_build_final\Release\ShadePilot.DriverProbe.exe`

说明：直接在本项目较长路径下配置时，Visual Studio FileTracker 返回 `FTK1011`；这属于构建路径/文件跟踪问题，不是 MSVC、IGCL ABI 或 Probe 源码错误。使用短构建目录并关闭 FileTracker 后编译成功。

## 三次只读实测

三次均满足：进程退出码 `0`、JSON 可解析、状态为 `Read-only probe completed`、IGCL 初始化成功，且三份报告去除时间戳后完全一致。

| 项目 | 三次结果 |
| --- | --- |
| IGCL 初始化 | `0x0`，成功 |
| IGCL 版本 | requested/supported `65537` |
| Adapter | `Intel(R) Arc(TM) B580 Graphics` |
| Vendor ID | `32902` |
| Device ID | `57867` |
| Driver Version | SDK 原始字段 `9007199261368992` |
| Display Output 枚举 | 25 个 handle；每个均读取了 Display Properties |
| Display Output 0 Sharpness Caps | `0x0`；FilterType `1`；范围 `1..5`；步长 `1` |
| Display Output 0 Current Sharpness | `0x0`；Enabled `false`；FilterType `1`；Intensity `0` |
| 其余输出 Sharpness | 原始返回码 `0x40000017`，未自行解释或伪造能力 |
| 3D Feature Caps | `0x0`；10 项 |
| Per-App / Live Change | 按当前 Header 的 `PerAppSupport` 和 `CTL_3D_FEATURE_MISC_FLAG_LIVE_CHANGE` 原样记录 |
| Set 调用 | 0；日志明确记录未调用任何 IGCL Set 函数 |

3D 能力中，实测返回的 `Live Change=true` 项包括 Frame Limit、Gaming Flip Modes、Low Latency、Frame Generation、Live State；其余项按原始字段记录。冲突字段和 `FeatureMiscSupport` 原始数值也已保留。

## 证据位置

- [Probe 源码](./ShadePilot.DriverProbe/main.cpp)
- [CMake 配置](./ShadePilot.DriverProbe/CMakeLists.txt)
- [官方 SDK 固定版本](./igcl-upstream)
- [三次 JSON/日志结果](./ShadePilot.DriverProbe/test-results/phase1-2026-08-26/)

## 严格停止点

Phase 1 的初始化、B580 确认、Display/Sharpness/3D 读取和三次稳定性验收均已通过。其后仅完成了独立的 Phase 2“抽象接口 + Intel Sharpness Read”，详见 `../PHASE2_STATUS.md`；Sharpness Write、Backup/Restore、UI、Profile 和自动应用仍未开始，并继续保持默认关闭、可失败和不安装驱动/不修改注册表的边界。
