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
| **一键即时熄屏** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>S</kbd> | 发送底层 `SC_MONITORPOWER` 广播，驱动 GPU 切断显示链路（DPMS D3）。OLED 像素发光度直接归零（**0.0000 nit**），功耗近乎为零。内嵌按键物理防抖，鼠标轻晃即醒。 | 常规后台计算、本地 LLM 推理、终端编译等非 GUI 视觉任务。 |
| **Agent 守护保活** | GUI 开关 | 调用底层内核 `SetThreadExecutionState` 注入 `ES_SYSTEM_REQUIRED \| ES_AWAYMODE_REQUIRED`。**强制阻止整机睡眠挂起**，CPU 与网络全速高负载运转，跨夜通宵任务不断线。 | 通宵大型 Agent 任务、长时间自动化测试、长时间文件同步。 |
| **锁定并熄屏** | GUI 按钮 | 调用 Win32 `LockWorkStation()` (Win+L) 并同步执行硬件关屏。 | 离开工位、外出防窥且需要后台任务持续运转。 |
| **OLED 纯黑置顶幕布** | <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>B</kbd> | 覆盖跨屏幕的无边框顶层全黑窗体（#000000）。**OLED 子像素 100% 物理熄灭**，且显卡驱动显示链路不断开，完好保留帧缓冲区与分辨率。按 <kbd>Esc</kbd> 退出。 | 依赖屏幕截取（`pyautogui.screenshot()` / CV 视觉模型识别）的 GUI Agent。 |

### 2. 🪶 极致原生与轻量化 (Zero Bloatware)
- **单文件仅 ~25 KB**：没有 Electron（动辄 150MB+）、没有 Python 打包膨胀（动辄 50MB+）、不需要安装任何运行时或 SDK。
- **Per-Monitor V2 高分屏适配**：内嵌高清 DPI 清单，在 2.5K/4K 等高分屏（如 125%、150% 缩放）下文本与边框极度锐利，绝无模糊拉伸。
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
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>S</kbd> | **一键即时熄屏** (DPMS D3 硬件切断，后台全速) |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>B</kbd> | **全屏纯黑幕布** (针对截屏 Agent，按 Esc 退出) |

---

## 📄 开源许可 (License)

本项目基于 [MIT License](LICENSE) 开源，允许任何形式的个人或商业自由使用与分发。
