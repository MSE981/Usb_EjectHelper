# UsbEjectHelper 手动测试清单

> 日期：2026-05-08
> 自动化测试已覆盖项请见文末 **附录 A**。本文档只列出**真正需要人工或硬件参与**的项。

## 0. 前置准备

| 项 | 说明 |
| - | - |
| 操作系统 | Windows 10 / 11，常规账号即可（不强制管理员） |
| 测试用 U 盘 | 至少 1 个 USB 大容量存储设备，最好准备 2 个以验证多盘并发 |
| 终端 | PowerShell 5.1 或 7+；脚本已自带 UTF-8 控制台编码 |
| 启动方式 | `.\run.ps1` 或 `run.bat`（直接构建并运行 Debug） |
| 清理 | 测试结束后请验证：托盘图标消失、进程退出、`%LocalAppData%\UsbEjectHelper\settings.json` 不存在或符合预期 |

---

## 1. 系统托盘 / UI（视觉确认 — 不能被 xunit 覆盖）

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **U-1** | `.\run.ps1` 启动后，观察任务栏右下角 | 出现 `UsbEjectHelper` 托盘图标，主窗口默认最小化（受设置影响） |
| **U-2** | 单击托盘图标 | 弹出主窗口；再次单击或选 `隐藏` → 重新藏回托盘 |
| **U-3** | 右键托盘 → `设置` | 弹出 **独立的** `SettingsForm` 模态对话框（PR4 引入），不再是嵌入式面板 |
| **U-4** | `SettingsForm` 切换 `开机自启动` 开关 → 确定 → 用 `regedit` 查看 `HKCU\...\Run\UsbEjectHelper` | 写入 / 删除均符合开关状态 |
| **U-5** | 右键托盘 → `退出` | 进程立刻消失；命名管道 `\\.\pipe\UsbEjectHelper_Pipe_*` 消失 |

> 自动化已验证：进程能够启动、PID 持续存在、Mutex/Pipe 在退出后释放（见附录 A 项 5、6）。

---

## 2. 设备热插拔（必须真实 U 盘）

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **D-1** | 程序启动后，**插入** U 盘 | 主窗口列表立刻刷新，新增此盘符；状态栏短暂提示"检测到设备" |
| **D-2** | **拔出** U 盘 | 列表立刻减少；如 `主窗口隐藏`、`托盘常驻` 也仍能正确响应（PR4 解耦后由 `DeviceNotificationWindow` 独立监听） |
| **D-3** | 关闭主窗口（仍然托盘常驻），插拔 | `DeviceNotificationWindow` 仍持续工作，再次打开主窗口时列表是最新的 |
| **D-4** | 同时插入 2 个 U 盘 | 都被识别；分别可独立选中 |

> `DeviceWatcher` 的 WMI 枚举侧已被 `WmiMockTests` 自动覆盖；硬件级 `WM_DEVICECHANGE` 触发只能现场确认。

---

## 3. 安全弹出（必须真实 U 盘 + 真实占用进程）

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **E-1** | 选中 U 盘，点 `安全弹出`（无任何资源管理器窗口或文件占用） | 弹出成功；列表里盘符消失；Windows 托盘有"可以安全移除"提示 |
| **E-2** | 在资源管理器里打开该 U 盘根目录，再点 `安全弹出` | 提示**被占用**；扫描结果会列出 `explorer.exe` |
| **E-3** | 用 `notepad.exe` 打开该 U 盘上的一个 .txt 并保持，再点 `安全弹出` | 失败；扫描列表显示 `notepad.exe` + 文件路径 |
| **E-4** | 关闭 notepad，再点 `安全弹出` | 立刻成功 |
| **E-5** | (E-2 / E-3 失败时) 观察对话框标题 | PR5 之后会区分两种失败：普通 `设备繁忙` 与 `弹出被拒绝（DeviceBusyVetoed）`。后者还会带上 `PNP_VETO_TYPE` 详情 |

> `EjectService` 对错误参数（无效盘符）的兜底由 xunit 的 `EjectServiceTests` 覆盖；CM/SetupDi 真实路径只能用真硬件验证。

---

## 4. 占用进程扫描（必须真实占用）

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **S-1** | E-3 场景中，点 `扫描占用` | 列表至少包含 `notepad.exe`，并显示 PID、可执行路径、占用文件路径 |
| **S-2** | 同时让多个进程（资源管理器 + notepad）占用 | 全部被列出；不重复 |
| **S-3** | 把 `Settings.EnablePrivacyMode` 设为 `true`，重新扫描 → 导出 JSON | 用户名（如 `Alice`）等敏感字段被脱敏；**自动化已经覆盖脱敏逻辑** (`ExportServiceTests`)，但此处用真实数据再确认一次 |

