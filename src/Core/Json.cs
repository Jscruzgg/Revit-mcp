// Minimal JSON (from Claude Revit Bridge): System.Web.Extensions does not exist on .NET 8/10.
// Parse: object -> Dictionary<string, object>, array -> object[], string, bool, null, number -> int, long or double.
// Write: null, string, bool, numbers, IDictionary<string, object>, any other IEnumerable as an array, anything else as its text.
using System;
using System.Collections.Generic;
using System.Text;

namespace RevitMultiBridge
{
    static class Json
    {
        public static object Parse(string s)
        {
            if (s == null) throw new FormatException("JSON: no text");
            int i = 0;
            object v = Value(s, ref i, 0);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("JSON: extra text at " + i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

        static void Expect(string s, ref int i, char c)
        {
            Ws(s, ref i);
            if (i >= s.Length || s[i] != c) throw new FormatException("JSON: '" + c + "' expected at " + i);
            i++;
        }

        // depth limit like JavaScriptSerializer (RecursionLimit 100): a deeply nested body must not overflow the stack and close Revit
        static object Value(string s, ref int i, int depth)
        {
            if (depth > 100) throw new FormatException("JSON: nested deeper than 100");
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length) throw new FormatException("JSON: key expected at " + i);
                    string k = s[i] == '"' || s[i] == '\'' ? Str(s, ref i) : Key(s, ref i);
                    Expect(s, ref i, ':');
                    d[k] = Value(s, ref i, depth + 1);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return d;
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                while (true)
                {
                    l.Add(Value(s, ref i, depth + 1));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return l.ToArray();
                }
            }
            if (c == '"' || c == '\'') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        static bool Word(string s, ref int i, string w)
        {
            if (i + w.Length > s.Length || string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static object Num(string s, ref int i)
        {
            int a = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string n = s.Substring(a, i - a);
            if (n.Length == 0) throw new FormatException("JSON: unexpected '" + s[a] + "' at " + a);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (n.IndexOfAny(new[] { '.', 'e', 'E' }) < 0)
            {
                int iv; long lv;
                if (int.TryParse(n, System.Globalization.NumberStyles.AllowLeadingSign, inv, out iv)) return iv;
                if (long.TryParse(n, System.Globalization.NumberStyles.AllowLeadingSign, inv, out lv)) return lv;
            }
            double dv;
            if (double.TryParse(n, System.Globalization.NumberStyles.Float, inv, out dv)) return dv;
            throw new FormatException("JSON: bad number '" + n + "' at " + a);
        }

        // unquoted key, as JavaScriptSerializer took it ({tab: "Claude"})
        static string Key(string s, ref int i)
        {
            int a = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) i++;
            if (i == a) throw new FormatException("JSON: key expected at " + a);
            return s.Substring(a, i - a);
        }

        static string Str(string s, ref int i)
        {
            char q = s[i++];   // opening quote: " or ' (JavaScriptSerializer took both)
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON: unterminated string");
                char c = s[i++];
                if (c == q) return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON: unterminated string");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON: bad \\u escape at " + i);
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON: bad escape at " + (i - 1));
                }
            }
        }

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            W(sb, v);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object v)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (v == null) { sb.Append("null"); return; }
            if (v is string || v is char) { Q(sb, v.ToString()); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, inv);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null"); else sb.Append(d.ToString("R", inv));
                return;
            }
            if (v is int || v is long || v is short || v is byte || v is uint || v is ulong || v is ushort || v is sbyte || v is decimal)
            { sb.Append(Convert.ToString(v, inv)); return; }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Q(sb, kv.Key); sb.Append(':'); W(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var o in list) { if (!first) sb.Append(','); first = false; W(sb, o); }
                sb.Append(']');
                return;
            }
            Q(sb, v.ToString());
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
