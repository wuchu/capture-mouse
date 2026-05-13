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

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public ushort usButtonFlags;
        public ushort usButtonData;
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

    [DllImport("user32.dll")]
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

    // 鼠标按钮标志
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

    private bool _isCapturing = false;
    private bool _disposed = false;
    private DateTime _lastMouseMove = DateTime.MinValue;
    private readonly TimeSpan _mouseThrottle = TimeSpan.FromMilliseconds(8); // ~120fps

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
    public bool CaptureMouse { get; set; } = true;

    /// <summary>
    /// 是否捕获键盘
    /// </summary>
    public bool CaptureKeyboard { get; set; } = true;

    /// <summary>
    /// 开始捕获
    /// </summary>
    public bool StartCapture()
    {
        if (_isCapturing) return true;

        try
        {
            // 创建消息窗口
            CreateHandle(new CreateParams
            {
                ExStyle = 0x80, // WS_EX_TOOLWINDOW ( invisible )
                Style = 0x80000000 // WS_POPUP
            });

            var devices = new List<RAWINPUTDEVICE>();

            // 注册鼠标设备
            if (CaptureMouse)
            {
                devices.Add(new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01, // Generic Desktop
                    usUsage = 0x02,     // Mouse
                    dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                    hwndTarget = this.Handle
                });
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
            }

            if (devices.Count == 0) return false;

            var result = RegisterRawInputDevices(
                devices.ToArray(),
                (uint)devices.Count,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            if (!result)
            {
                Console.WriteLine("注册 Raw Input 设备失败");
                return false;
            }

            _isCapturing = true;
            Console.WriteLine("输入捕获已启动");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启动捕获失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 停止捕获
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        _isCapturing = false;

        // 注销设备
        var devices = new RAWINPUTDEVICE[]
        {
            new() { usUsagePage = 0x01, usUsage = 0x02, dwFlags = 0, hwndTarget = IntPtr.Zero },
            new() { usUsagePage = 0x01, usUsage = 0x06, dwFlags = 0, hwndTarget = IntPtr.Zero }
        };

        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        Console.WriteLine("输入捕获已停止");
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
                Console.WriteLine("输入设备变更");
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

        // 处理鼠标移动
        if ((mouse.usFlags & 0x01) == 0) // 不是绝对模式
        {
            // 节流鼠标移动事件
            if (DateTime.Now - _lastMouseMove < _mouseThrottle)
            {
                // 仍然更新位置，但不触发事件
            }
            else
            {
                _lastMouseMove = DateTime.Now;

                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseMove,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y
                });
            }
        }

        // 处理鼠标按键
        if (mouse.usButtonFlags != 0)
        {
            // 左键
            if ((mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            {
                InputReceived?.Invoke(this, new InputEvent
                {
                    Type = InputEventType.MouseButtonDown,
                    Timestamp = InputEvent.GetTimestamp(),
                    X = pt.X,
                    Y = pt.Y,
                    Button = 1
                });
            }
            if ((mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) != 0)
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
            if ((mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
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
            if ((mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) != 0)
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
            if ((mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
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
            if ((mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
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
            if ((mouse.usButtonFlags & RI_MOUSE_WHEEL) != 0)
            {
                short wheelDelta = (short)mouse.usButtonData;
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
        DestroyHandle();

        _disposed = true;
    }
}
