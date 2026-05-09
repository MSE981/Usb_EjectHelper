# USB Eject Helper

Windows 系统托盘小工具：**当 Windows 自带的"安全删除硬件"弹"此设备正在使用中"时，能告诉你具体是哪个进程、哪个文件在占用 U 盘**，并提供一键弹出 / 二次扫描 / JSON 导出。

> 中文界面 · .NET 8 · WinForms · 仅限 Windows 10/11

```text
[Tray]   USB Eject Helper
         ├── 显示主窗口
         ├── 刷新设备列表
         ├── 设置
         └── 退出

[主窗口]  可移动设备：
         | 盘符 | 卷标   | 文件系统 | 容量      |
         | E:   | KINGSTON | NTFS  | 14.6 GB   |
         [弹出] [扫描占用] [刷新] [导出 JSON] [设置] [关于] [隐藏到托盘]

         占用结果：
         | PID  | 进程名      | 进程路径              | 占用路径         | 检测方法 |
         | 9032 | notepad.exe | C:\Windows\notepad.exe| E:\readme.txt    | NT Handle Scan |
```

---

## 1. 这是什么 / 不是什么

### 是什么
- 解决日常 U 盘 / 移动硬盘弹出失败的诊断工具。把"谁在占、占什么"从黑盒变成可操作的清单。
- 全用户态实现，不装驱动、不装服务、不要求管理员权限即可运行（普通模式即够用）。
- 单文件 .NET 8 WinForms 程序，启动 ~58 MB 内存，CPU 闲时近 0%。

### 不是什么
- **不**保证 100% 弹出。系统服务 / 防病毒 / 索引器 / 写缓存等场景仍可能需要人工处理。
- **不**默认强制拔盘 —— 强制卸载有数据损坏风险，本工具坚持"先诊断、后释放"原则。
- **不**自动结束进程 —— 杀进程意味着用户未保存的数据丢失，第一版只列出占用源、不主动 kill。
- 不是 Sysinternals `handle.exe` 的替代品。它走的是同一条 NT 句柄枚举路径，但只针对 USB 卷做了体验包装。

---

## 2. 谁需要它

| 你是… | 它能帮到你 |
|------|----------|
| 普通用户 | 弹出失败时点一下"扫描占用"，看到具体的进程 / 文件，关掉它，再点"弹出"。不用重启。 |
| 开发者 / 运维 | 可视化卷句柄占用；导出 JSON 用于分析；学习 Windows 设备管理 / 句柄枚举 / Restart Manager / NT API 的实现路径。 |
| 学习 .NET P/Invoke | 一个有完整工程结构的 P/Invoke 例子：`cfgmgr32` / `setupapi` / `rstrtmgr` / `kernel32` / `ntdll` 互相协作。 |

---

## 3. 快速开始

### 3.1 前置

| 项 | 要求 |
|---|---|
| 操作系统 | Windows 10 / 11（仅 x64） |
| 运行时 | .NET 8 Desktop Runtime（开发者用 .NET 8 SDK） |
| 终端 | PowerShell 5.1+ 或 7+；脚本已自带 UTF-8 控制台编码 |

