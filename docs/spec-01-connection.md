# 迭代一 Spec：基础连接层

## 目标
建立 Windows 到 macOS 的基础 VNC 连接，实现键鼠事件的发送能力。

## 功能需求

### 1. VNC 协议握手
- [ ] 实现 RFB 3.8 协议版本协商
- [ ] 支持 VNC 密码认证（DES 加密）
- [ ] 处理服务器初始化消息

### 2. 连接管理
- [ ] 建立 TCP 连接到 macOS:5900
- [ ] 连接状态管理（连接中、已连接、断开）
- [ ] 基础错误处理

### 3. 键鼠事件发送
- [ ] 实现鼠标移动事件发送
- [ ] 实现鼠标按键事件发送
- [ ] 实现键盘按键事件发送

## 技术细节

### VNC 协议流程

```
Client                           Server
  │                                │
  ├──── ProtocolVersion ─────────►│
  │                                │
  │◄───────── "RFB 003.008" ─────┤
  │                                │
  ├──── SecurityTypes ───────────►│
  │                                │
  │◄───────── SecurityResult ────┤
  │                                │
  ├──── ClientInit ─────────────►│
  │                                │
  │◄───────── ServerInit ────────┤
  │                                │
  ├──── SetPixelFormat ──────────►│
  ├──── SetEncodings ────────────►│
  │                                │
  ═════ 连接建立，可以发送事件 ═════
```

### 关键消息格式

#### 鼠标事件 (Client -> Server)
```
+--------------+--------------+--------------+
|  MessageType |    Button    |      X       |
|   (1 byte)   |   (1 byte)   |   (2 bytes)  |
+--------------+--------------+--------------+
|                    Y                       |
|                 (2 bytes)                  |
+-------------------------------------------+
```

#### 键盘事件 (Client -> Server)
```
+--------------+--------------+--------------+
|  MessageType |  DownFlag    |     Key      |
|   (1 byte)   |   (1 byte)   |   (4 bytes)  |
+--------------+--------------+--------------+
```

### macOS VNC 特性

- 端口：5900 (标准 VNC)
- 认证：VNC 密码（DES 加密）
- 坐标系：绝对坐标，范围 0-65535

## 接口设计

```csharp
public class VncClient
{
    // 事件
    public event EventHandler<bool> ConnectionStateChanged;
    public event EventHandler<string> ErrorOccurred;
    
    // 连接管理
    public bool IsConnected { get; }
    public async Task<bool> ConnectAsync(string host, int port, string password);
    public void Disconnect();
    
    // 输入发送
    public void SendMouseMove(int x, int y);
    public void SendMouseButton(int button, bool pressed);
    public void SendKey(uint keySym, bool pressed);
}
```

## 测试验证

### 测试步骤
1. 在 macOS 开启屏幕共享，设置密码
2. 运行程序，输入 macOS IP 和密码
3. 验证连接成功
4. 发送测试鼠标移动，观察 macOS 光标移动
5. 发送测试按键，观察 macOS 响应

### 预期结果
- [ ] 成功连接到 macOS
- [ ] 鼠标移动事件正确转发
- [ ] 键盘事件正确转发

## 交付物

- [ ] `VncClient.cs` - VNC 客户端实现
- [ ] `Program.cs` - 测试程序入口
- [ ] `.csproj` - 项目文件

## 时间估算

2-3 天

## 依赖

- .NET 9 SDK
- macOS 测试设备（开启屏幕共享）
