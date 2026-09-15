using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinUpdateManager.Output
{
    /// <summary>Minimal indented JSON writer (System.Text.Json is not available on .NET Framework 3.5).</summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly Stack<bool> containerHasItems = new Stack<bool>();
        private bool afterName;

        public JsonWriter BeginObject()
        {
            BeforeValue();
            sb.Append('{');
            containerHasItems.Push(false);
            return this;
        }

        public JsonWriter BeginObject(string name)
        {
            WriteName(name);
            return BeginObject();
        }

        public JsonWriter EndObject()
        {
            EndContainer('}');
            return this;
        }

        public JsonWriter BeginArray()
        {
            BeforeValue();
            sb.Append('[');
            containerHasItems.Push(false);
            return this;
        }

        public JsonWriter BeginArray(string name)
        {
            WriteName(name);
            return BeginArray();
        }

        public JsonWriter EndArray()
        {
            EndContainer(']');
            return this;
        }

        public JsonWriter Property(string name, string value)
        {
            WriteName(name);
            return Value(value);
        }

        public JsonWriter Property(string name, bool value)
        {
            WriteName(name);
            return Value(value);
        }

        public JsonWriter Property(string name, bool? value)
        {
            WriteName(name);
            if (value.HasValue) return Value(value.Value);
            return Null();
        }

        public JsonWriter Property(string name, int value)
        {
            WriteName(name);
            return Value(value);
        }

        public JsonWriter Property(string name, long value)
        {
            WriteName(name);
            return Value(value);
        }

        public JsonWriter Property(string name, int? value)
        {
            WriteName(name);
            if (value.HasValue) return Value(value.Value);
            return Null();
        }

        public JsonWriter Property(string name, decimal value)
        {
            WriteName(name);
            BeforeValue();
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Property(string name, DateTime? value)
        {
            WriteName(name);
            if (!value.HasValue) return Null();
            return Value(value.Value.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture));
        }

        public JsonWriter Property(string name, IEnumerable<string> values)
        {
            BeginArray(name);
            foreach (var v in values) Value(v);
            return EndArray();
        }

        public JsonWriter Value(string value)
        {
            BeforeValue();
            if (value == null)
            {
                sb.Append("null");
            }
            else
            {
                WriteString(value);
            }
            return this;
        }

        public JsonWriter Value(bool value)
        {
            BeforeValue();
            sb.Append(value ? "true" : "false");
            return this;
        }

        public JsonWriter Value(long value)
        {
            BeforeValue();
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Null()
        {
            BeforeValue();
            sb.Append("null");
            return this;
        }

        public override string ToString()
        {
            return sb.ToString();
        }

        private void WriteName(string name)
        {
            BeforeValue();
            WriteString(name);
            sb.Append(": ");
            afterName = true;
        }

        private void BeforeValue()
        {
            if (afterName)
            {
                afterName = false;
                return;
            }
            if (containerHasItems.Count == 0) return;
            if (containerHasItems.Peek()) sb.Append(',');
            containerHasItems.Pop();
            containerHasItems.Push(true);
            NewLine();
        }

        private void EndContainer(char close)
        {
            if (containerHasItems.Count == 0) throw new InvalidOperationException("No open JSON container.");
            bool hadItems = containerHasItems.Pop();
            if (hadItems) NewLine();
            sb.Append(close);
        }

        private void NewLine()
        {
            sb.Append(Environment.NewLine);
            sb.Append(' ', containerHasItems.Count * 2);
        }

        private void WriteString(string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
