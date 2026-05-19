using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CaptureMouse;

/// <summary>
/// Windows Raw Input 输入捕获
/// </summary>
public class InputCapture : NativeWindow, IDisposable
{
    #region Win32 API

    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_DEVNOTIFY = 0x00002000;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    // RAWMOUSE 的正确结构定义
    // usButtonFlags 和 usButtonData 是 union 的一部分
    // 参考: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        // union { ULONG ulButtons; struct { USHORT usButtonFlags; USHORT usButtonData; }; }
        public uint ulButtons;          // 整个 union 作为 ULONG
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWINPUTDATA data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWINPUTDATA
    {
        [FieldOffset(0)]
        public RAWMOUSE mouse;
        [FieldOffset(0)]
        public RAWKEYBOARD keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern int GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // 鼠标按钮标志 (从 ulButtons 的低16位提取)
    private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    private const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    private const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    private const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    private const ushort RI_MOUSE_WHEEL = 0x0400;

    // 键盘标志
    private const ushort RI_KEY_MAKE = 0;
    private const ushort RI_KEY_BREAK = 1;
    private const ushort RI_KEY_E0 = 2;
    private const ushort RI_KEY_E1 = 4;

    #endregion

    private volatile bool _isCapturing = false;
    private bool _disposed = false;
    private DateTime _lastMouseMove = DateTime.MinValue;
    private readonly TimeSpan _mouseThrottle = TimeSpan.FromMilliseconds(8); // ~120fps

    // 当前鼠标位置（用于 VNC 坐标映射）
    private int _currentMouseX;
    private int _currentMouseY;

    /// <summary>
    /// 输入事件
    /// </summary>
    public event EventHandler<InputEvent>? InputReceived;

    /// <summary>
    /// 是否正在捕获
    /// </summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// 是否捕获鼠标
    /// </summary>
    public bool CaptureMouseInput { get; set; } = true;

    /// <summary>
    /// 是否捕获键盘
    /// </summary>
    public bool CaptureKeyboard { get; set; } = true;

    /// <summary>
    /// 获取当前鼠标 X 坐标
    /// </summary>
    public int CurrentMouseX => _currentMouseX;

    /// <summary>
    /// 获取当前鼠标 Y 坐标
    /// </summary>
    public int CurrentMouseY => _currentMouseY;

    /// <summary>
    /// 开始捕获
    /// </summary>
    public bool StartCapture()
    {
        if (_isCapturing) return true;

        Logger.Info("启动输入捕获...");

        try
        {
            // 创建消息窗口
            CreateHandle(new CreateParams
            {
                ExStyle = 0x80, // WS_EX_TOOLWINDOW (invisible)
                Style = unchecked((int)0x80000000) // WS_POPUP
            });
            Logger.Debug($"消息窗口已创建, Handle={Handle}");

            var devices = new List<RAWINPUTDEVICE>();

            // 注册鼠标设备
            if (CaptureMouseInput)
            {
                devices.Add(new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, // Generic Desktop
                    usUsage = 0x02,     // Mouse
                    dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                    hwndTarget = this.Handle
                });
                Logger.Debug("已注册鼠标设备");
            }

            // 注册键盘设备
            if (CaptureKeyboard)
            {
                devices.Add(new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, // Generic Desktop
                    usUsage = 0x06,     // Keyboard
                    dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                    hwndTarget = this.Handle
                });
                Logger.Debug("已注册键盘设备");
            }

            if (devices.Count == 0)
            {
                Logger.Warning("没有注册任何输入设备");
                return false;
            }

            var result = RegisterRawInputDevices(
                devices.ToArray(),
                (uint)devices.Count,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            if (!result)
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Error($"注册 Raw Input 设备失败, Win32 错误码: {error}");
                DestroyHandle();
                return false;
            }

            _isCapturing = true;
            Logger.Info("输入捕获已启动");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("启动捕获失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 停止捕获
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        Logger.Info("停止输入捕获...");
        _isCapturing = false;

        // 注销设备 - 使用 RIDEV_REMOVE 标志
        var devices = new List<RAWINPUTDEVICE>();

        if (CaptureMouseInput)
        {
            devices.Add(new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            });
        }

        if (CaptureKeyboard)
        {
            devices.Add(new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            });
        }

