namespace CaptureMouse;

/// <summary>
/// 输入事件类型
/// </summary>
public enum InputEventType
{
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    KeyDown,
    KeyUp
}

/// <summary>
/// 输入事件数据
/// </summary>
public class InputEvent
{
    /// <summary>
    /// 事件类型
    /// </summary>
    public InputEventType Type { get; set; }
    
    /// <summary>
    /// 时间戳（毫秒）
    /// </summary>
    public long Timestamp { get; set; }
    
    // ========== 鼠标相关 ==========
    
    /// <summary>
    /// 鼠标 X 坐标（Windows 屏幕坐标）
    /// </summary>
    public int X { get; set; }
    
    /// <summary>
    /// 鼠标 Y 坐标（Windows 屏幕坐标）
    /// </summary>
    public int Y { get; set; }
    
    /// <summary>
    /// 鼠标按键：1=左键, 2=右键, 4=中键
    /// </summary>
    public int Button { get; set; }
    
    /// <summary>
    /// 滚轮增量（正数向上，负数向下）
    /// </summary>
    public int WheelDelta { get; set; }
    
    // ========== 键盘相关 ==========
    
    /// <summary>
    /// Windows 虚拟键码
    /// </summary>
    public ushort KeyCode { get; set; }
    
    /// <summary>
    /// X11 KeySym（用于 VNC 传输）
    /// </summary>
    public uint KeySym { get; set; }
    
    /// <summary>
    /// 是否扩展键
    /// </summary>
    public bool IsExtended { get; set; }
    
    /// <summary>
    /// 创建时间戳
    /// </summary>
    public static long GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    public override string ToString()
    {
        return Type switch
        {
            InputEventType.MouseMove => $"MouseMove({X}, {Y})",
            InputEventType.MouseButtonDown => $"MouseButtonDown(Button={Button}, {X}, {Y})",
            InputEventType.MouseButtonUp => $"MouseButtonUp(Button={Button}, {X}, {Y})",
            InputEventType.MouseWheel => $"MouseWheel(Delta={WheelDelta}, {X}, {Y})",
            InputEventType.KeyDown => $"KeyDown(KeyCode={KeyCode:X}, KeySym={KeySym:X})",
            InputEventType.KeyUp => $"KeyUp(KeyCode={KeyCode:X}, KeySym={KeySym:X})",
            _ => $"Unknown({Type})"
        };
    }
}
