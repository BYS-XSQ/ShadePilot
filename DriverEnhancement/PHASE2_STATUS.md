# ShadePilot Driver Enhancement — Phase 2 Status

状态：**抽象接口 + Intel Sharpness Read 已完成；停在写入前**

## 本阶段范围

已完成：

- `IDriverEnhancementProvider` 只读接口
- `DriverEnhancementReadResult`、Adapter、Display、Sharpness 和 3D 能力模型
- `IntelSharpnessReadProvider`
- 独立 `ReadTest` 验证程序

Provider 不直接修改 WPF、Gamma、DDC/CI 或配置系统，也没有加入任何正式 UI。它只复制已编译的官方-IGCL Probe 到临时目录，运行后读取 JSON，再删除临时目录；Probe 仍然只调用 IGCL 初始化和 Get 接口。

## 编译

```text
dotnet build DriverEnhancement\Managed\ReadTest\ReadTest.csproj -c Release --nologo
```

结果：成功，0 个警告，0 个错误。

生成：

- `DriverEnhancement/Managed/bin/Release/net8.0/ShadePilot.DriverEnhancement.dll`
- `DriverEnhancement/Managed/ReadTest/bin/Release/net8.0/ReadTest.dll`

## 真实机器实测

使用已通过 Phase 1 验收的 Probe：

```text
F:\sp_shadeprobe_build_final\Release\ShadePilot.DriverProbe.exe
```

连续调用 Provider 三次，三次均返回退出码 `0`：

```text
Succeeded=True
Supported=True
Status=Read-only probe completed
Adapter=Intel(R) Arc(TM) B580 Graphics;Vendor=32902;Device=57867;Displays=25
SharpnessDisplay=0;Caps=0x0;Current=0x0;RangeCount=1;CurrentIntensity=0
```

这确认托管抽象层能在真实 B580 上读取官方 Probe 结果，而不是只通过编译检查。

## 严格停止点

本阶段没有实现：

- `ctlSetCurrentSharpness`
- 备份/恢复
- WPF UI
- 游戏 Profile 或自动应用
- AMD/NVIDIA 接口

下一步若继续，只能进入计划中的 Sharpness Write，并且必须先设计回读确认、失败保护和退出恢复；在此之前不向驱动写入任何值。