> Restart Manager 的 P/Invoke 单元由 `HandleScannerTests` mock 覆盖；真实多进程并发占用只能现场跑。

---

## 5. 长时间运行 / 资源监控（人工持续观察）

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **L-1** | 程序运行 10 分钟，期间反复插拔 U 盘 5 次 | CPU 平均 < 1%，内存稳定（开启时 ~ 58 MB，无持续增长） |
| **L-2** | 在 `任务管理器 → 详细信息` 持续观察 `UsbEjectHelper.exe` | 句柄数、线程数稳定；无显著漏出 |
| **L-3** | `Get-Process UsbEjectHelper` 间隔 1 分钟看 `WS` (`Working Set`) | 无单调上升 |

> 自动化只能确认**冷启动**内存（57.9 MB），无法跨 10 分钟连续监控 → 必手动。

---

## K. 阶段 2：弹出失败的恢复手段（必须真实 U 盘 + 真实占用）

适用版本：实施 PR10（弹出失败三选一对话框 + 进程关闭 + 强制弹出）之后。

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **K-1** | notepad.exe 打开 U 盘文件 → 点弹出 → 三选一对话框出现 → 选 ②"关闭占用进程" → 子对话框默认勾选 + 优雅关闭 → 点开始 | notepad 弹"是否保存"对话框；用户点"不保存" → notepad 退出 → 主窗口提示"成功 1 / 共 1"；不会自动重试弹出 |
| **K-2** | 同 K-1，但用户在 notepad 保存对话框点"取消" | 5s 超时 → 主窗口结果摘要里显示"⏱ notepad.exe — 超时（应用可能弹了'是否保存'对话框）"；用户处理 notepad 后**手动**点弹出 → 弹出成功 |
| **K-3** | notepad 持文件 → 弹出 → 选 ② → 子对话框切到"强制结束"+ 启动 ConfirmTerminateDialog → 勾选"我已了解" → 确认 | notepad 立即消失 → 摘要"✓ notepad.exe — 已退出 (TerminateProcess)" |
| **K-4** | 资源管理器持有 U 盘根目录 → 弹出 → 选 ② → 在子对话框看到 explorer 行带浅红背景 + "⚠ High" 标记 → 切到"强制结束" → 触发 ConfirmTerminateDialog | 风险横幅红色明示"桌面会闪一下"；要求**打字精确匹配** `explorer.exe`，输入对了才能点确认 |
| **K-5** | 弹出 → 选 ③ "强制弹出" | ForceEjectConfirmDialog 出现：标题 / 横幅 / 风险列表都是红色；"确认 (2)"按钮灰禁用；1s 后变 "确认 (1)"；2s 后变红色"强制弹出"启用；点击 → U 盘从列表消失 |
| **K-6** | 同 K-5 但在 1.5s 时按 ESC | 立即取消，无任何动作；audit log **没有** force-eject 行（仅 Refused-NoConsent） |
| **K-7** | 检查 `%LOCALAPPDATA%\UsbEjectHelper\actions.log` | 每个动作一行 JSON；`{"action":"close-graceful",...}` / `{"action":"force-terminate",...}` / `{"action":"force-eject",...}` / `{"action":"reveal",...}`；`consent` 字段记录用户具体怎么同意的 |
| **K-8** | 启用 EnablePrivacyMode 后再做 K-1，看 actions.log | `exe` / `filePath` 字段已脱敏成 `E:\***.txt` 形式；进程名 / PID / drive 字段不脱敏 |
| **K-9** | 占用结果列表里**右键** notepad 行 | 上下文菜单：在资源管理器中定位 / 优雅关闭 / 强制结束 / 复制路径；选"在资源管理器中定位" → 弹出 explorer 高亮 notepad.exe |
| **K-10** | 占用结果列表右键 SYSTEM (PID 4) 或 csrss.exe | 优雅关闭 / 强制结束 都灰禁用；tooltip 显示"系统关键进程不可关闭" |
| **K-11** | 设置首次开启 `允许在程序内结束占用进程` | 弹二次确认对话框，详细说明放开了什么能力 + 不放开什么；点否 → 不保存 |
| **K-12** | 设置首次开启 `允许强制弹出` | 弹二次确认对话框，明确说明 dismount 影响 + 数据丢失风险 |
| **K-13** | 设置开关全打开 → 重启程序 → 检查 `settings.json` | `AllowProcessTermination`、`EnableForceTerminate`、`EnableForceEject`、`GracefulCloseTimeoutSeconds`、`EnableActionAuditLog` 等字段已落盘且 round-trip 正确 |
| **K-14** | 强制弹出对 C: / 固定盘（手工修改源代码或测试入口） | ForceEjectService 在 Validate 阶段拒绝；不会真的对系统盘执行 dismount |

