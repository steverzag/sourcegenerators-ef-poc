using System.Globalization;
using System.Text;

namespace SourceGenerators.EF.Generators;

/// <summary>
/// Minimal JSON reader. Generators target netstandard2.0 and can't safely depend on
/// System.Text.Json, so this keeps the generator self-contained.
/// Objects -> Dictionary&lt;string, object?&gt;, arrays -> List&lt;object?&gt;,
/// numbers -> double, plus string / bool / null.
/// </summary>
internal sealed class MiniJson
{
	private readonly string _s;
	private int _i;

	private MiniJson(string s) => _s = s;

	public static object? Parse(string json)
	{
		var p = new MiniJson(json);
		p.SkipWs();
		var v = p.ReadValue();
		p.SkipWs();
		if (p._i != p._s.Length) throw p.Error("Unexpected trailing content");
		return v;
	}

	private object? ReadValue()
	{
		if (_i >= _s.Length) throw Error("Unexpected end of input");
		switch (_s[_i])
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

	private Dictionary<string, object?> ReadObject()
	{
		var d = new Dictionary<string, object?>();
		_i++; // {
		SkipWs();
		if (Peek() == '}') { _i++; return d; }
		while (true)
		{
			SkipWs();
			if (Peek() != '"') throw Error("Expected property name");
			var key = ReadString();
			SkipWs();
			if (Peek() != ':') throw Error("Expected ':'");
			_i++;
			SkipWs();
			d[key] = ReadValue();
			SkipWs();
			var c = Peek();
			if (c == ',') { _i++; continue; }
			if (c == '}') { _i++; return d; }
			throw Error("Expected ',' or '}'");
		}
	}

	private List<object?> ReadArray()
	{
		var l = new List<object?>();
		_i++; // [
		SkipWs();
		if (Peek() == ']') { _i++; return l; }
		while (true)
		{
			SkipWs();
			l.Add(ReadValue());
			SkipWs();
			var c = Peek();
			if (c == ',') { _i++; continue; }
			if (c == ']') { _i++; return l; }
			throw Error("Expected ',' or ']'");
		}
	}

	private string ReadString()
	{
		var sb = new StringBuilder();
		_i++; // opening quote
		while (_i < _s.Length)
		{
			var c = _s[_i++];
			if (c == '"') return sb.ToString();
			if (c != '\\') { sb.Append(c); continue; }
			if (_i >= _s.Length) break;
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
					if (_i + 4 > _s.Length) throw Error("Bad \\u escape");
					sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
					_i += 4;
					break;
				default: throw Error("Bad escape sequence");
			}
		}
		throw Error("Unterminated string");
	}

	private double ReadNumber()
	{
		var start = _i;
		while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
		if (start == _i) throw Error("Unexpected character");
		return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	private void Expect(string lit)
	{
		if (string.CompareOrdinal(_s, _i, lit, 0, lit.Length) != 0) throw Error($"Expected '{lit}'");
		_i += lit.Length;
	}

	private char Peek() => _i < _s.Length ? _s[_i] : throw Error("Unexpected end of input");

	private void SkipWs()
	{
		while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
	}

	private FormatException Error(string msg) => new($"{msg} at position {_i}");
}