[下载 .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 3.2 一行命令跑起来（自构建 + 启动）

```powershell
.\run.ps1
```

或用 `cmd`：

```cmd
run.bat
```

启动后请观察任务栏右下角的托盘图标。主窗口默认最小化到托盘（可在"设置"里改）。

### 3.3 其他常用命令

```powershell
.\run.ps1 -Build              # 只构建 Debug，不启动
.\run.ps1 -Release            # Release 配置 + 启动
.\run.ps1 -Test               # 跑全部 xunit 单元测试
.\run.ps1 -Clean -Build       # 干净构建
.\run.ps1 -Pretty             # 控制台日志带颜色
```

或者直接用 `dotnet`：

```powershell
dotnet build  UsbEjectHelper\UsbEjectHelper.sln -c Release
dotnet test   UsbEjectHelper\UsbEjectHelper.sln -c Release --nologo
dotnet run    --project UsbEjectHelper\src\UsbEjectHelper\UsbEjectHelper.csproj
```

---

## 4. 功能与使用

### 4.1 主要功能

- **可移动设备列表** —— 自动识别 U 盘 / USB 移动硬盘 / 读卡器。固定盘和系统盘永远不会出现在列表里。
- **一键弹出** —— 走 `cfgmgr32!CM_Request_Device_Eject`，失败时区分两类原因：普通"设备繁忙"和"被否决（PNP_VETO_TYPE）"，否决会带上具体类别（程序占用 / 卷被挂载 / 同 PnP 总线下其他设备未停 / 等）。
- **占用扫描** —— 两种模式（详见下文 §5）：
  - **安全模式（默认）** —— 只用 Restart Manager 注册资源，能识别向 RM 注册过的应用（资源管理器、Office 等）。
  - **深度模式（用户主动开启）** —— NT 系统级句柄枚举，能找到任何持有 U 盘文件 / 目录的进程（包括 notepad.exe、图片查看器、cmd.exe 这种不向 RM 注册的）。
- **导出 JSON** —— 设备列表 / 扫描结果都可以导出，支持隐私脱敏。
- **设备热插拔自动刷新** —— `WM_DEVICECHANGE` + WMI 双重监听。
- **开机自启动** —— 写到 `HKCU\...\Run`，用户级，不要管理员权限。
- **托盘常驻 + 单实例** —— 用 `Mutex` + `NamedPipeServerStream`：第二次启动会 IPC 通知现有实例显示主窗口然后静默退出。
- **关于对话框** —— 满足 GPL v3 §5(d) "Appropriate Legal Notices" 建议；显示版本 / 版权 / warranty 声明 / 跳转 LICENSE。
- **弹出失败的恢复手段（阶段 2）** —— 弹出失败时弹三选一对话框：
  - **① 扫描占用进程** —— 现有功能（看是哪个程序在占用）。
  - **② 关闭占用进程** —— 用户勾选要关哪些进程 + 选优雅关闭 (WM_CLOSE) / 强制结束 (TerminateProcess) → 完成后**只回主窗口**（不自动重试弹出，让用户先处理保存对话框）。
  - **🔴 ③ 强制弹出** —— `FSCTL_LOCK_VOLUME` + `FSCTL_DISMOUNT_VOLUME` + `IOCTL_STORAGE_EJECT_MEDIA`，让所有持该卷句柄的进程立即得 invalid handle；红色按钮 + 2s 倒计时确认对话框。
- **进程关闭的多重护栏** —— 系统关键进程（`System`/`csrss`/`lsass`/`svchost` 等 13 项）永远拒绝；High 等级进程（`explorer`、Defender、杀软、数据库守护等）强制结束需要**打字精确匹配进程名**才能确认；Normal 进程需要勾选"我已了解风险"+ 确认。
- **占用结果右键菜单** —— "在资源管理器中定位 / 优雅关闭 / 强制结束 / 复制路径"，按 RiskTier 与设置闸门动态启用。
- **动作审计日志** —— 关闭进程 / 强制弹出的每一次动作都写到 `%LOCALAPPDATA%\UsbEjectHelper\actions.log`（JSON Lines，1MB 滚动 × 5 份），含用户同意方式（`type-match-force` / `force-eject-2s-confirm` / `declined` 等），方便企业 EDR 反查与故障复盘。

### 4.2 典型场景

| 场景 | 操作 |
|---|---|
| 弹出成功 | 选中盘 → "弹出" → 提示"可以安全移除" |
| 资源管理器打开了 U 盘 | "弹出"会失败 → 三选一对话框 → 选 ① 扫描 → 列表显示 `explorer.exe` → 关掉资源管理器 → 再点"弹出" |
| notepad / 图片查看器打开了 U 盘文件 | 默认安全模式扫描会**找不到**（这些应用不向 RM 注册）→ 进入"设置"勾选"启用深度占用扫描"并确认 → 重新扫描 → 能看到 `notepad.exe` |
| 想直接在程序内关掉占用 | 设置勾选"允许在程序内结束占用进程" → 弹出失败时选 ② → 子对话框勾选 + 优雅关闭 → notepad 弹保存对话框 → 用户处理后再点弹出 |
| 紧急强制弹出 | 设置勾选"允许强制弹出" → 弹出失败时选 ③ → 2s 倒计时风险确认 → 确认 → 卷立即卸载（持有句柄的应用会得 invalid handle） |
| 想长期常驻 | "设置" → 勾选"开机自启动" + "启动后最小化到托盘" |
| 想保留弹出诊断 | 扫描后"导出 JSON"，文件包含设备 / 占用 / 检测方法；进程关闭 / 强制弹出动作写到 `actions.log` |

---

## 5. 安全 / 深度两种扫描模式

这是本项目的核心权衡，必须看清楚再决定要不要开。

| 维度 | 安全模式（默认） | 深度模式（需明确启用） |
|------|---------------|--------------------|
| 实现机制 | `rstrtmgr!RmStartSession + RmRegisterResources + RmGetList` | `ntdll!NtQuerySystemInformation(SystemExtendedHandleInformation)` + `kernel32!OpenProcess + DuplicateHandle + GetFinalPathNameByHandle` |
| 能发现谁 | 仅向 RM 注册过自己依赖的应用（资源管理器、Office、部分编辑器） | 任何对 U 盘上文件 / 目录持有句柄的同用户进程（notepad、图片查看器、cmd、自己的脚本…） |
| 信息披露面 | 无 | 读全系统句柄表元数据（PID、句柄值、对象类型、访问掩码、对象内核指针）；对同用户进程做 DuplicateHandle |
| 是否绕过 ACL | 否 | 否 —— 受 Windows 用户态访问控制约束。`OpenProcess` 拿不到的进程（SYSTEM、其他用户、高完整性）直接放弃 |
| 用到内核驱动 / 提权 | 否 | 否 —— 不调 `SeDebugPrivilege`、不装 driver |
| EDR / 杀软误报风险 | 几乎无 | 中等 —— "全量句柄枚举 + 跨进程 DuplicateHandle" 是 Sysinternals `handle.exe` 同款特征，部分杀软引擎可能启发式打分 |
| `summary.Method` 字段值 | `Restart Manager (Safe Mode)` | `NT Handle Scan` |
| 默认状态 | 启用 | 关闭 |
| 启用方式 | — | 设置 → "启用深度占用扫描" → 阅读二次确认对话框 → 同意 |

技术细节、稳定性保护（150ms 超时、`GetFileType` 预过滤、专用后台 Thread 防 ThreadPool 饿死）见 [`UsbEjectHelper/src/UsbEjectHelper/Core/HandleScanner.cs`](UsbEjectHelper/src/UsbEjectHelper/Core/HandleScanner.cs)。

---

## 6. 项目结构

```text
AI_study/                                     ← Git 仓库根（混合内容：本项目 + 学习 PDF / Cursor skills）
├── README.md                                 ← 本文件
├── LICENSE                                   ← GPL v3 全文
├── run.ps1 / run.bat                         ← 构建 + 测试 + 启动入口
├── doc/
│   ├── plan.md                               ← 技术规划与里程碑
│   ├── manual-test-plan.md                   ← 必须人工 / 真实硬件参与的测试清单（附录 A 列出已被自动化覆盖的部分）
│   └── phase1-development-test-plan.md       ← 阶段 1 测试方法论
├── tools/
│   └── auto-tests/                           ← 端到端验证脚本
└── UsbEjectHelper/                           ← 主项目（GPL v3 范围）
    ├── UsbEjectHelper.sln
    ├── src/UsbEjectHelper/
    │   ├── App/                              ← 进程入口、装配根、托盘生命周期
    │   │   ├── Program.cs                    ← STA、单实例 Mutex、IPC、全局异常兜底
    │   │   ├── ServiceComposer.cs            ← 唯一一处 new 服务的地方（手写依赖注入）
    │   │   ├── TrayApplication.cs            ← NotifyIcon、ContextMenu、IPC 服务端、设备通知窗口生命周期
    │   │   └── AppConstants.cs
    │   ├── Core/                             ← 业务核心，与 UI / 平台 API 解耦
    │   │   ├── Abstractions/                 ← 接口（IEjectService、IHandleScanner、…）
    │   │   ├── DeviceWatcher.cs              ← 设备增删事件、卷信息聚合、debounce
    │   │   ├── DeviceChangeParser.cs         ← WM_DEVICECHANGE 消息解析
    │   │   ├── VolumeResolver.cs             ← 盘符 ↔ 卷 GUID ↔ \Device\HarddiskVolumeN 互转
    │   │   ├── EjectService.cs               ← cfgmgr32 弹出 + Veto 翻译
    │   │   ├── HandleScanner.cs              ← 安全 / 深度两条扫描路径
    │   │   ├── ProcessInspector.cs           ← PID 元数据、危险进程白名单
    │   │   ├── ExportService.cs              ← 设备 / 扫描结果 → JSON / 文本，含隐私脱敏
    │   │   └── WmiQueryService.cs
    │   ├── Native/                           ← P/Invoke 声明（按 dll 分文件）
    │   │   ├── NativeMethods.CfgMgr32.cs     ← CM_* 设备管理
    │   │   ├── NativeMethods.SetupApi.cs     ← SetupDi*、设备实例 ID
    │   │   ├── NativeMethods.Rstrtmgr.cs     ← Restart Manager
    │   │   ├── NativeMethods.NtDll.cs        ← NtQuerySystemInformation、句柄表
    │   │   ├── NativeMethods.Kernel32.cs     ← OpenProcess、DuplicateHandle、GetFinalPathNameByHandle
    │   │   └── NativeMethods.Ioctl.cs
    │   ├── Settings/
    │   │   ├── AppSettings.cs                ← JSON 持久化到 %LOCALAPPDATA%\UsbEjectHelper\settings.json
    │   │   └── StartupManager.cs             ← HKCU\...\Run 增删
    │   └── UI/
    │       ├── MainWindow.cs                 ← 主窗口（设备列表 / 占用结果 / 操作按钮）
    │       ├── SettingsForm.cs               ← 模态设置对话框（含深度扫描二次确认）
    │       ├── DeviceNotificationWindow.cs   ← 隐藏顶层窗口（接收 WM_DEVICECHANGE 广播）
    │       └── AboutDialog.cs                ← 关于（GPL §5(d) 法律声明入口）
    └── tests/UsbEjectHelper.Tests/           ← 89 个 xunit 单测，串行执行
        └── xunit.runner.json                 ← parallelizeTestCollections=false（防句柄扫描跨测试相互饿死）
```

---

## 7. 架构关键决策

1. **手写 DI** —— `ServiceComposer.Build()` 是唯一构造服务的地方。没有引入 `Microsoft.Extensions.DependencyInjection`，第一版的依赖图小到不需要容器。
2. **接口而非具体类** —— `IEjectService` / `IHandleScanner` / `IExportService` / `IProcessInspector` / `IVolumeResolver`。绝大部分单测靠 `Moq` 替换实现。
3. **`InternalsVisibleTo`** —— 测试程序集能拿到 `internal` 类型，避免为测试把内部 API 升为 `public`。
4. **设备通知不进 `MainWindow`** —— 单独的 `DeviceNotificationWindow` 长期持有，主窗口可以随用户关掉。这条修复了"主窗口隐藏后热插拔不刷新"的 bug。
5. **NT 句柄扫描的稳定性三层防护**：
   - 直接 `Marshal.ReadInt32` / `ReadIntPtr` 读结构字段，不用 `PtrToStructure`（15 万句柄能省 5 秒）。
   - `IsHangProneAccess` 过滤已知会卡死 `GetFinalPathNameByHandle` 的访问掩码（如 `0x0012019F`，命名管道常见）。
   - `ResolveWithTimeout` 用专用 `System.Threading.Thread`（不是 `Task.Run` 占用 ThreadPool worker），150ms 超时被命中也只是浪费一个线程，不会饿死整个进程。
6. **测试串行化** —— `xunit.runner.json` 设 `parallelizeTestCollections=false`。NT 扫描类测试一旦并行，testhost 进程内的句柄表会互相污染。
7. **安全默认** —— `EnableDeepHandleScan` 默认 `false`。要启用必须经过设置 → 二次确认对话框（描述清楚做什么、披露什么）→ 同意。这是对"是否越权"问题的工程回答。

---

## 8. 测试

```powershell
.\run.ps1 -Test
```

- ~150 个 xunit 测试，约 5~10 秒完成（含子 cmd 集成）。
- 已自动化覆盖：编译、单元逻辑、进程冷启动、单实例 + IPC、命名管道生命周期、`AppSettings` JSON round-trip（含阶段 2 的 7 个新字段）、`HKCU\Run` 真实写入再清理、JSON 导出反向解析、隐私脱敏、`ProcessTerminator`（Critical 拒绝 / 闸门拒绝 / 幂等 / 集成杀子 cmd）、`ForceEjectService`（无效盘 / 系统盘拒绝 / 盘符规范化）、`ActionAuditLog`（JSON Lines / 滚动 / 脱敏 / 写失败不抛）、`run.ps1` 全开关组合等。完整列表见 `doc/manual-test-plan.md` 附录 A。
- **必须人工确认**：托盘视觉、热插拔真硬件、Veto 真实分支、阶段 2 K-1..K-14 的真实进程关闭 / 强制弹出流程、长稳定性 10 分钟监控。详见 `doc/manual-test-plan.md`。

---

## 9. 配置 / 隐私

| 项 | 默认 | 位置 |
|---|---|---|
| 配置文件 | — | `%LOCALAPPDATA%\UsbEjectHelper\settings.json` |
| 审计日志 | — | `%LOCALAPPDATA%\UsbEjectHelper\actions.log`（1MB 滚动 × 5 份） |
| 开机自启动 | 关 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\UsbEjectHelper` |
| 启动后最小化到托盘 | 开 | settings.json |
| 关闭窗口时最小化到托盘 | 开 | settings.json |
| 隐私脱敏（导出时） | 关 | settings.json |
| 启用深度占用扫描 | 关 | settings.json（要先经过设置里的二次确认对话框） |
| 允许在程序内结束占用进程 | 关 | settings.json（首次开启走二次确认；启用 ② 关闭进程入口） |
| 启用"强制结束"选项 | 关 | settings.json（启用 TerminateProcess UI 入口） |
| 允许"强制弹出" | 关 | settings.json（首次开启走二次确认；启用 🔴 ③ 强制弹出入口） |
| 优雅关闭超时（秒） | 5 | settings.json（WM_CLOSE 后等进程退出的时长，1~30） |
| 启用动作审计日志 | 开 | settings.json（关闭后 actions.log 不再增长，但已有日志保留） |

卸载干净：退出程序 → 删除 `%LOCALAPPDATA%\UsbEjectHelper\`（含 settings.json + actions.log）→ `regedit` 删除 `HKCU\...\Run\UsbEjectHelper`（如果开过自启动）→ 删除 exe 所在目录。无服务、无驱动、无系统级残留。

---

## 10. 路线图

详见 [`doc/plan.md`](doc/plan.md)。简版：

- **MVP（已完成）** —— 托盘常驻、设备识别、安全弹出、双模式占用扫描、导出、自启动、单实例、关于对话框 + GPL v3。
- **阶段 2（已完成）** —— 弹出失败三选一对话框、四层进程关闭模型（揭示 / WM_CLOSE / RM / TerminateProcess）、强制弹出（卷级 dismount + 2s 倒计时）、关键进程双重护栏、动作审计日志、占用列表右键菜单。详见 [`doc/phase2-development-plan.md`](doc/phase2-development-plan.md)。
- **v0.3（待规划）** —— 管理员模式（UAC 提权后扫描跨完整性句柄）、通知中心、典型场景 FAQ、可选代码签名（见 §11）。

---

## 11. 代码签名（暂未集成）

本仓库目前**不签名**。Windows 用户首次运行会被 SmartScreen 提示"未知发布者"。三档可选方案（按代价递增）：

1. **自签名** —— 免费，仅自己机器信任，适合开发 / 内部测试。
2. **Sectigo / SSL.com 个人 IV 证书** —— ~$120/年，要个人身份验证；分发后 SmartScreen 信誉需积累。
3. **Azure Trusted Signing** —— $10/月起，2024 年微软推出，云端 HSM；要求公司主体注册 ≥ 3 年。

签名脚本（`tools/sign.ps1`）暂未落地，等用户决定走哪一档证书后再补。

---

## 12. 贡献

这是一个个人维护的小工具，但欢迎 Issue / PR：

- 代码风格：follow `.editorconfig`（如有；当前项目以 `dotnet format` 默认即可）。
- 提交信息：能让人看出"做了什么、为什么"即可。
- 任何会动 P/Invoke / 句柄扫描的 PR，请同时跑 `.\run.ps1 -Test` 并贴结果。
- 涉及高风险操作（结束进程、关闭句柄等）的 PR，必须经过显式确认对话框 + 关键进程保护清单审查。

---

## 13. License

**GPL v3 or later** — 全文见 [`LICENSE`](LICENSE)。

```text
Copyright (C) 2026  Jin Bohan

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
```

> 仓库根的 PDF 学习资料（`Professional-C++…pdf`、`C# 12 in a Nutshell.pdf` 等）属于第三方著作，**不受本项目 GPL v3 覆盖**，遵循其各自原始版权与许可。本项目的 GPL v3 仅适用于 `UsbEjectHelper/` 目录及其下源代码、构建脚本和文档。
