# 迭代五 Spec：打包与部署

## 目标
完成产品化，交付可独立运行的程序。

## 功能需求

### 1. 单文件可执行程序
- [ ] 使用 `PublishSingleFile` 发布
- [ ] 自包含运行时（无需 .NET 运行时）
- [ ] 裁剪未使用代码（Trimming）
- [ ] 压缩输出

### 2. 安装程序（可选）
- [ ] 创建 MSI 安装包
- [ ] 注册到开始菜单
- [ ] 可选：开机自启动
- [ ] 卸载支持

### 3. 用户文档
- [ ] README.md 使用说明
- [ ] macOS 配置指南
- [ ] 故障排查指南
- [ ] 快捷键速查表

### 4. 开源发布准备
- [ ] LICENSE 文件
- [ ] CONTRIBUTING.md
- [ ] CHANGELOG.md
- [ ] GitHub Actions CI/CD

## 技术细节

### 发布配置

```xml
<!-- CaptureMouse.csproj -->
<PropertyGroup>
    <!-- 单文件发布 -->
    <PublishSingleFile>true</PublishSingleFile>
    
    <!-- 自包含运行时 -->
    <SelfContained>true</SelfContained>
    
    <!-- 目标平台 -->
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    
    <!-- 裁剪 -->
    <PublishTrimmed>true</PublishTrimmed>
    
    <!-- 压缩 -->
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    
    <!--  ReadyToRun 优化 -->
    <PublishReadyToRun>true</PublishReadyToRun>
</PropertyGroup>
```

### 发布命令

```bash
# 发布单文件可执行程序
dotnet publish -c Release -o ./publish

# 结果：
# ./publish/CaptureMouse.exe  (~ 15-20 MB)
```

### 文件大小优化

| 配置 | 大小 |
|------|------|
| 默认 | ~ 150 MB |
| PublishSingleFile | ~ 60 MB |
| + Trimmed | ~ 25 MB |
| + Compression | ~ 15 MB |

### 安装程序（WiX Toolset）

```xml
<!-- Setup.wxs -->
<Product Id="*" Version="1.0.0" ...>
    <Package InstallerVersion="200" Compressed="yes" .../>
    
    <Directory Id="TARGETDIR" Name="SourceDir">
        <Directory Id="ProgramFilesFolder">
            <Directory Id="INSTALLFOLDER" Name="CaptureMouse">
                <Component>
                    <File Source="CaptureMouse.exe"/>
                </Component>
            </Directory>
        </Directory>
        
        <Directory Id="ProgramMenuFolder">
            <Component>
                <Shortcut Name="CaptureMouse" Target="CaptureMouse.exe"/>
            </Component>
        </Directory>
    </Directory>
</Product>
```

## 项目结构

```
capture-mouse/
├── docs/
│   ├── spec-01-connection.md
│   ├── spec-02-input-capture.md
│   ├── spec-03-control-switch.md
│   ├── spec-04-optimization.md
│   ├── spec-05-deployment.md
│   └── DESIGN.md
├── src/
│   └── CaptureMouse/
│       ├── CaptureMouse.csproj
│       ├── Program.cs
│       ├── VncClient.cs
│       ├── InputCapture.cs
│       ├── InputEvent.cs
│       ├── ControlManager.cs
│       ├── TrayIcon.cs
│       ├── HotkeyManager.cs
│       ├── ConfigManager.cs
│       └── KeyMapping.cs
├── tests/
│   └── CaptureMouse.Tests/
├── installer/
│   └── Setup.wxs
├── .github/
│   └── workflows/
│       └── build.yml
├── README.md
├── LICENSE
├── CHANGELOG.md
└── CONTRIBUTING.md
```

## README.md 模板

```markdown
# CaptureMouse

让 Windows 键鼠临时控制 macOS 的小工具。

## 特点

- ✅ macOS 无需安装任何软件
- ✅ 基于原生 VNC 协议
- ✅ 快捷键一键切换
- ✅ 边缘检测自动切换
- ✅ 单文件，无需安装

## 使用方法

### 1. macOS 端配置

1. 打开系统设置 → 通用 → 共享
2. 开启"屏幕共享"
3. 设置访问密码
4. 记录 IP 地址

### 2. Windows 端使用

1. 下载 `CaptureMouse.exe`
2. 双击运行
3. 输入 macOS IP 和密码
4. 按 `Ctrl + Alt + M` 切换控制权

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| `Ctrl + Alt + M` | 切换本地/远程模式 |
| `Ctrl + Alt + Q` | 退出程序 |

## 系统要求

- Windows 11
- macOS 10.15+（开启屏幕共享）
- 同一局域网

## 下载

[Releases](../../releases)

## License

MIT
```

## CI/CD 配置

```yaml
# .github/workflows/build.yml
name: Build

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      
      - name: Restore
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore --configuration Release
      
      - name: Publish
        run: dotnet publish --no-build --configuration Release --output ./publish
      
      - name: Upload
        uses: actions/upload-artifact@v4
        with:
          name: CaptureMouse
          path: ./publish/CaptureMouse.exe
```

## 测试验证

### 发布测试
1. 执行发布命令
2. 验证输出文件大小 < 20MB
3. 复制到干净环境测试运行
4. 验证无需安装 .NET 运行时

### 安装测试
1. 运行安装程序
2. 验证开始菜单快捷方式
3. 验证卸载功能

## 交付物

- [ ] `CaptureMouse.exe` - 主程序
- [ ] `Setup.msi` - 安装程序（可选）
- [ ] `README.md` - 使用说明
- [ ] `LICENSE` - MIT 许可证
- [ ] `CHANGELOG.md` - 更新日志
- [ ] GitHub Release

## 时间估算

1-2 天

## 依赖

- 迭代一、二、三、四完成
- Windows 11 开发环境
- WiX Toolset（可选，用于安装包）
