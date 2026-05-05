# 第一阶段开发与调试测试计划

## 当前判断

仓库目前只看到主计划文档 [doc/plan.md](plan.md)，未发现现有 `.sln`、`.csproj` 或 README。因此第一阶段应从工程骨架开始，按计划文档推荐结构创建 .NET 8 WinForms 程序与测试项目。

第一阶段范围锁定为：系统托盘常驻、用户级开机自启动设置、可移动盘枚举与设备变化监听、一键安全弹出、弹出失败后的只读占用扫描、占用结果展示与导出。明确不做：关闭句柄、结束进程、强制卸载、驱动或服务。

## 开发计划

1. 建立工程骨架

- 创建解决方案 [UsbEjectHelper.sln](../UsbEjectHelper.sln)。
- 创建主程序 [src/UsbEjectHelper/UsbEjectHelper.csproj](../src/UsbEjectHelper/UsbEjectHelper.csproj)，目标 `net8.0-windows`，启用 WinForms。
- 创建测试项目 [tests/UsbEjectHelper.Tests/UsbEjectHelper.Tests.csproj](../tests/UsbEjectHelper.Tests/UsbEjectHelper.Tests.csproj)。
- 先采用单进程桌面程序，不引入服务、驱动或 native helper。

2. 搭建应用入口与托盘生命周期

- [src/UsbEjectHelper/App/Program.cs](../src/UsbEjectHelper/App/Program.cs)：实现 STA 入口、单实例互斥、异常兜底日志。
- 单实例互斥建议使用稳定命名，如 `Global\UsbEjectHelper-{应用固定 GUID}` 或当前用户级 `Local\UsbEjectHelper-{应用固定 GUID}`；MVP 优先用当前用户级，避免跨用户会话互相影响。
- 第二个实例启动时不继续运行：优先通过命名管道、窗口消息或其他轻量 IPC 通知已有实例显示主窗口并前置；若 IPC 失败，则静默退出并写日志。
- [src/UsbEjectHelper/App/TrayApplication.cs](../src/UsbEjectHelper/App/TrayApplication.cs)：托盘图标、右键菜单、显示主窗口、刷新设备、退出。
- [src/UsbEjectHelper/UI/MainWindow.cs](../src/UsbEjectHelper/UI/MainWindow.cs)：先做简单 WinForms 窗口，包含设备列表、操作按钮、占用结果表格、状态栏。
- 主窗口关闭行为必须可配置：用户可选择点击关闭按钮时退出程序，或最小化到托盘继续常驻；默认建议最小化到托盘，但首次关闭时应给出说明和设置入口。
- 托盘常驻时主进程应保持低占用：不进行持续占用扫描，不高频轮询设备，不后台枚举全系统句柄。

3. 实现设备枚举与刷新

- [src/UsbEjectHelper/Core/DeviceWatcher.cs](../src/UsbEjectHelper/Core/DeviceWatcher.cs)：启动时枚举盘符，并通过 `WM_DEVICECHANGE` 和 WMI 监听插入/移除事件。
- 设备模型建议包含：盘符、卷标、容量、剩余空间、文件系统、DriveType、卷 GUID、设备路径、是否可弹出。
- 第一版判断策略不能只依赖 `DriveType.Removable`。Windows 经常把大容量 U 盘、USB SSD、移动硬盘报告为 `DriveType.Fixed`，因此 MVP 必须同时用 WMI 做 USB 关联。
- WMI 关联路径建议：`Win32_DiskDrive` 中筛选 `InterfaceType = "USB"` 或 PNP 信息包含 USB，再经 `Win32_DiskDriveToDiskPartition` 与 `Win32_LogicalDiskToPartition` 映射到盘符。
- 设备列表默认展示 USB 关联得到的可移动存储盘符，包括 `DriveType.Removable` 和 USB 介质上的 `DriveType.Fixed`；排除系统盘和非 USB 固定硬盘。
- WinForms 中处理 `WM_DEVICECHANGE` 时，需要在窗口 `WndProc` 中识别 `DBT_DEVICEARRIVAL`、`DBT_DEVICEREMOVECOMPLETE`、`DBT_DEVNODES_CHANGED`。
- 对 `DBT_DEVTYP_VOLUME` 事件解析 `DEV_BROADCAST_VOLUME` 结构中的 unit mask，转换为盘符后触发刷新；对无法解析或非 volume 事件只做防抖全量刷新。
- 每次插入/移除事件触发后做防抖刷新，避免系统连续事件造成 UI 抖动。

