# AgentScreenGuard (护屏卫士)

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%207%2F8%2F10%2F11-0078d7.svg)](https://microsoft.com/windows)
[![Binary Size](https://img.shields.io/badge/Binary%20Size-~25%20KB-success.svg)](bin/AgentScreenGuard.exe)
[![Zero Dependency](https://img.shields.io/badge/Dependencies-Zero%20(Pure%20Native)-brightgreen.svg)](#)
[![DPI Aware](https://img.shields.io/badge/DPI-Per--Monitor%20V2-blueviolet.svg)](#)

**专为长时间运行 AI Agent、本地大模型推理与长耗时计算任务设计的轻量化防烧屏与系统保活工具。**  
*Ultra-lightweight Screen-Off & Anti-Burn-In Guard for Long-Running AI Agents on Windows.*

[功能特性](#-功能特性) • [物理机理与技术校核](#-物理机理与技术校核) • [快速开始](#-快速开始) • [源码构建](#-源码构建) • [全局快捷键](#-全局快捷键)

</div>

---

## 📖 背景与痛点 (Problem Statement)

当本地运行长时间的自主 Agent（如大模型自我迭代、代码深度重构、全自动化爬取/测试、跨夜编译）时，屏幕往往持续停留于高对比度静态界面（如黑底白字控制台、IDE 边界、进度指示条）：
1. **OLED / Mini-LED 烧屏风险**：发光二极管长时间高亮静态点亮，易导致局部有机发光材料过早不可逆衰减，产生永久残影。
2. **笔记本自动休眠中断任务**：许多笔记本（如支持 Modern Standby S0ix 的轻薄本/全能本）在常规熄屏后，若无专门的系统保活锁，空闲定时器超时后将整机挂入睡眠（DRIPS），导致 CPU 节流、网络中断、Agent 进程被彻底暂停。
3. **GUI / 视觉识别 Agent 截屏黑屏失效**：若简单切断显示链路（DPMS Off），部分显卡驱动会使虚拟桌面帧缓冲区输出黑帧或重置分辨率，导致依赖计算机视觉（如 `pyautogui` / CV 目标检测）的自动化任务崩溃。

**AgentScreenGuard** 针对以上全部痛点而生，提供纯原生、零依赖、单文件便携的桌面 GUI 解决方案。

---

## ✨ 功能特性 (Key Features)

### 1. 🎛️ 四大核心护屏模式 (按需自由切换)

| 模式 | 快捷键 | 核心机理 | 适用场景 |
| :--- | :---: | :--- | :--- |
| **🌑 护屏保活 (屏黑·不休眠)** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>G</kbd> | **推荐**。不向系统发任何"关屏"指令：把内置屏背光压到 0（WMI `WmiMonitorBrightnessMethods`，失败退化为 gamma 压暗）+ 跨屏纯黑置顶窗体，再挂三层保活（`PowerCreateRequest`/`PowerSetRequest`、后台线程每 10s 重申 `ES_SYSTEM_REQUIRED`、电源空闲超时临时归零）。系统始终认为屏幕开着 → 不会进入 Modern Standby，CPU/网络/Agent 全程满速。按 <kbd>Esc</kbd> 或双击退出并精确还原。 | **通宵 Agent、跨夜编译、Modern Standby (S0ix) 笔记本。** |
| **⚡ 硬件熄屏 (DPMS)** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>S</kbd> | 广播 `SC_MONITORPOWER` 驱动 GPU 切断显示链路（DPMS D3），像素归零、背光断电。内嵌按键防抖，鼠标轻晃即醒。**⚠ 在无 S3 的 S0ix 平台上，该指令会被固件直接映射成"进入新型待机"，长任务会中断（本机三次实测命中）。** | 短暂离开、此刻没有长任务在跑的场合。 |
| **🔒 锁定并熄屏** | GUI 按钮 | `LockWorkStation()` 锁屏后硬件关屏。 | 离开工位、外出防窥。同样受上面 S0ix 限制。 |
| **⬛ 纯黑幕布 (不改背光)** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>B</kbd> | 只覆盖全黑置顶层，不切显示链路、不调背光，帧缓冲与分辨率完好。按 <kbd>Esc</kbd> 或双击退出。 | 依赖 `pyautogui.screenshot()` / CV 识别的 GUI Agent。 |

### 2. 🪶 极致原生与轻量化 (Zero Bloatware)
- **单文件仅 ~25 KB**：没有 Electron（动辄 150MB+）、没有 Python 打包膨胀（动辄 50MB+）、不需要安装任何运行时或 SDK。
- **紧凑极简布局**：固定 430×248 像素基准窗（按 DPI 等比换算），绝对像素定位 + `TextRenderer.MeasureText` 实测文字宽度，每行高度精确匹配内容，无文字截断、无重叠、无大面积空白。
- **Per-Monitor V2 高分屏适配**：内嵌高清 DPI 清单与 GDI+ 字体防泄漏缓存，在 2.5K/4K 等高分屏（如 125%、150% 缩放）下文本与边框极度锐利，绝无模糊拉伸。
- **现代暗色主题 UI**：采用深色低刺激 OLED Dark 调色体系，直观呈现实时 AC/DC 供电状态与防休眠激活标志。
- **系统托盘常驻**：支持双击托盘恢复、右键快捷菜单快速关屏、关屏防休眠切换，退出时自动平稳复原系统电源策略。

---

## 🔬 物理机理与技术校核 (Numerical Verification)

```text
               +-----------------------------+
               |      AgentScreenGuard       |
               +--------------+--------------+
                              |
       +----------------------+----------------------+
       |                                             |
       v                                             v
[硬件切断 DPMS D3]                           [内核系统保活锁]
WM_SYSCOMMAND: 0x0112                        SetThreadExecutionState:
SC_MONITORPOWER: 0xF170 (61808)              ES_CONTINUOUS     (0x80000000)
MONITOR_OFF: 2                               ES_SYSTEM_REQUIRED (0x00000001)
       |                                     ES_AWAYMODE_REQUIRED(0x00000040)
       v                                             |
+---------------+                             +---------------+
| OLED / LCD    |                             | Windows 电源  |
| 像素 0 nit    |                             | 严禁整机睡眠  |
| 背光断电      |                             | CPU/网络全速  |
+---------------+                             +---------------+
```

1. **DPMS D3 硬件关断与防抖校核**：
   - 物理微动开关在双击或回车触发后存在 50~200ms 的按键弹起阶段（`WM_LBUTTONUP`）。若不作处理，弹起信号将作为唤醒事件重新点亮显示器。
   - 本项目通过 `GetAsyncKeyState` 轮询虚拟键码 `VK_LBUTTON (0x01)`、`VK_RBUTTON (0x02)`、`VK_MBUTTON (0x04)` 高位掩码 `0x8000`，确保物理按键完全弹起后再执行延时并关屏。
2. **防休眠与离开模式校核**：
   - `ES_SYSTEM_REQUIRED` 重置空闲计时器，阻止 Windows 将核心 CPU 降频或挂起到 DRIPS 状态。
   - `ES_AWAYMODE_REQUIRED` 告知操作系统处于无人值守高负载状态。
   - **关键设计：绝不置位 `ES_DISPLAY_REQUIRED (0x00000002)`**，从而在压制系统睡眠的同时，允许显示器硬件保持完全熄灭。

---

## 🧪 平台实测结论：为什么"熄屏"和"不休眠"在本机不能兼得 (2026-11-11)

测试机：MECHREVO WUJIE15X（AMD Radeon 780M，内置 BOE 2560×1600@120Hz，**无 S3，仅 Modern Standby S0ix**）。

| 实验 | 手段 | 结果 |
| :--- | :--- | :--- |
| ① | `PowerSetRequest(SystemRequired / ExecutionRequired)` + 每 3s 重申 `ES_SYSTEM_REQUIRED` 心跳 | 熄屏瞬间心跳中断 **69 秒**，Kernel-Power **506** 进入新型待机 |
| ② | `ES_CONTINUOUS｜ES_SYSTEM_REQUIRED｜ES_DISPLAY_REQUIRED`（0x80000003） | 熄屏即 506，同样被挂起 |
| ③ | `ES_CONTINUOUS｜ES_SYSTEM_REQUIRED｜ES_AWAYMODE_REQUIRED`（0x80000041） | 熄屏即 506，同样被挂起 |
| ④ | DDC/CI `GetVCPFeatureAndVCPFeatureReply`（VCP 0xD6/0x10/0x6F/0xDF） | 全部 `ok=False err=31`（`ERROR_GEN_FAILURE`），不支持显示器级硬关屏 |
| ⑤ | 断开内置显示器（`SetDisplayConfig` 拓扑 / `DisplaySwitch /external`） | 单屏机不允许"零活动显示"，此路不通 |

**结论**：该平台固件把"DPMS 硬件熄屏"直接映射为"进入 Modern Standby"，任何内核电源锁都拦不住。
因此本工具默认走**假黑屏**：不关屏 → 系统永不判定屏幕关闭 → 整机不入待机；代价是 0% 背光下仍有面板自身的最小背光余辉（软件已到硬件下限，回读确认亮度真实为 0）。

> 若需要"屏幕物理全灭 + 整机不休眠"，唯一途径是关闭 Modern Standby：
> `reg add "HKLM\SYSTEM\CurrentControlSet\Control\Power" /v PlatformAoAcOverride /t REG_DWORD /d 0 /f`（管理员 + 重启；还原用 `reg delete` 同名值）。副作用：合盖与睡眠按钮不再进入睡眠。

---

## 🚀 快速开始 (Quick Start)

### 方式一：直接下载便携版 (Single Executable)
1. 前往 GitHub [Releases](https://github.com) 页面下载 `AgentScreenGuard.exe`。
2. 双击直接运行，无需安装，即下即用。

### 方式二：使用桌面交互批处理
本项目同时提供了免编译的纯原生批处理版本：
- 桌面控制台脚本：`屏幕管理与Agent保活.bat`

---

## 🛠️ 源码构建 (Build from Source)

本项目采用**纯零依赖极简构建**。任何一台搭载 Windows 7/8/10/11 的电脑，均自带了 .NET Framework 原生 C# 编译器（`csc.exe`），无需安装 Visual Studio 或 .NET Core SDK 即可在 1 秒内完成编译：

```cmd
git clone https://github.com/your-username/AgentScreenGuard.git
cd AgentScreenGuard
build.bat
```

构建产物将自动生成于 `bin\AgentScreenGuard.exe`。

---

## ⌨️ 全局快捷键 (Global Hotkeys)

| 快捷键 | 功能 |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>G</kbd> | **护屏保活**（背光 0 + 全黑幕 + 三层保活，<kbd>Esc</kbd> / 双击退出） |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>S</kbd> | **硬件熄屏**（DPMS；S0ix 平台会进新型待机，慎用于长任务） |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>B</kbd> | **纯黑幕布**（不改背光，<kbd>Esc</kbd> / 双击退出） |

---

## 📄 开源许可 (License)

本项目基于 [MIT License](LICENSE) 开源，允许任何形式的个人或商业自由使用与分发。
