# 迭代三 Spec：控制权切换

## 目标
实现快捷键切换控制权，在本地模式和远程模式之间切换。

## 功能需求

### 1. 全局快捷键
- [ ] 注册 `Ctrl + Alt + M` 切换控制权
- [ ] 注册 `Ctrl + Alt + Q` 退出程序
- [ ] 快捷键冲突检测
- [ ] 快捷键自定义配置

### 2. 控制状态机
- [ ] 定义三种状态：未连接/本地模式/远程模式
- [ ] 状态转换逻辑
- [ ] 状态持久化

### 3. 输入拦截
- [ ] 远程模式下拦截本地键鼠输入
- [ ] 本地模式下恢复键鼠输入
- [ ] 平滑切换（无卡顿）

### 4. 系统托盘
- [ ] 显示当前状态图标
- [ ] 右键菜单（连接/断开/退出）
- [ ] 双击切换模式
- [ ] 气泡提示

## 技术细节

### 状态机设计

```
                    ┌─────────────┐
                    │  未连接     │
                    │ (灰色图标)  │
                    └──────┬──────┘
                           │ 连接成功
                           ▼
              ┌────────────────────────┐
              │        本地模式        │
              │    (蓝色图标)          │
              │  键鼠控制本地 Windows  │
              └───────────┬────────────┘
                          │ Ctrl+Alt+M
                          ▼
              ┌────────────────────────┐
              │        远程模式        │
              │    (绿色图标)          │
              │  键鼠转发到 macOS      │
              └───────────┬────────────┘
                          │ Ctrl+Alt+M
                          │ 或断线
                          ▼
              └────────────────────────┘
```

### 快捷键注册

```csharp
[DllImport("user32.dll")]
static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll")]
static extern bool UnregisterHotKey(IntPtr hWnd, int id);

// 修饰符
const uint MOD_ALT = 0x0001;
const uint MOD_CONTROL = 0x0002;
const uint MOD_SHIFT = 0x0004;

// 使用
RegisterHotKey(hWnd, 1, MOD_CONTROL | MOD_ALT, (uint)Keys.M); // Ctrl+Alt+M
RegisterHotKey(hWnd, 2, MOD_CONTROL | MOD_ALT, (uint)Keys.Q); // Ctrl+Alt+Q
```

### 输入拦截策略

```
本地模式：
  Raw Input 捕获 → 丢弃（让系统处理）

远程模式：
  Raw Input 捕获 → 发送到 VNC → 拦截本地处理
```

拦截方法：
- 鼠标：使用 `BlockInput` API 或保持 Raw Input 独占模式
- 键盘：在 Raw Input 处理中标记已处理

### 系统托盘实现

```csharp
public class TrayIcon : IDisposable
{
    private NotifyIcon _notifyIcon;
    private ContextMenuStrip _contextMenu;
    
    // 图标状态
    public void SetState(ControlState state)
    {
        _notifyIcon.Icon = state switch
        {
            ControlState.Disconnected => Icons.Gray,
            ControlState.Local => Icons.Blue,
            ControlState.Remote => Icons.Green,
            _ => Icons.Gray
        };
        
        _notifyIcon.Text = state switch
        {
            ControlState.Disconnected => "CaptureMouse - 未连接",
            ControlState.Local => "CaptureMouse - 本地模式",
            ControlState.Remote => "CaptureMouse - 远程模式",
            _ => "CaptureMouse"
        };
    }
}
```

## 接口设计

```csharp
public enum ControlState
{
    Disconnected,   // 未连接
    Local,          // 本地模式
    Remote          // 远程模式
}

public class ControlManager : IDisposable
{
    // 事件
    public event EventHandler<ControlState>? StateChanged;
    public event EventHandler? ExitRequested;
    
    // 状态
    public ControlState CurrentState { get; }
    public bool IsConnected { get; }
    
    // 控制
    public void Connect(string host, string password);
    public void Disconnect();
    public void ToggleMode();  // 本地/远程切换
    public void Exit();
    
    // 配置
    public string LastHost { get; set; }
    public HotkeyConfig ToggleHotkey { get; set; }
}
```

## 状态转换表

| 当前状态 | 事件 | 新状态 | 操作 |
|---------|------|--------|------|
| 未连接 | 连接成功 | 本地模式 | 显示蓝色图标 |
| 本地模式 | Ctrl+Alt+M | 远程模式 | 开始转发输入，显示绿色图标 |
| 远程模式 | Ctrl+Alt+M | 本地模式 | 停止转发输入，显示蓝色图标 |
| 任意 | 断线 | 未连接 | 显示灰色图标，尝试重连 |
| 任意 | Ctrl+Alt+Q | 退出 | 清理资源 |

## 测试验证

### 测试步骤
1. 启动程序，验证托盘图标显示（灰色）
2. 连接到 macOS，验证图标变蓝
3. 按 Ctrl+Alt+M，验证图标变绿，键鼠控制 macOS
4. 再按 Ctrl+Alt+M，验证图标变蓝，键鼠控制本地
5. 断开连接，验证图标变灰
6. 按 Ctrl+Alt+Q，验证程序退出

### 预期结果
- [ ] 快捷键响应及时（< 100ms）
- [ ] 状态切换平滑无卡顿
- [ ] 图标正确反映当前状态
- [ ] 远程模式下本地输入被正确拦截

## 交付物

- [ ] `ControlManager.cs` - 控制管理核心
- [ ] `TrayIcon.cs` - 系统托盘实现
- [ ] `HotkeyManager.cs` - 快捷键管理

## 时间估算

2-3 天

## 依赖

- 迭代一（VNC 连接）
- 迭代二（输入捕获）
