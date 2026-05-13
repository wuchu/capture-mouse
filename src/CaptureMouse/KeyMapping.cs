namespace CaptureMouse;

/// <summary>
/// Windows 虚拟键码到 X11 KeySym 的映射
/// 参考: https://www.x.org/releases/X11R7.7/doc/xproto/x11protocol.html#keysym_encoding
/// </summary>
public static class KeyMapping
{
    /// <summary>
    /// 将 Windows 虚拟键码转换为 X11 KeySym
    /// </summary>
    /// <param name="vkCode">Windows 虚拟键码</param>
    /// <param name="isExtended">是否是扩展键 (RI_KEY_E0 标志)</param>
    public static uint ToKeySym(ushort vkCode, bool isExtended)
    {
        // 字母 A-Z -> 小写 a-z (X11 KeySym 使用小写)
        if (vkCode >= 0x41 && vkCode <= 0x5A)
            return (uint)(vkCode + 0x20);

        // 数字 0-9 (主键盘)
        if (vkCode >= 0x30 && vkCode <= 0x39)
            return (uint)vkCode;

        // 功能键 F1-F12
        if (vkCode >= 0x70 && vkCode <= 0x7B)
            return (uint)(0xFFBE + (vkCode - 0x70));

        // 扩展键的特殊处理 (RI_KEY_E0 标志)
        // 某些键在扩展模式下有不同映射
        if (isExtended)
        {
            switch (vkCode)
            {
                case 0x0D: return 0xFF8D;  // Numpad Enter (扩展) -> XK_KP_Enter
                case 0x6F: return 0xFFAF;  // Numpad Divide (扩展) -> XK_KP_Divide
                // 方向键和编辑键在扩展模式下 VK 码相同，映射不变
            }
        }

        // 非扩展键的默认映射
        return vkCode switch
        {
            // 编辑键
            0x08 => 0xFF08,    // Backspace -> XK_BackSpace
            0x09 => 0xFF09,    // Tab -> XK_Tab
            0x0D => 0xFF0D,    // Enter -> XK_Return
            0x1B => 0xFF1B,    // Escape -> XK_Escape
            0x20 => 0x0020,    // Space -> XK_space
            0x2D => 0xFF63,    // Insert -> XK_Insert
            0x2E => 0xFFFF,    // Delete -> XK_Delete
            0x24 => 0xFF50,    // Home -> XK_Home
            0x23 => 0xFF57,    // End -> XK_End
            0x21 => 0xFF55,    // Page Up -> XK_Page_Up
            0x22 => 0xFF56,    // Page Down -> XK_Page_Down

            // 方向键
            0x25 => 0xFF51,    // Left Arrow -> XK_Left
            0x26 => 0xFF52,    // Up Arrow -> XK_Up
            0x27 => 0xFF53,    // Right Arrow -> XK_Right
            0x28 => 0xFF54,    // Down Arrow -> XK_Down

            // 其他特殊键
            0x2C => 0xFF61,    // Print Screen -> XK_Print
            0x91 => 0xFF13,    // Scroll Lock -> XK_Scroll_Lock
            0x13 => 0xFF14,    // Pause -> XK_Pause

            // 数字键盘
            0x60 => 0xFFB0,    // Numpad 0 -> XK_KP_0
            0x61 => 0xFFB1,    // Numpad 1 -> XK_KP_1
            0x62 => 0xFFB2,    // Numpad 2 -> XK_KP_2
            0x63 => 0xFFB3,    // Numpad 3 -> XK_KP_3
            0x64 => 0xFFB4,    // Numpad 4 -> XK_KP_4
            0x65 => 0xFFB5,    // Numpad 5 -> XK_KP_5
            0x66 => 0xFFB6,    // Numpad 6 -> XK_KP_6
            0x67 => 0xFFB7,    // Numpad 7 -> XK_KP_7
            0x68 => 0xFFB8,    // Numpad 8 -> XK_KP_8
            0x69 => 0xFFB9,    // Numpad 9 -> XK_KP_9
            0x6A => 0xFFAA,    // Multiply -> XK_KP_Multiply
            0x6B => 0xFFAB,    // Add -> XK_KP_Add
            0x6C => 0xFFAC,    // Separator -> XK_KP_Separator
            0x6D => 0xFFAD,    // Subtract -> XK_KP_Subtract
            0x6E => 0xFFAE,    // Decimal -> XK_KP_Decimal
            0x6F => 0xFFAF,    // Divide -> XK_KP_Divide

            // 修饰键 (通用 VK 码)
            0x10 => 0xFFE1,    // Shift -> XK_Shift_L
            0x11 => 0xFFE3,    // Control -> XK_Control_L
            0x12 => 0xFFE9,    // Alt -> XK_Alt_L

            // 修饰键 (具体左/右 VK 码)
            0xA0 => 0xFFE1,    // Shift Left -> XK_Shift_L
            0xA1 => 0xFFE2,    // Shift Right -> XK_Shift_R
            0xA2 => 0xFFE3,    // Control Left -> XK_Control_L
            0xA3 => 0xFFE4,    // Control Right -> XK_Control_R
            0xA4 => 0xFFE9,    // Alt Left -> XK_Alt_L
            0xA5 => 0xFFEA,    // Alt Right -> XK_Alt_R

            // Windows 键
            0x5B => 0xFFEB,    // Windows Left -> XK_Super_L
            0x5C => 0xFFEC,    // Windows Right -> XK_Super_R
            0x5D => 0xFF67,    // Menu -> XK_Menu

            // 符号键 (US 键盘布局)
            0xBA => 0x003B,    // ;: -> semicolon
            0xBB => 0x003D,    // =+ -> equal
            0xBC => 0x002C,    // ,< -> comma
            0xBD => 0x002D,    // -_ -> minus
            0xBE => 0x002E,    // .> -> period
            0xBF => 0x002F,    // /? -> slash
            0xC0 => 0x0060,    // `~ -> grave
            0xDB => 0x005B,    // [{ -> bracketleft
            0xDC => 0x005C,    // \| -> backslash
            0xDD => 0x005D,    // ]} -> bracketright
            0xDE => 0x0027,    // '" -> apostrophe

            // Lock 键
            0x14 => 0xFFE5,    // Caps Lock -> XK_Caps_Lock
            0x90 => 0xFF7F,    // Num Lock -> XK_Num_Lock

            // 默认返回虚拟键码（未知键）
            _ => vkCode
        };
    }

    /// <summary>
    /// 检查是否是修饰键
    /// </summary>
    public static bool IsModifierKey(ushort vkCode)
    {
        return vkCode is 0x10 or 0xA0 or 0xA1 or   // Shift
                    0x11 or 0xA2 or 0xA3 or   // Control
                    0x12 or 0xA4 or 0xA5 or   // Alt
                    0x5B or 0x5C;            // Windows
    }

    /// <summary>
    /// 获取修饰键的 KeySym
    /// </summary>
    public static uint GetModifierKeySym(ushort vkCode, bool isLeft)
    {
        return vkCode switch
        {
            0x10 or 0xA0 or 0xA1 => isLeft ? 0xFFE1u : 0xFFE2u,  // Shift
            0x11 or 0xA2 or 0xA3 => isLeft ? 0xFFE3u : 0xFFE4u,  // Control
            0x12 or 0xA4 or 0xA5 => isLeft ? 0xFFE9u : 0xFFEAu,  // Alt
            0x5B => 0xFFEBu,  // Windows Left
            0x5C => 0xFFECu,  // Windows Right
            _ => 0
        };
    }
}