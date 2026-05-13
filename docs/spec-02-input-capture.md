# 迭代二 Spec：输入捕获层

## 目标
在 Windows 端捕获本地键鼠输入，为后续转发到 macOS 做准备。

## 功能需求

### 1. Raw Input 设备注册
- [ ] 注册鼠标设备 (HID)
- [ ] 注册键盘设备 (HID)
- [ ] 处理设备变更通知

### 2. 全局输入捕获
- [ ] 捕获鼠标移动事件（绝对坐标）
- [ ] 捕获鼠标按键事件（左/中/右键）
- [ ] 捕获鼠标滚轮事件
- [ ] 捕获键盘按键事件（按下/释放）
- [ ] 捕获键盘修饰键（Shift/Ctrl/Alt/Win）

### 3. 输入事件队列
- [ ] 线程安全的事件队列
- [ ] 事件去重和节流
- [ ] 事件类型定义

## 技术细节

### Windows Raw Input API

Raw Input 是 Windows 推荐的全局输入捕获方式，相比钩子（Hook）更加稳定且不会被安全软件拦截。

```csharp
// 注册 Raw Input 设备
[DllImport("user32.dll")]
static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

// 获取 Raw Input 数据
[DllImport("user32.dll")]
static extern int GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
```

### 输入事件结构

```csharp
public enum InputEventType
{
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    KeyDown,
    KeyUp
}

public class InputEvent
{
    public InputEventType Type { get; set; }
    public long Timestamp { get; set; }
    
    // Mouse
    public int X { get; set; }
    public int Y { get; set; }
    public int Button { get; set; }  // 1=左键, 2=右键, 4=中键
    public int WheelDelta { get; set; }
    
    // Keyboard
    public ushort KeyCode { get; set; }      // Windows 虚拟键码
    public uint KeySym { get; set; }         // X11 KeySym (用于 VNC)
    public bool IsExtended { get; set; }
}
```

### 键盘映射

Windows 虚拟键码 (VK) 需要映射到 X11 KeySym：

```csharp
public static class KeyMapping
{
    // 字母
    public static uint VK_A = 0x41;  // -> 0x0061 (XK_a)
    public static uint VK_B = 0x42;  // -> 0x0062 (XK_b)
    // ...
    
    // 数字
    public static uint VK_0 = 0x30;  // -> 0x0030 (XK_0)
    // ...
    
    // 功能键
    public static uint VK_F1 = 0x70;  // -> 0xFFBE (XK_F1)
    // ...
    
    // 特殊键
    public static uint VK_RETURN = 0x0D;   // -> 0xFF0D (XK_Return)
    public static uint VK_ESCAPE = 0x1B;   // -> 0xFF1B (XK_Escape)
    public static uint VK_SPACE = 0x20;    // -> 0x0020 (XK_space)
    // ...
}
```

### 鼠标坐标转换

```
Windows 屏幕坐标 (0,0) - (ScreenWidth, ScreenHeight)
            ↓
    按比例映射
            ↓
VNC 坐标 (0-65535, 0-65535)
```

## 接口设计

```csharp
public class InputCapture : IDisposable
{
    // 事件
    public event EventHandler<InputEvent>? InputReceived;
    
    // 状态
    public bool IsCapturing { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    
    // 控制
    public bool StartCapture();
    public void StopCapture();
    
    // 配置
    public bool CaptureMouse { get; set; }
    public bool CaptureKeyboard { get; set; }
}
```

## 实现要点

### 1. 消息循环集成

Raw Input 需要通过 Windows 消息循环接收数据：

```csharp
// 在 Form 或 NativeWindow 中处理
protected override void WndProc(ref Message m)
{
    if (m.Msg == WM_INPUT)
    {
        ProcessRawInput(m.LParam);
    }
    base.WndProc(ref m);
}
```

### 2. 后台线程处理

为避免阻塞 UI，输入处理应在后台线程：

```csharp
private readonly BlockingCollection<InputEvent> _eventQueue = new();

private void ProcessInputThread()
{
    foreach (var evt in _eventQueue.GetConsumingEnumerable())
    {
        InputReceived?.Invoke(this, evt);
    }
}
```

### 3. 鼠标事件节流

鼠标移动事件频率很高，需要节流：

```csharp
private DateTime _lastMouseMove = DateTime.MinValue;
private readonly TimeSpan _mouseThrottle = TimeSpan.FromMilliseconds(8); // ~120fps

private void OnMouseMove(int x, int y)
{
    if (DateTime.Now - _lastMouseMove < _mouseThrottle) return;
    _lastMouseMove = DateTime.Now;
    
    // 发送事件
}
```

## 测试验证

### 测试步骤
1. 运行程序，启动输入捕获
2. 移动鼠标，观察坐标输出
3. 点击鼠标按键，观察按键事件
4. 滚动滚轮，观察滚轮事件
5. 按下键盘按键，观察按键事件

### 预期结果
- [ ] 鼠标移动事件捕获正常，坐标准确
- [ ] 鼠标按键事件捕获正常，左右中键区分
- [ ] 键盘事件捕获正常，KeySym 映射正确
- [ ] 事件队列无内存泄漏

## 交付物

- [ ] `InputCapture.cs` - 输入捕获核心实现
- [ ] `KeyMapping.cs` - 键盘映射表
- [ ] `InputEvent.cs` - 事件类型定义

## 时间估算

2-3 天

## 依赖

- Windows 11
- .NET 9 SDK
- 物理键鼠设备
