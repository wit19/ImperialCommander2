// Minimal dependency-free JSON reader/writer (Unity 2020.3 has no
// System.Text.Json; net8 harness must behave identically).
// Tolerates trailing commas (IC2 repo JSON flavour).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TacticalAI.Core
{
    public static class Json
    {
        // Values: null, bool, double, long, string, List<object>, Dictionary<string, object>

        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || s.Substring(i, word.Length) != word)
                throw new FormatException("JSON: expected " + word + " at " + i);
            i += word.Length;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: expected ':' at " + i);
                i++;
                object v = ParseValue(s, ref i);
                d[k] = v;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException("JSON: expected ',' or '}' at " + i);
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException("JSON: expected ',' or ']' at " + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // "
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    char e = s[i++];
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
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: throw new FormatException("JSON: bad escape " + e);
                    }
                }
                else sb.Append(c);
            }
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { i++; continue; }
                if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') { isFloat |= c == '.' || c == 'e' || c == 'E'; i++; continue; }
                break;
            }
            string tok = s.Substring(start, i - start);
            if (!isFloat && long.TryParse(tok, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long lv))
                return lv;
            return double.Parse(tok, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---- typed access helpers (throw with path on type mismatch) ----
        public static Dictionary<string, object> AsDict(object v)
        {
            return v as Dictionary<string, object> ?? throw new FormatException("JSON: not an object: " + (v == null ? "null" : v.GetType().Name));
        }
        public static List<object> AsList(object v)
        {
            if (v is List<object> lo) return lo;
            // Runtime records come from the GAME's DataStore and carry .NET
            // arrays / typed lists (string[] keywords, ToArray() attacks) -
            // only this parser's own output is List<object>. All 69 cards were
            // skipped in the live playtest 27.10 because of the strict cast
            // (same adapter lesson as AsLong). Accept any enumerable.
            if (v is System.Collections.IEnumerable en && !(v is string))
            {
                var list = new List<object>();
                foreach (var item in en) list.Add(item);
                return list;
            }
            throw new FormatException("JSON: not an array" + (v == null ? ": null" : ": " + v.GetType().Name));
        }
        public static string AsStr(object v) { return (string)v; }
        public static bool AsBool(object v) { return (bool)v; }
        // Runtime records come from the GAME's DataStore (boxed int/float/string
        // values), not only from this parser's own long/double output - first
        // live playtest skipped all 69 cards because AsLong only accepted
        // long/double. Accept every numeric shape + invariant strings.
        public static long AsLong(object v)
        {
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is uint ui) return ui;
            if (v is double d) return (long)d;
            if (v is float f) return (long)f;
            if (v is decimal m) return (long)m;
            if (v is string s && long.TryParse(s, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
            throw new FormatException("JSON: not a number" + (v == null ? ": null" : ": " + v.GetType().Name));
        }
        public static int AsInt(object v) { return (int)AsLong(v); }
        public static double AsDouble(object v)
        {
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is float f) return f;
            if (v is decimal m) return (double)m;
            if (v is string s && double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
            throw new FormatException("JSON: not a number" + (v == null ? ": null" : ": " + v.GetType().Name));
        }
        public static Cell AsCell(object v)
        {
            var l = AsList(v);
            return new Cell(AsInt(l[0]), AsInt(l[1]));
        }
        public static List<Cell> AsCells(object v)
        {
            var outl = new List<Cell>();
            foreach (var e in AsList(v)) outl.Add(AsCell(e));
            return outl;
        }
        public static object Get(Dictionary<string, object> d, string k, object def = null)
        {
            return d.TryGetValue(k, out var v) ? v : def;
        }

        public static Cell EdgeKeyCell(object a, object b) => AsCell(a); // convenience kept tiny
    }
}