4. 实现卷路径解析

- [src/UsbEjectHelper/Core/VolumeResolver.cs](../src/UsbEjectHelper/Core/VolumeResolver.cs)：封装 `GetVolumeNameForVolumeMountPoint`、`QueryDosDevice` 等能力。
- 将 `E:\`、`\\.\E:`、`\\?\Volume{guid}\`、`\Device\HarddiskVolumeN` 建立映射。
- 为后续句柄路径匹配提供规范化方法：输入任意路径或 NT 设备路径，输出尽量用户可读的盘符路径。

5. 实现安全弹出尝试

- [src/UsbEjectHelper/Core/EjectService.cs](../src/UsbEjectHelper/Core/EjectService.cs)：提供 `TryEjectAsync(device)`。
- MVP 可先走 Windows Shell/设备 API 的保守调用路径，失败时返回结构化原因：权限不足、设备忙、设备不存在、API 调用失败、未知错误。
- 不做强制卸载；失败后自动建议执行“扫描占用”。
- UI 上成功提示“可以安全拔出”，失败提示“设备仍被占用/无法弹出”及下一步。

6. 实现只读占用扫描

- [src/UsbEjectHelper/Core/HandleScanner.cs](../src/UsbEjectHelper/Core/HandleScanner.cs)：扫描目标卷相关占用。
- 第一轮可采用两级策略：先用 Restart Manager 查询占用目标盘路径的进程，作为稳定 MVP；再预留 `NtQuerySystemInformation` 深度句柄枚举接口。
- 占用扫描必须按需触发：仅在用户打开主窗口后点击“扫描占用”、点击“刷新占用状态”，或弹出失败后由用户确认扫描时执行；托盘空闲状态不主动扫描，以减少对其他应用和系统 I/O 的影响。
- UI 必须明确说明 Restart Manager 的局限：它主要发现参与 RM 资源管理或可由 RM 识别的文件占用，不保证覆盖所有便携软件、脚本解释器、后台服务、命令行当前目录、杀毒扫描和不可枚举句柄。
- 当扫描为空但弹出失败时，不显示“无占用”这种绝对结论，应显示“当前扫描方法未发现占用”，并建议关闭资源管理器窗口、保存并关闭相关程序、切换终端当前目录、以管理员模式重扫或稍后重试。
- 普通权限下只展示能获取到的信息；遇到访问拒绝、进程退出、路径不可解析时记录为可解释状态，不崩溃。
- 扫描结果模型包含：PID、进程名、可执行路径、命令行、占用路径、来源方法、错误/权限状态。

7. 实现进程信息与风险过滤

- [src/UsbEjectHelper/Core/ProcessInspector.cs](../src/UsbEjectHelper/Core/ProcessInspector.cs)：查询进程名、主模块路径、命令行。
- 对 `System`、`csrss.exe`、`wininit.exe`、`services.exe` 等关键进程打标。
- 第一阶段只展示风险提示，不提供结束进程按钮。

8. 实现设置与开机自启动

- [src/UsbEjectHelper/Settings/AppSettings.cs](../src/UsbEjectHelper/Settings/AppSettings.cs)：保存用户设置，如开机自启动、启动后是否最小化到托盘、关闭窗口时退出或最小化到托盘、日志脱敏偏好。
- [src/UsbEjectHelper/Settings/StartupManager.cs](../src/UsbEjectHelper/Settings/StartupManager.cs)：使用当前用户级 `Run` 注册表项开启/关闭自启动。
- 默认不开启自启动；设置页必须能关闭并校验当前状态。
- 设置页应明确展示后台行为：托盘常驻只监听设备变化和等待用户操作，不在后台持续扫描占用。

9. 实现导出与日志

- 日志记录程序启动、设备刷新、弹出请求、扫描请求、权限失败和 API 错误。
- UI 提供导出 JSON/文本，导出前提示可能包含用户名、文件路径等隐私信息。
- MVP 可先实现 JSON 导出，文本导出作为同一数据模型的格式化输出。

## 推荐实现顺序

1. 工程骨架和主窗口可运行。
2. 托盘常驻、刷新设备列表、退出流程。
3. 可移动盘枚举、USB Fixed 盘 WMI 关联和设备变化监听。
4. 卷路径解析和设备详情展示。
5. 安全弹出接口与失败状态展示。
6. Restart Manager 占用扫描 MVP。
7. 进程信息补充、权限降级提示、导出。
8. 单元测试、手工测试脚本、README 使用说明。

## 调试计划

- 每个核心服务先设计为可单独实例化的类，UI 只调用接口，避免调试 WinForms 时所有问题混在一起。
- 为 `VolumeResolver`、设备过滤、路径规范化、导出格式建立单元测试。
- 对 Windows API 调用统一封装返回值和 `Marshal.GetLastWin32Error()`，日志中保留错误码与上下文。
- 对设备变化监听增加调试日志：`WM_DEVICECHANGE` 事件类型、`DEV_BROADCAST_VOLUME` 解析结果、WMI 查询结果、刷新前设备数、刷新后设备数、被过滤设备原因。
- 对占用扫描增加分阶段日志：开始扫描、匹配卷路径、查询进程信息、不可访问原因、耗时。
- 对 Restart Manager 扫描结果增加方法标记和覆盖说明；扫描为空时记录弹出失败码、目标卷、是否已尝试管理员权限，便于排查“扫描为空但无法弹出”的场景。
- 扫描逻辑设置超时与取消令牌，避免某个句柄或进程查询阻塞 UI。
- 调试空闲资源占用：托盘常驻且主窗口关闭时，确认 CPU 长期接近 0、无持续磁盘 I/O、无高频 WMI 查询；设备变化事件只触发防抖刷新，不触发占用扫描。
- UI 调试时先用手动“刷新/扫描”按钮验证，再接入自动插入/移除事件。
- 普通权限和管理员权限分别运行，记录差异，确保普通权限失败时是降级提示而不是异常退出。

## 测试计划

单元测试：

- `VolumeResolver`：盘符标准化、尾部斜杠处理、大小写归一、NT 设备路径映射。
- `DeviceWatcher` 的过滤函数：排除系统盘和非 USB 固定盘，保留 `DriveType.Removable` 与 WMI 关联到 USB 物理磁盘的 `DriveType.Fixed`。
- WMI 查询通过接口抽象测试，例如定义 `IWmiQueryService` 返回磁盘、分区、逻辑盘 DTO；单元测试使用内存假数据覆盖 USB 移动硬盘、普通 U 盘、系统盘、内置固定硬盘、读卡器空盘等情况。
- `WM_DEVICECHANGE` 解析逻辑拆成纯函数测试：输入 `DBT_DEVICEARRIVAL`、`DBT_DEVICEREMOVECOMPLETE` 和 volume unit mask，验证转换出的盘符集合与刷新策略。
- `StartupManager`：注册表路径和值生成逻辑可通过抽象注册表接口测试。
- `AppSettings`：关闭窗口行为、启动后最小化、日志脱敏等设置能正确保存和读取。
- 托盘生命周期：模拟主窗口关闭事件，分别验证“退出程序”和“最小化到托盘”两种行为。
- `ProcessInspector`：关键进程名单识别、命令行不可访问时的降级状态。
- 导出：JSON 字段完整、脱敏选项生效、空结果也能导出。

手工集成测试：

- 插入普通 U 盘，设备列表自动出现，显示盘符、卷标、容量、文件系统。
- 拔出 U 盘，设备列表自动移除，UI 不报错。
- 点击刷新，设备列表与系统状态一致。
- 在无占用情况下点击弹出，成功后提示可拔出。
- 资源管理器打开 U 盘目录，点击弹出应失败或提示设备忙，再扫描应定位到 `explorer.exe` 或给出合理占用说明。
- 记事本打开 U 盘上的 txt 文件，扫描应定位到 `notepad.exe` 和对应文件路径。
- PowerShell/CMD 当前目录切到 U 盘，扫描应尽量定位到对应终端进程。
- 使用便携软件或脚本解释器打开 U 盘文件，若 Restart Manager 未发现占用，UI 应明确显示“当前扫描方法未发现占用”及 RM 局限说明，而不是断言没有占用。
- 插入被 Windows 识别为 `DriveType.Fixed` 的 USB 移动硬盘或 USB SSD，设备列表应能通过 WMI 关联识别并展示。
- 插入内置固定硬盘分区或系统盘，不应出现在可弹出设备列表中。
- 拷贝大文件到 U 盘过程中尝试弹出，应失败并给出“可能存在后台 IO/占用”的提示，程序不崩溃。
- 使用非管理员运行，遇到不可访问进程时显示“权限不足，可用管理员模式补充信息”。
- 使用管理员运行，对同一场景扫描结果应不少于普通权限，并仍不提供危险操作。
- 开启自启动后重启或重新登录，程序能启动；关闭自启动后注册表项移除。
- 设置“关闭时最小化到托盘”后，点击窗口关闭按钮应隐藏主窗口且托盘仍存在；从托盘菜单可重新打开主窗口。
- 设置“关闭时退出程序”后，点击窗口关闭按钮应退出进程并移除托盘图标。
- 程序在托盘空闲 10 分钟，不应主动执行占用扫描；CPU、内存和磁盘 I/O 应保持低占用水平。
- 只有在用户打开主窗口并点击扫描，或弹出失败后确认扫描时，才启动占用扫描。
- 导出 JSON 后检查包含设备、扫描时间、进程信息、错误状态；脱敏模式不暴露完整用户路径。

验收测试：

- 满足 [doc/plan.md](plan.md) 中阶段 1 验收：插入 U 盘后自动出现；可点击弹出；在资源管理器打开文件和记事本打开 txt 的可控场景下，能稳定指向正确进程。
- 设备覆盖要求：MVP 不仅支持 `DriveType.Removable`，也支持 WMI 关联到 USB 物理磁盘的 `DriveType.Fixed` 移动硬盘。
- 诊断诚实性要求：Restart Manager 扫描为空时必须表达“未发现”而不是“无占用”，并展示扫描方法局限与下一步建议。
- 稳定性要求：连续插拔 5 次不崩溃；连续刷新/扫描 20 次不造成 UI 卡死；无设备时启动和退出正常。
- 低占用要求：托盘后台状态只监听设备变化和响应用户唤醒，不主动持续扫描占用；关闭行为可由用户设置为退出或最小化到托盘。
- 安全要求：第一阶段没有结束进程、关闭句柄、强制卸载入口；所有失败都以状态和建议展示。

## 任务清单

- `setup-solution`：创建 .NET 8 WinForms 解决方案、主项目和测试项目。
- `app-shell`：实现单实例入口、托盘生命周期和主窗口骨架。
- `device-detection`：实现可移动盘枚举、USB Fixed 盘 WMI 关联、设备变化监听和设备详情展示。
- `volume-resolution`：实现盘符、卷 GUID、NT 设备路径映射与路径规范化。
- `eject-service`：实现安全弹出尝试、失败分类和 UI 提示。
- `handle-scan-mvp`：实现 Restart Manager 优先的只读占用扫描 MVP。
- `settings-startup`：实现用户设置、关闭行为配置、托盘低占用约束和 HKCU 自启动开关。
- `export-logging`：实现日志、JSON 导出和隐私脱敏提示。
- `tests-debugging`：补齐单元测试、手工测试清单和验收测试记录。