        if (devices.Count > 0)
        {
            RegisterRawInputDevices(devices.ToArray(), (uint)devices.Count, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }

        Logger.Info("输入捕获已停止");
    }

    /// <summary>
    /// 处理 Windows 消息
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_INPUT = 0x00FF;
        const int WM_INPUT_DEVICE_CHANGE = 0x00FE;

        switch (m.Msg)
        {
            case WM_INPUT:
                if (_isCapturing)
                {
                    ProcessRawInput(m.LParam);
                }
                break;

            case WM_INPUT_DEVICE_CHANGE:
                Logger.Debug("输入设备变更");
                break;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// 处理 Raw Input 数据
    /// </summary>
    private void ProcessRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());

        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == size)
            {
                var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);

                switch (raw.header.dwType)
                {
                    case RIM_TYPEMOUSE:
                        ProcessMouseInput(raw.data.mouse);
                        break;

                    case RIM_TYPEKEYBOARD:
                        ProcessKeyboardInput(raw.data.keyboard);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("处理 RawInput 失败", ex);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// 处理鼠标输入
    /// </summary>
    private void ProcessMouseInput(RAWMOUSE mouse)
    {
        // 获取当前鼠标位置
        GetCursorPos(out var pt);
        _currentMouseX = pt.X;
        _currentMouseY = pt.Y;

        // 从 ulButtons 提取 usButtonFlags (低16位) 和 usButtonData (高16位)
        ushort buttonFlags = (ushort)(mouse.ulButtons & 0xFFFF);
        ushort buttonData = (ushort)((mouse.ulButtons >> 16) & 0xFFFF);

        // 处理鼠标移动
        if ((mouse.usFlags & 0x01) == 0) // 不是绝对模式
        {
            // 节流鼠标移动事件
            if (DateTime.Now - _lastMouseMove >= _mouseThrottle)
            {
                _lastMouseMove = DateTime.Now;

                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseMove,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                });
            }
        }

        // 处理鼠标按键
        if (buttonFlags != 0)
        {
            // 左键
            if ((buttonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonDown,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 1  // VNC 左键 = 1
                });
            }
            if ((buttonFlags & RI_MOUSE_LEFT_BUTTON_UP) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonUp,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 1
                });
            }

            // 右键
            if ((buttonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonDown,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 4  // VNC 右键 = 4
                });
            }
            if ((buttonFlags & RI_MOUSE_RIGHT_BUTTON_UP) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonUp,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 4
                });
            }

            // 中键
            if ((buttonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonDown,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 2  // VNC 中键 = 2
                });
            }
            if ((buttonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonUp,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 2
                });
            }

            // 滚轮
            if ((buttonFlags & RI_MOUSE_WHEEL) != 0)
            {
                short wheelDelta = (short)buttonData;
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseWheel,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    WheelDelta = wheelDelta
                });
            }
        }
    }

    /// <summary>
    /// 处理键盘输入
    /// </summary>
    private void ProcessKeyboardInput(RAWKEYBOARD keyboard)
    {
        bool isExtended = (keyboard.Flags & RI_KEY_E0) != 0;
        bool isBreak = (keyboard.Flags & RI_KEY_BREAK) != 0;

        var keySym = KeyMapping.ToKeySym(keyboard.VKey, isExtended);

        InputReceived?.Invoke(this, new InputEvent
        {
            Type = isBreak ? InputEventType.KeyUp : InputEventType.KeyDown,
            Timestamp = InputEvent.GetTimestamp(),
            KeyCode = keyboard.VKey,
            KeySym = keySym,
            IsExtended = isExtended
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopCapture();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }

        _disposed = true;
    }
}