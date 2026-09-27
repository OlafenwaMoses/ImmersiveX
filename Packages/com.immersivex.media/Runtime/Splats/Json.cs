using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A small JSON reader for stream manifests: objects become dictionaries, arrays become lists, numbers become
    /// doubles. Unity's JsonUtility can't read nested arrays, and this avoids adding a JSON package.
    /// </summary>
    static class Json
    {
        public static object Parse(string text)
        {
            var reader = new Reader(text ?? string.Empty);
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw new FormatException($"Unexpected text at character {reader.Position}.");
            return value;
        }

        sealed class Reader
        {
            readonly string _text;
            readonly StringBuilder _builder = new StringBuilder();
            int _index;

            public Reader(string text) => _text = text;

            public bool AtEnd => _index >= _text.Length;
            public int Position => _index;

            public void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                    _index++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd)
                    throw new FormatException("Unexpected end of JSON.");
                switch (_text[_index])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>();
                _index++;
                SkipWhitespace();
                if (TryConsume('}'))
                    return result;
                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    SkipWhitespace();
                    Consume(':');
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (TryConsume(','))
                        continue;
                    Consume('}');
                    return result;
                }
            }

            List<object> ReadArray()
            {
                var result = new List<object>();
                _index++;
                SkipWhitespace();
                if (TryConsume(']'))
                    return result;
                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (TryConsume(','))
                        continue;
                    Consume(']');
                    return result;
                }
            }

            string ReadString()
            {
                Consume('"');
                _builder.Clear();
                while (true)
                {
                    if (AtEnd)
                        throw new FormatException("Unterminated string.");
                    var c = _text[_index++];
                    if (c == '"')
                        return _builder.ToString();
                    if (c != '\\')
                    {
                        _builder.Append(c);
                        continue;
                    }

                    if (AtEnd)
                        throw new FormatException("Unterminated escape.");
                    var escaped = _text[_index++];
                    switch (escaped)
                    {
                        case 'n': _builder.Append('\n'); break;
                        case 't': _builder.Append('\t'); break;
                        case 'r': _builder.Append('\r'); break;
                        case 'b': _builder.Append('\b'); break;
                        case 'f': _builder.Append('\f'); break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                                throw new FormatException("Bad unicode escape.");
                            _builder.Append((char)int.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default: _builder.Append(escaped); break; // \" \\ \/
                    }
                }
            }

            double ReadNumber()
            {
                var start = _index;
                while (_index < _text.Length && "+-0123456789.eE".IndexOf(_text[_index]) >= 0)
                    _index++;
                if (start == _index)
                    throw new FormatException($"Unexpected '{_text[_index]}' at character {_index}.");
                return double.Parse(_text.Substring(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _index, word, 0, word.Length) != 0)
                    throw new FormatException($"Expected '{word}' at character {_index}.");
                _index += word.Length;
            }

            bool TryConsume(char c)
            {
                if (_index < _text.Length && _text[_index] == c)
                {
                    _index++;
                    return true;
                }

                return false;
            }

            void Consume(char c)
            {
                if (!TryConsume(c))
                    throw new FormatException($"Expected '{c}' at character {_index}.");
            }
        }
    }
}
