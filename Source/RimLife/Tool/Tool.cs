using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimLife
{
    internal class Tool
    {
        public static Pawn GetPawn(string ID)
        {
            return PawnsFinder.AllMaps_Spawned.FirstOrFallback(pp => pp.ThingID == ID);
        }

        // JSON helpers: centralized, fast, and safe encoders/builders for lightweight JSON construction
        public static class Json
        {
            public static string Escape(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var sb = new StringBuilder(s.Length +8);
                for (int i =0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c <32)
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
                return sb.ToString();
            }

            public static string Quote(string s)
            {
                return '"' + Escape(s ?? string.Empty) + '"';
            }
        }

        // Lightweight JSON writer struct to minimize allocations while keeping code concise
        public struct JsonWriter
        {
            private readonly StringBuilder _sb;
            private bool _first;

            public JsonWriter(int capacity =256)
            {
                _sb = new StringBuilder(capacity);
                _first = true;
                _sb.Append('{');
            }

            private void CommaIfNeeded()
            {
                if (!_first) _sb.Append(',');
                else _first = false;
            }

            public JsonWriter Prop(string name, string value)
            {
                if (value == null) return this;
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":\"").Append(Json.Escape(value)).Append('"');
                return this;
            }

            public JsonWriter Prop(string name, bool value)
            {
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":").Append(value ? "true" : "false");
                return this;
            }

            public JsonWriter Prop(string name, int value)
            {
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public JsonWriter Prop(string name, long value)
            {
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public JsonWriter Prop(string name, float value, string format = null)
            {
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":");
                _sb.Append(format == null ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
                return this;
            }

            public JsonWriter Prop(string name, double value, string format = null)
            {
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":");
                _sb.Append(format == null ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
                return this;
            }

            // Use when the value is already valid JSON (object/array/number/bool/null)
            public JsonWriter PropRaw(string name, string rawJson)
            {
                if (string.IsNullOrEmpty(rawJson)) return this;
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":").Append(rawJson);
                return this;
            }

            public JsonWriter Array(string name, IEnumerable<string> values)
            {
                if (values == null) return this;
                var list = values as IList<string> ?? values.ToList();
                if (list.Count ==0) return this;
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":[");
                bool first = true;
                foreach (var v in list)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    _sb.Append('"').Append(Json.Escape(v ?? string.Empty)).Append('"');
                }
                _sb.Append(']');
                return this;
            }

            // For arrays of nested JSON objects/values (already encoded)
            public JsonWriter ArrayRaw(string name, IEnumerable<string> rawValues)
            {
                if (rawValues == null) return this;
                var list = rawValues as IList<string> ?? rawValues.ToList();
                if (list.Count ==0) return this;
                CommaIfNeeded();
                _sb.Append('"').Append(Json.Escape(name)).Append("\":[");
                bool first = true;
                foreach (var rv in list)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    _sb.Append(rv ?? "null");
                }
                _sb.Append(']');
                return this;
            }

            public string Close()
            {
                _sb.Append('}');
                return _sb.ToString();
            }

            public override string ToString()
            {
                // Non-mutating close for convenience
                return _sb.ToString() + "}";
            }
        }
    }
}
