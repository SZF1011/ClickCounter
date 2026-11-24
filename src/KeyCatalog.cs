using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyRank
{
    internal enum HitKind
    {
        Keyboard = 0,
        Mouse = 1,
        Wheel = 2
    }

    internal struct KeyHit
    {
        public readonly string Id;
        public readonly HitKind Kind;

        public KeyHit(string id, HitKind kind)
        {
            Id = id;
            Kind = kind;
        }
    }

    internal static class KeyCatalog
    {
        static readonly Dictionary<string, string> labels = new Dictionary<string, string>();
        static readonly Dictionary<int, string> vkIds = new Dictionary<int, string>();
        static readonly object gate = new object();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetKeyNameTextW(int lParam, StringBuilder lpString, int nSize);

        static KeyCatalog()
        {
            for (int c = 'A'; c <= 'Z'; c++) labels["K_" + (char)c] = ((char)c).ToString();
            for (int d = 0; d <= 9; d++) labels["K_" + d] = d.ToString();
            for (int i = 1; i <= 24; i++) labels["K_F" + i] = "F" + i;
            for (int i = 0; i <= 9; i++) labels["K_NUM" + i] = "小键盘 " + i;

            labels["M_L"] = "鼠标左键";
            labels["M_R"] = "鼠标右键";
            labels["M_WU"] = "滚轮上";
            labels["M_WD"] = "滚轮下";
            labels["M_WL"] = "滚轮左";
            labels["M_WR"] = "滚轮右";

            Add(0x08, "K_BACKSPACE", "退格");
            Add(0x09, "K_TAB", "Tab");
            Add(0x0D, "K_ENTER", "回车");
            Add(0x13, "K_PAUSE", "Pause");
            Add(0x14, "K_CAPS", "大写锁定");
            Add(0x1B, "K_ESC", "Esc");
            Add(0x20, "K_SPACE", "空格");
            Add(0x21, "K_PGUP", "上翻页");
            Add(0x22, "K_PGDN", "下翻页");
            Add(0x23, "K_END", "End");
            Add(0x24, "K_HOME", "Home");
            Add(0x25, "K_LEFT", "←");
            Add(0x26, "K_UP", "↑");
            Add(0x27, "K_RIGHT", "→");
            Add(0x28, "K_DOWN", "↓");
            Add(0x2C, "K_PRTSC", "截屏");
            Add(0x2D, "K_INS", "插入");
            Add(0x2E, "K_DEL", "删除");
            Add(0x5D, "K_MENU", "菜单键");
            Add(0x6A, "K_NUMMUL", "小键盘 *");
            Add(0x6B, "K_NUMPLUS", "小键盘 +");
            Add(0x6D, "K_NUMMINUS", "小键盘 -");
            Add(0x6E, "K_NUMDOT", "小键盘 .");
            Add(0x6F, "K_NUMDIV", "小键盘 /");
            Add(0x90, "K_NUMLOCK", "NumLock");
            Add(0x91, "K_SCROLLLOCK", "ScrollLock");
            Add(0xBA, "K_SEMI", "; :");
            Add(0xBB, "K_EQUAL", "= +");
            Add(0xBC, "K_COMMA", ", <");
            Add(0xBD, "K_HYPHEN", "- _");
            Add(0xBE, "K_PERIOD", ". >");
            Add(0xBF, "K_SLASH", "/ ?");
            Add(0xC0, "K_BACKTICK", "` ~");
            Add(0xDB, "K_LBRACKET", "[ {");
            Add(0xDC, "K_BACKSLASH", "\\ |");
            Add(0xDD, "K_RBRACKET", "] }");
            Add(0xDE, "K_QUOTE", "' \"");

            labels["K_LSHIFT"] = "左 Shift";
            labels["K_RSHIFT"] = "右 Shift";
            labels["K_LCTRL"] = "左 Ctrl";
            labels["K_RCTRL"] = "右 Ctrl";
            labels["K_LALT"] = "左 Alt";
            labels["K_RALT"] = "右 Alt";
            labels["K_LWIN"] = "左 Win";
            labels["K_RWIN"] = "右 Win";
            labels["K_NUMENTER"] = "小键盘 回车";
        }

        static void Add(int vk, string id, string label)
        {
            vkIds[vk] = id;
            labels[id] = label;
        }

        public static HitKind KindOf(string id)
        {
            if (id == null) return HitKind.Keyboard;
            if (id == "M_WU" || id == "M_WD" || id == "M_WL" || id == "M_WR") return HitKind.Wheel;
            if (id.StartsWith("M_", StringComparison.Ordinal)) return HitKind.Mouse;
            return HitKind.Keyboard;
        }

        public static string Label(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            lock (gate)
            {
                string s;
                if (labels.TryGetValue(id, out s)) return s;
            }
            return id;
        }

        public static string FromMouse(int button)
        {
            if (button == 1) return "M_L";
            if (button == 2) return "M_R";
            return null;
        }

        public static string FromKeyboard(int vk, int scan, int flags)
        {
            bool ext = (flags & 0x01) != 0;

            if (vk <= 0 || vk == 0xFF) return null;

            switch (vk)
            {
                case 0xA0: return "K_LSHIFT";
                case 0xA1: return "K_RSHIFT";
                case 0xA2: return "K_LCTRL";
                case 0xA3: return "K_RCTRL";
                case 0xA4: return "K_LALT";
                case 0xA5: return "K_RALT";
                case 0x10: return scan == 0x36 ? "K_RSHIFT" : "K_LSHIFT";
                case 0x11: return ext ? "K_RCTRL" : "K_LCTRL";
                case 0x12: return ext ? "K_RALT" : "K_LALT";
                case 0x5B: return "K_LWIN";
                case 0x5C: return "K_RWIN";
            }

            if (vk >= 0x41 && vk <= 0x5A) return "K_" + (char)vk;
            if (vk >= 0x30 && vk <= 0x39) return "K_" + (char)vk;
            if (vk >= 0x70 && vk <= 0x87) return "K_F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69) return "K_NUM" + (vk - 0x60);

            if (vk == 0x0D && ext) return "K_NUMENTER";
            if (vk == 0x2D && ext) return "K_NUMMINUS";
            if (vk == 0x2B && ext) return "K_NUMPLUS";

            string id;
            if (vkIds.TryGetValue(vk, out id)) return id;

            return UnknownId(vk, scan, ext);
        }

        static string UnknownId(int vk, int scan, bool ext)
        {
            string id = "K_VK" + vk.ToString("X2");
            lock (gate)
            {
                if (!labels.ContainsKey(id))
                {
                    string name = null;
                    try
                    {
                        StringBuilder sb = new StringBuilder(64);
                        int lp = (scan << 16) | (ext ? (1 << 24) : 0);
                        if (GetKeyNameTextW(lp, sb, sb.Capacity) > 0) name = sb.ToString();
                    }
                    catch { }
                    if (string.IsNullOrEmpty(name)) name = "VK " + vk.ToString("X2");
                    labels[id] = name;
                }
            }
            return id;
        }
    }
}
