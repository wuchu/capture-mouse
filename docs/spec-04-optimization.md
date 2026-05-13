# 迭代四 Spec：优化与完善

## 目标
提升用户体验和系统稳定性，增加高级功能。

## 功能需求

### 1. 断线重连机制
- [ ] 检测连接断开
- [ ] 自动重连策略（指数退避）
- [ ] 重连次数限制
- [ ] 手动重连选项

### 2. 配置持久化
- [ ] JSON 配置文件
- [ ] 自动保存配置
- [ ] 配置项：
  - macOS IP 地址
  - VNC 密码（加密存储）
  - 快捷键设置
  - 鼠标灵敏度
  - 边缘检测开关

### 3. 边缘检测切换
- [ ] 鼠标移到屏幕边缘自动切换
- [ ] 可配置触发边缘（左/右/上/下）
- [ ] 触发延迟（防止误触）
- [ ] 视觉反馈（边缘高亮）

### 4. 输入延迟优化
- [ ] 批量发送鼠标移动事件
- [ ] 键盘事件优先级队列
- [ ] 网络缓冲区优化
- [ ] 延迟显示（可选）

### 5. 多显示器支持
- [ ] 检测显示器配置
- [ ] 跨显示器鼠标追踪
- [ ] 显示器选择配置

## 技术细节

### 断线重连

```csharp
public class ReconnectManager
{
    private int _attemptCount = 0;
    private readonly int[] _backoffDelays = { 1, 2, 4, 8, 16 }; // 秒
    
    public async Task<bool> TryReconnect(Func<Task<bool>> connectFunc)
    {
        while (_attemptCount < _backoffDelays.Length)
        {
            var delay = _backoffDelays[_attemptCount];
            Console.WriteLine($"{delay}秒后尝试重连...");
            await Task.Delay(TimeSpan.FromSeconds(delay));
            
            if (await connectFunc())
            {
                _attemptCount = 0;
                return true;
            }
            
            _attemptCount++;
        }
        
        return false;
    }
}
```

### 配置系统

```csharp
public class AppConfig
{
    public string LastHost { get; set; } = "";
    public int LastPort { get; set; } = 5900;
    public string EncryptedPassword { get; set; } = "";
    
    // 快捷键
    public HotkeyConfig ToggleHotkey { get; set; } = new() 
    { 
        Modifiers = KeyModifiers.Control | KeyModifiers.Alt,
        Key = Keys.M 
    };
    
    // 边缘检测
    public bool EdgeDetectionEnabled { get; set; } = true;
    public ScreenEdge TriggerEdge { get; set; } = ScreenEdge.Right;
    public int EdgeThreshold { get; set; } = 5; // 像素
    public int EdgeDelayMs { get; set; } = 300; // 毫秒
    
    // 性能
    public int MouseThrottleMs { get; set; } = 8;
    public bool ShowLatency { get; set; } = false;
}
```

### 边缘检测

```csharp
public class EdgeDetector
{
    private Screen _currentScreen;
    
    public ScreenEdge? CheckEdge(int x, int y)
    {
        var screen = Screen.FromPoint(new Point(x, y));
        var bounds = screen.Bounds;
        
        if (x <= bounds.Left + Config.EdgeThreshold) return ScreenEdge.Left;
        if (x >= bounds.Right - Config.EdgeThreshold) return ScreenEdge.Right;
        if (y <= bounds.Top + Config.EdgeThreshold) return ScreenEdge.Top;
        if (y >= bounds.Bottom - Config.EdgeThreshold) return ScreenEdge.Bottom;
        
        return null;
    }
}
```

### 批量发送优化

```csharp
public class BatchedInputSender
{
    private readonly Queue<InputEvent> _queue = new();
    private readonly Timer _flushTimer;
    
    public BatchedInputSender(TimeSpan interval)
    {
        _flushTimer = new Timer(Flush, null, interval, interval);
    }
    
    public void Enqueue(InputEvent evt)
    {
        lock (_queue)
        {
            // 合并连续的鼠标移动事件
            if (evt.Type == InputEventType.MouseMove && 
                _queue.Count > 0 && 
                _queue.Last().Type == InputEventType.MouseMove)
            {
                _queue.Dequeue(); // 移除旧的
            }
            _queue.Enqueue(evt);
        }
    }
    
    private void Flush(object? state)
    {
        lock (_queue)
        {
            while (_queue.Count > 0)
            {
                var evt = _queue.Dequeue();
                SendToVnc(evt);
            }
        }
    }
}
```

## 界面设计

### 配置对话框

```
┌─────────────────────────────────────┐
│  CaptureMouse 配置                   │
├─────────────────────────────────────┤
│                                     │
│  macOS 连接                          │
│  ├─ IP 地址: [________________]      │
│  ├─ 端口:    [5900    ]             │
│  └─ 密码:    [________________]      │
│                                     │
│  快捷键                              │
│  ├─ 切换模式: [Ctrl+Alt+M]          │
│  └─ 退出:     [Ctrl+Alt+Q]          │
│                                     │
│  边缘检测                            │
│  [✓] 启用边缘检测                    │
│  触发边缘: [右 ▼]                    │
│  触发延迟: [300] 毫秒                │
│                                     │
│  高级                                │
│  [ ] 显示输入延迟                    │
│  鼠标节流: [8] 毫秒                   │
│                                     │
│           [保存] [取消]              │
└─────────────────────────────────────┘
```

## 性能目标

| 指标 | 目标值 |
|------|--------|
| 输入延迟 | < 50ms |
| 重连时间 | < 5s |
| 内存占用 | < 50MB |
| CPU 占用 | < 5% |

## 测试验证

### 断线测试
1. 连接成功后断开 macOS 网络
2. 验证自动重连触发
3. 验证退避策略
4. 恢复网络，验证重连成功

### 边缘检测测试
1. 启用边缘检测
2. 鼠标移到右边缘
3. 验证 300ms 后自动切换到远程模式
4. 鼠标移回，验证切换回本地模式

### 配置测试
1. 修改配置并保存
2. 重启程序，验证配置加载
3. 验证密码加密存储

## 交付物

- [ ] `ConfigManager.cs` - 配置管理
- [ ] `ReconnectManager.cs` - 重连管理
- [ ] `EdgeDetector.cs` - 边缘检测
- [ ] `BatchedInputSender.cs` - 批量发送
- [ ] `SettingsForm.cs` - 配置界面

## 时间估算

3-4 天

## 依赖

- 迭代一、二、三