> 阶段 2 自动化已覆盖：`ProcessTerminator` (Critical 拒绝 / 闸门拒绝 / 幂等 / 子 cmd 集成杀)，`ForceEjectService` (无效盘 / 系统盘拒绝 / 盘符规范化)，`ActionAuditLog` (写入 / JSON Lines / 滚动 / 脱敏 / 写失败不抛)，`AppSettings` (7 个新字段 round-trip + 默认值)，`ProcessInspector.GetRiskTier` (Critical / High / Normal 分类)。RM L3 / WM_CLOSE 真实 GUI 应用 / 强制弹出 USB 物理路径只能用真硬件验证。

---

## 6. 单实例真实场景

| 编号 | 步骤 | 预期 |
| -- | -- | -- |
| **I-1** | 程序运行中，**用资源管理器**双击 `UsbEjectHelper.exe` 第二次 | 第二次启动后**主窗口被前台拉起**（IPC `SHOW` 已送达），不会出现两个托盘图标 |
| **I-2** | 一边运行 Debug，一边再启动 Release | 两个版本互不干扰（`Mutex` 名包含同一 GUID，所以**应当**也只剩一个 — 这是预期行为） |

> 自动化已确认第二个实例 < 1.1 秒静默退出 + 主实例继续运行（附录 A 项 4），但"主窗口前台拉起"这一**视觉反馈**只能人工确认。

---

## 附录 A：已被本次自动化覆盖的内容（无需手动）

| 项 | 自动化方式 | 结果 |
| - | - | - |
| 1. Debug + Release 双构建零警告零错误 | `.\run.ps1 -Build` / `.\run.ps1 -Release -Build` | ✅ 0 warnings, 0 errors |
| 2. 全量 xunit (含 RM、WMI、设备、设置、导出、CM 弹出枚举值、`HandleScanner` mock) | `.\run.ps1 -Test` | ✅ **83/83 passed** |
| 3. 进程冷启动 + 优雅强杀 | PowerShell 控制 `Start-Process` / `Stop-Process` | ✅ 1 进程，~58 MB，强杀后 0 残留 |
| 4. 单实例 + IPC 静默退出 | 双发 `UsbEjectHelper.exe` | ✅ 第二个进程 1.0 秒退出，exit code = 0 |
| 5. 命名管道在运行时存在 / 退出后释放 | 列举 `\\.\pipe\` | ✅ 1 → 0 |
| 6. Mutex 释放（重启可成功） | 强杀后立刻再启动 | ✅ 重启成功 |
| 7. AppSettings JSON 真实文件 round-trip | `SettingsTests.AppSettings_SaveAndLoad_ShouldRoundTrip` | ✅ |
| 8. **HKCU\Run** 真实写入 + 读回 + 删除 | 新增 `StartupManager_EnableThenDisable_RealRegistry_RoundTrips` (用 GUID 唯一值名避免污染用户) | ✅ 注册表无残留 |
| 9. `ExportService` JSON 真实落盘 + `JsonDocument.Parse` 反向校验 | 新增 `ExportToDisk_ShouldProduceParseableJson` | ✅ |
| 10. 隐私模式脱敏 | `ExportServiceTests.ExportScanResults_PrivacyMode_ShouldSanitize` | ✅ |
| 11. `run.ps1` 全开关组合：`-Build` / `-Release -Build` / `-Test` / `-Clean -Build` / `-Pretty` | 顺序触发并比对输出 | ✅ |
| 12. `run.bat` 透传参数 | `cmd /c "run.bat -Test"` | ✅ 同样 83/83 |
| 13. `dotnet clean` 真实清理 + 再构建 | bin/obj 计数 4 → 0 → 4 | ✅ |
| 14. 退出后无僵尸 dotnet 宿主 | `Get-Process` 检查 | ✅ |

---

## 附录 B：测试矩阵速查

| 类别 | 自动 (本次) | 手动 |
| - | - | - |
| 编译 / 单元 | 全 | — |
| 进程生命周期 | 启动、退出、单实例、Mutex、Pipe | 视觉确认（窗口前台、托盘图标） |
| 配置存储 | `settings.json`、HKCU\Run round-trip | 用 `regedit` 肉眼复核 |
| 设备识别 | WMI mock | 真实 U 盘热插拔 |
| 弹出 | API 异常分支 + enum 完整性 | 真实弹出 / Veto 分支 |
| 占用扫描 | RM mock | 真实进程占用 |
| 隐私脱敏 | 字符串断言 | 真实带用户名路径再确认一次 |
| 长稳定性 | — | 10 分钟 / 反复插拔 |
