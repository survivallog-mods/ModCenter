using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;

namespace SurvivalLog.ModCenter;

/// <summary>
/// pending 契约（ModCenter v2 生命周期任务 2 定稿；T16 起只在插件项目直编——单份，
/// 无 MCCleaner link 编译）：<c>BepInEx\ModCenter\pending.json</c> 是
/// 插件（写意图）与启动清理（CleanupRunner，由引导壳 MCShell 唤起）之间的唯一介质，spec §3。
/// JSON 手写纪律（Payload.cs 同款，AOT/补丁器阶段零外部依赖）：仅对象/数组/字符串/
/// 数字/布尔；字符串转义 \" \\ \n（另支持 \r \t 与 &lt;0x20 控制字符的 \uXXXX）；
/// 读写均 UTF-8 无 BOM（new UTF8Encoding(false)）。
/// </summary>
public static class McPending
{
    /// <summary>op 类型合法值（Op.Type）。</summary>
    public const string TypeDelete = "delete";
    public const string TypeInstall = "install";
    public const string TypeReplace = "replace";
    public const string TypeDisable = "disable";
    public const string TypeEnable = "enable";

    /// <summary>pending.json 绝对路径：BepInEx 根\ModCenter\pending.json（运行期由 Paths 解析，非编译期常量）。</summary>
    public static readonly string File = Path.Combine(Paths.BepInExRootPath, "ModCenter", "pending.json");

    /// <summary>暂存目录绝对路径：BepInEx 根\ModCenter\staging（不带尾分隔符）。</summary>
    public static readonly string StagingDir = Path.Combine(Paths.BepInExRootPath, "ModCenter", "staging");

    /// <summary>本类内 <see cref="File"/> 遮蔽 System.IO.File，IO 调用须全限定。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 单条意图（spec §3）。Target/Staged 均为 BepInEx 根相对路径
    /// （'/' 或 '\' 分隔均可，执行侧统一归一为 '\' 后 Path.Combine，等价 -LiteralPath 语义）。
    /// </summary>
    public sealed class Op
    {
        public string Type;   // delete | install | replace | disable | enable
        public string Target; // 目标文件（BepInEx 根相对路径）
        public string Staged; // install/replace 的来源文件（通常位于 StagingDir 下）
        public string Md5;    // staged 的 MD5 十六进制（大小写不敏感；install/replace 必填）
    }

    /// <summary>读 ops：文件不在场或无 "ops" 键 → 空表；格式坏抛异常（调用方决定保留/修复）。</summary>
    public static List<Op> Load(string path)
    {
        var ops = new List<Op>();
        if (!System.IO.File.Exists(path)) return ops;
        var root = ParseRoot(path);
        if (root.TryGetValue("ops", out var raw) && raw is List<object> arr)
        {
            foreach (var e in arr)
            {
                if (e is not Dictionary<string, object> o)
                    throw new InvalidDataException("ops 数组元素不是对象");
                ops.Add(new Op
                {
                    Type = GetStr(o, "op"),
                    Target = GetStr(o, "target"),
                    Staged = GetStr(o, "staged"),
                    Md5 = GetStr(o, "md5"),
                });
            }
        }
        return ops;
    }

    /// <summary>读 order（顺序覆盖清单，T3/T9 消费）：文件不在场或无 "order" 键 → 空表。</summary>
    public static List<string> LoadOrder(string path)
    {
        var order = new List<string>();
        if (!System.IO.File.Exists(path)) return order;
        var root = ParseRoot(path);
        if (root.TryGetValue("order", out var raw) && raw is List<object> arr)
        {
            foreach (var e in arr)
            {
                if (e is not string s)
                    throw new InvalidDataException("order 数组含非字符串");
                order.Add(s);
            }
        }
        return order;
    }

