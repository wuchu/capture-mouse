# CaptureMouse

让 Windows 键鼠临时控制 macOS 的创新方案。

## 🎯 核心特点

- ✅ **macOS 零安装** - 完全利用原生 Screen Sharing，无需安装任何软件
- ✅ **Windows 单端开发** - 降低维护成本，集中优化
- ✅ **轻量级** - 单文件可执行程序，< 20MB
- ✅ **低延迟** - 直连 VNC 协议，输入延迟 < 50ms
- ✅ **一键切换** - `Ctrl + Alt + M` 快速切换控制权
- ✅ **边缘检测** - 鼠标移到屏幕边缘自动切换（可选）

## 🏗️ 技术架构

```
Windows PC                          macOS
├─ InputCapture (Raw Input)         └─ Screen Sharing (VNC Server)
├─ VncClient (RFB 3.8)                  端口 5900
├─ ControlManager (状态机)
└─ TrayIcon (系统托盘)
```

## 📋 项目进展

| 迭代 | 功能 | 状态 | 文档 |
|------|------|------|------|
| 一 | 基础连接层 - VNC 协议实现 | ✅ 已完成 | [spec-01](docs/spec-01-connection.md) |
| 二 | 输入捕获层 - Raw Input | ✅ 已完成 | [spec-02](docs/spec-02-input-capture.md) |
| 三 | 控制权切换 - 快捷键 | 🚧 进行中 | [spec-03](docs/spec-03-control-switch.md) |
| 四 | 优化完善 - 重连/配置 | 📅 待开始 | [spec-04](docs/spec-04-optimization.md) |
| 五 | 打包部署 - 单文件 | 📅 待开始 | [spec-05](docs/spec-05-deployment.md) |

### 当前完成的功能

- ✅ VNC 客户端连接 macOS Screen Sharing
- ✅ RFB 3.8 协议握手和认证
- ✅ 键鼠事件发送（鼠标移动、点击、滚轮、键盘按键）
- ✅ Windows Raw Input 全局输入捕获
- ✅ 键码映射（Windows → X11 KeySym）
- ✅ GitHub Actions 自动构建
- ✅ 单文件发布配置

### 待实现功能

- 🚧 全局快捷键切换控制模式
- 🚧 系统托盘图标和状态显示
- 🚧 配置持久化
- 🚧 连接断线重连
- 🚧 边缘检测自动切换

## 📋 迭代计划

| 迭代 | 功能 | 文档 |
|------|------|------|
| 一 | 基础连接层 - VNC 协议实现 | [spec-01](docs/spec-01-connection.md) |
| 二 | 输入捕获层 - Raw Input | [spec-02](docs/spec-02-input-capture.md) |
| 三 | 控制权切换 - 快捷键 | [spec-03](docs/spec-03-control-switch.md) |
| 四 | 优化完善 - 重连/配置 | [spec-04](docs/spec-04-optimization.md) |
| 五 | 打包部署 - 单文件 | [spec-05](docs/spec-05-deployment.md) |

## 🚀 快速开始

### macOS 端配置

1. 打开**系统设置** → **通用** → **共享**
2. 开启**屏幕共享**
3. 点击**电脑设置**，设置 VNC 密码
4. 记录 IP 地址（**系统设置** → **网络**）

### Windows 端使用

```bash
# 1. 克隆仓库
git clone https://github.com/yourname/capture-mouse.git
cd capture-mouse

# 2. 构建并运行
dotnet run --project src/CaptureMouse

# 3. 输入 macOS IP 和密码
# 4. 按 Ctrl + Alt + M 切换控制权
```

## ⌨️ 快捷键

| 快捷键 | 功能 |
|--------|------|
| `Ctrl + Alt + M` | 切换本地/远程模式 |
| `Ctrl + Alt + Q` | 退出程序 |

## 📁 项目结构

```
capture-mouse/
├── docs/                    # 设计文档
│   ├── DESIGN.md           # 整体设计文档
│   ├── spec-01-connection.md
│   ├── spec-02-input-capture.md
│   ├── spec-03-control-switch.md
│   ├── spec-04-optimization.md
│   └── spec-05-deployment.md
├── src/
│   └── CaptureMouse/       # 主项目
│       ├── CaptureMouse.csproj
│       ├── Program.cs
│       ├── VncClient.cs    # VNC 客户端
│       ├── InputCapture.cs # 输入捕获
│       ├── InputEvent.cs   # 事件定义
│       └── ...
└── README.md
```

## 🛠️ 技术栈

- **语言**: C# .NET 9
- **平台**: Windows 11
- **协议**: VNC (RFB 3.8)
- **输入**: Windows Raw Input API
- **UI**: System.Windows.Forms

## 📝 详细设计

参见 [DESIGN.md](docs/DESIGN.md)

## 📄 许可证

MIT License

## 🤝 贡献

欢迎提交 Issue 和 PR！

---

**注意**: 迭代一、二已完成，代码已提交。迭代三（快捷键切换）进行中。

### 构建状态

[![Build Windows x64](https://github.com/wuchu/capture-mouse/actions/workflows/build.yml/badge.svg)](https://github.com/wuchu/capture-mouse/actions/workflows/build.yml)
