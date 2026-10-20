using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mithor.Core.Util
{
    /// <summary>
    /// Implementación mínima de JSON (lectura y escritura) sin dependencias externas.
    /// Se usa para la configuración y para la salida de la CLI. Funciona igual en
    /// .NET Framework 4.8 y en .NET 8, lo que mantiene el núcleo portable a Linux.
    /// </summary>
    public sealed class JsonValue
    {
        public enum ValueKind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object
        }

        private readonly ValueKind _kind;
        private bool _bool;
        private double _number;
        private string _text;
        private List<JsonValue> _items;
        private Dictionary<string, JsonValue> _members;
        private List<string> _order;

        /// <summary>Valor ausente: evita comprobaciones de null al leer configuración.</summary>
        public static readonly JsonValue Missing = new JsonValue(ValueKind.Null);

        private JsonValue(ValueKind kind)
        {
            _kind = kind;
        }

        public ValueKind Kind { get { return _kind; } }
        public bool IsMissing { get { return _kind == ValueKind.Null; } }
        public bool IsObject { get { return _kind == ValueKind.Object; } }
        public bool IsArray { get { return _kind == ValueKind.Array; } }

        public static JsonValue From(bool value)
        {
            JsonValue v = new JsonValue(ValueKind.Bool);
            v._bool = value;
            return v;
        }

        public static JsonValue From(int value)
        {
            return From((double)value);
        }

        public static JsonValue From(double value)
        {
            JsonValue v = new JsonValue(ValueKind.Number);
            v._number = value;
            return v;
        }

        public static JsonValue From(string value)
        {
            if (value == null)
            {
                return Missing;
            }
            JsonValue v = new JsonValue(ValueKind.String);
            v._text = value;
            return v;
        }

        public static JsonValue NewObject()
        {
            JsonValue v = new JsonValue(ValueKind.Object);
            v._members = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);
            v._order = new List<string>();
            return v;
        }

        public static JsonValue NewArray()
        {
            JsonValue v = new JsonValue(ValueKind.Array);
            v._items = new List<JsonValue>();
            return v;
        }

        public int Count
        {
            get
            {
                if (_kind == ValueKind.Array) { return _items.Count; }
                if (_kind == ValueKind.Object) { return _members.Count; }
                return 0;
            }
        }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue value;
                if (_kind == ValueKind.Object && _members.TryGetValue(key, out value))
                {
                    return value;
                }
                return Missing;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (_kind == ValueKind.Array && index >= 0 && index < _items.Count)
                {
                    return _items[index];
                }
                return Missing;
            }
        }

        public bool Has(string key)
        {
            return _kind == ValueKind.Object && _members.ContainsKey(key);
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Members
        {
            get
            {
                if (_kind != ValueKind.Object)
                {
                    yield break;
                }
                for (int i = 0; i < _order.Count; i++)
                {
                    yield return new KeyValuePair<string, JsonValue>(_order[i], _members[_order[i]]);
                }
            }
        }

        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (_kind != ValueKind.Array)
                {
                    yield break;
                }
                for (int i = 0; i < _items.Count; i++)
                {
                    yield return _items[i];
                }
            }
        }

        public void Set(string key, JsonValue value)
        {
            if (_kind != ValueKind.Object || string.IsNullOrEmpty(key))
            {
                return;
            }
            JsonValue existing;
            if (_members.TryGetValue(key, out existing))
            {
                _members[key] = value ?? Missing;
                return;
            }
            _members[key] = value ?? Missing;
            _order.Add(key);
        }

        public void Add(JsonValue value)
        {
            if (_kind == ValueKind.Array)
            {
                _items.Add(value ?? Missing);
            }
        }

        public bool Remove(string key)
        {
            if (_kind != ValueKind.Object || !_members.ContainsKey(key))
            {
                return false;
            }
            _members.Remove(key);
            _order.Remove(key);
            return true;
        }

        public string AsString(string fallback)
        {
            if (_kind == ValueKind.String)
            {
                return _text;
            }
            if (_kind == ValueKind.Number)
            {
                return FormatNumber(_number);
            }
            if (_kind == ValueKind.Bool)
            {
                return _bool ? "true" : "false";
            }
            return fallback;
        }

        public bool AsBool(bool fallback)
        {
            if (_kind == ValueKind.Bool)
            {
                return _bool;
            }
            if (_kind == ValueKind.Number)
            {
                return Math.Abs(_number) > double.Epsilon;
            }
            if (_kind == ValueKind.String)
            {
                return string.Equals(_text, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(_text, "1", StringComparison.Ordinal);
            }
            return fallback;
        }

        public int AsInt(int fallback)
        {
            double d = AsDouble(double.NaN);
            if (double.IsNaN(d))
            {
                return fallback;
            }
            if (d > int.MaxValue) { return int.MaxValue; }
            if (d < int.MinValue) { return int.MinValue; }
            return (int)Math.Round(d);
        }

        public double AsDouble(double fallback)
        {
            if (_kind == ValueKind.Number)
            {
                return _number;
            }
            if (_kind == ValueKind.String)
            {
                double parsed;
                if (double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            if (_kind == ValueKind.Bool)
            {
                return _bool ? 1.0 : 0.0;
            }
            return fallback;
        }

        public string ToJson(bool indented)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, indented, 0);
            return sb.ToString();
        }

        public override string ToString()
        {
            return ToJson(false);
        }

        private static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "0";
            }
            if (Math.Abs(value) < 1e15 && Math.Abs(value - Math.Round(value)) < 1e-9)
            {
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            }
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void WriteValue(StringBuilder sb, bool indented, int depth)
        {
            switch (_kind)
            {
                case ValueKind.Null:
                    sb.Append("null");
                    break;
                case ValueKind.Bool:
                    sb.Append(_bool ? "true" : "false");
                    break;
                case ValueKind.Number:
                    sb.Append(FormatNumber(_number));
                    break;
                case ValueKind.String:
                    WriteString(sb, _text);
                    break;
                case ValueKind.Array:
                    WriteArray(sb, indented, depth);
                    break;
                case ValueKind.Object:
                    WriteObject(sb, indented, depth);
                    break;
            }
        }

        private void WriteArray(StringBuilder sb, bool indented, int depth)
        {
            if (_items.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                if (indented)
                {
                    sb.AppendLine();
                    Indent(sb, depth + 1);
                }
                _items[i].WriteValue(sb, indented, depth + 1);
            }
            if (indented)
            {
                sb.AppendLine();
                Indent(sb, depth);
            }
            sb.Append(']');
        }

        private void WriteObject(StringBuilder sb, bool indented, int depth)
        {
            if (_order.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append('{');
            for (int i = 0; i < _order.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                if (indented)
                {
                    sb.AppendLine();
                    Indent(sb, depth + 1);
                }
                WriteString(sb, _order[i]);
                sb.Append(indented ? ": " : ":");
                _members[_order[i]].WriteValue(sb, indented, depth + 1);
            }
            if (indented)
            {
                sb.AppendLine();
                Indent(sb, depth);
            }
            sb.Append('}');
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            if (text == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u007f')
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

        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new FormatException("JSON vacío.");
            }
            Parser parser = new Parser(text);
            return parser.ParseDocument();
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text;
                _index = 0;
            }

            public JsonValue ParseDocument()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    throw Error("documento vacío");
                }
                JsonValue value = ParseValue();
                SkipWhitespace();
                if (_index < _text.Length)
                {
                    throw Error("contenido adicional tras el valor");
                }
                return value;
            }

            private JsonValue ParseValue()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    throw Error("fin de documento inesperado");
                }
                char c = _text[_index];
                switch (c)
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return From(ParseString());
                    case 't':
                        ExpectWord("true");
                        return From(true);
                    case 'f':
                        ExpectWord("false");
                        return From(false);
                    case 'n':
                        ExpectWord("null");
                        return Missing;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return From(ParseNumber());
                        }
                        throw Error("carácter inesperado '" + c + "'");
                }
            }

            private JsonValue ParseObject()
            {
                JsonValue obj = NewObject();
                _index++; // {
                SkipWhitespace();
                if (Peek('}'))
                {
                    _index++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"'))
                    {
                        throw Error("se esperaba el nombre de una propiedad");
                    }
                    string key = ParseString();
                    SkipWhitespace();
                    if (!Peek(':'))
                    {
                        throw Error("se esperaba ':'");
                    }
                    _index++;
                    JsonValue value = ParseValue();
                    obj.Set(key, value);
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        _index++;
                        SkipWhitespace();
                        if (Peek('}'))
                        {
                            _index++;
                            break;
                        }
                        continue;
                    }
                    if (Peek('}'))
                    {
                        _index++;
                        break;
                    }
                    throw Error("se esperaba ',' o '}'");
                }
                return obj;
            }

            private JsonValue ParseArray()
            {
                JsonValue array = NewArray();
                _index++; // [
                SkipWhitespace();
                if (Peek(']'))
                {
                    _index++;
                    return array;
                }
                while (true)
                {
                    JsonValue value = ParseValue();
                    array.Add(value);
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        _index++;
                        SkipWhitespace();
                        if (Peek(']'))
                        {
                            _index++;
                            break;
                        }
                        continue;
                    }
                    if (Peek(']'))
                    {
                        _index++;
                        break;
                    }
                    throw Error("se esperaba ',' o ']'");
                }
                return array;
            }

            private string ParseString()
            {
                if (!Peek('"'))
                {
                    throw Error("se esperaba una cadena");
                }
                _index++;
                StringBuilder sb = new StringBuilder();
                while (_index < _text.Length)
                {
                    char c = _text[_index++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_index >= _text.Length)
                    {
                        throw Error("secuencia de escape incompleta");
                    }
                    char esc = _text[_index++];
                    switch (esc)
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
                            if (_index + 4 > _text.Length)
                            {
                                throw Error("escape \\u incompleto");
                            }
                            string hex = _text.Substring(_index, 4);
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                throw Error("escape \\u no válido");
                            }
                            sb.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw Error("escape no válido '\\" + esc + "'");
                    }
                }
                throw Error("cadena sin cerrar");
            }

            private double ParseNumber()
            {
                int start = _index;
                if (Peek('-'))
                {
                    _index++;
                }
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    bool isDigit = c >= '0' && c <= '9';
                    if (isDigit || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        _index++;
                        continue;
                    }
                    break;
                }
                string raw = _text.Substring(start, _index - start);
                double value;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    throw Error("número no válido '" + raw + "'");
                }
                return value;
            }

            private void ExpectWord(string word)
            {
                if (_index + word.Length > _text.Length
                    || string.Compare(_text, _index, word, 0, word.Length, StringComparison.Ordinal) != 0)
                {
                    throw Error("se esperaba '" + word + "'");
                }
                _index += word.Length;
            }

            private bool Peek(char c)
            {
                return _index < _text.Length && _text[_index] == c;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\ufeff')
                    {
                        _index++;
                        continue;
                    }
                    break;
                }
            }

            private FormatException Error(string message)
            {
                int line = 1;
                int column = 1;
                for (int i = 0; i < _index && i < _text.Length; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }
                return new FormatException(string.Format("JSON inválido (línea {0}, columna {1}): {2}", line, column, message));
            }
        }
    }
}