    /// <summary>
    /// 原子写：先写 path+".tmp" 再 File.Replace（目标在场）/ File.Move（首建）落位；
    /// order=null 时省略 order 键（重写时调用方应回传 LoadOrder 结果以免丢失 T3 的顺序覆盖）。
    /// </summary>
    public static void Save(string path, List<Op> ops, List<string> order)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"ops\":[");
        if (ops != null)
        {
            for (var i = 0; i < ops.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var op = ops[i];
                sb.Append("{\"op\":").Append(Str(op.Type ?? ""))
                  .Append(",\"target\":").Append(Str(op.Target ?? ""));
                if (!string.IsNullOrEmpty(op.Staged)) sb.Append(",\"staged\":").Append(Str(op.Staged));
                if (!string.IsNullOrEmpty(op.Md5)) sb.Append(",\"md5\":").Append(Str(op.Md5));
                sb.Append('}');
            }
        }
        sb.Append(']');
        if (order != null)
        {
            sb.Append(",\"order\":[");
            for (var i = 0; i < order.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Str(order[i] ?? ""));
            }
            sb.Append(']');
        }
        sb.Append('}');

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
        if (System.IO.File.Exists(path)) System.IO.File.Replace(tmp, path, null);
        else System.IO.File.Move(tmp, path);
    }

    private static Dictionary<string, object> ParseRoot(string path)
    {
        var v = Parser.Parse(System.IO.File.ReadAllText(path, Utf8NoBom));
        return v as Dictionary<string, object>
               ?? throw new InvalidDataException("JSON 根不是对象");
    }

    private static string GetStr(Dictionary<string, object> o, string key) =>
        o.TryGetValue(key, out var v) && v is string s ? s : null;

    /// <summary>字符串转义（Payload.JsonW.Str 同款：\" \\ \n \r \t + 控制字符 \uXXXX）。</summary>
    internal static string Str(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// 极简手写 JSON 解析器（零外部依赖，补丁器阶段不依赖 System.Text.Json）：
    /// 递归下降，产出 Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// </summary>
    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        private Parser(string s) { _s = s; }

        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.Ws();
            var v = p.Value();
            p.Ws();
            if (p._i != p._s.Length) throw p.Err("尾部多余字符");
            return v;
        }

        private object Value()
        {
            if (_i >= _s.Length) throw Err("意外结束");
            switch (_s[_i])
            {
                case '{': return Object();
                case '[': return Array();
                case '"': return Str();
                case 't': Lit("true"); return true;
                case 'f': Lit("false"); return false;
                case 'n': Lit("null"); return null;
                default: return Num();
            }
        }

        private Dictionary<string, object> Object()
        {
            var d = new Dictionary<string, object>();
            _i++; Ws();
            if (Eat('}')) return d;
            while (true)
            {
                Ws();
                if (_i >= _s.Length || _s[_i] != '"') throw Err("期望键字符串");
                var k = Str();
                Ws();
                if (!Eat(':')) throw Err("期望 ':'");
                Ws();
                d[k] = Value();
                Ws();
                if (Eat(',')) continue;
                if (Eat('}')) return d;
                throw Err("期望 ',' 或 '}'");
            }
        }

        private List<object> Array()
        {
            var l = new List<object>();
            _i++; Ws();
            if (Eat(']')) return l;
            while (true)
            {
                Ws();
                l.Add(Value());
                Ws();
                if (Eat(',')) continue;
                if (Eat(']')) return l;
                throw Err("期望 ',' 或 ']'");
            }
        }

        private string Str()
        {
            var sb = new StringBuilder();
            _i++; // 开引号
            while (_i < _s.Length)
            {
                var c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (_i >= _s.Length) throw Err("转义意外结束");
                var e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length) throw Err("\\u 转义不完整");
                        int cp;
                        try { cp = Convert.ToInt32(_s.Substring(_i, 4), 16); }
                        catch (Exception) { throw Err("非法 \\u 转义"); }
                        sb.Append((char)cp);
                        _i += 4;
                        break;
                    default:
                        throw Err("非法转义 \\" + e);
                }
            }
            throw Err("字符串未闭合");
        }

        private double Num()
        {
            var st = _i;
            while (_i < _s.Length && "-+.eE0123456789".IndexOf(_s[_i]) >= 0) _i++;
            return _i == st || !double.TryParse(_s.Substring(st, _i - st), NumberStyles.Float,
                       CultureInfo.InvariantCulture, out var v)
                ? throw Err("非法数字")
                : v;
        }

        private void Lit(string lit)
        {
            if (_i + lit.Length > _s.Length ||
                string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0)
                throw Err("非法字面量");
            _i += lit.Length;
        }

        private void Ws()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
        }

        private bool Eat(char c)
        {
            if (_i >= _s.Length || _s[_i] != c) return false;
            _i++;
            return true;
        }

        private Exception Err(string why) => new InvalidDataException("JSON 位置 " + _i + ": " + why);
    }
}
