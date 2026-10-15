namespace SkillSamples.Workflows;

/// <summary>
/// The part of GitHub Actions' expression language that job conditions here use: context paths
/// (<c>github.ref</c>, <c>inputs.environment</c>), string literals, <c>true</c>/<c>false</c>/<c>null</c>,
/// <c>==</c>, <c>!=</c>, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c> and parentheses. Comparison ignores case, as
/// GitHub's does; a missing context value is null.
/// </summary>
public sealed class WorkflowExpression
{
    private readonly List<string> _tokens;
    private readonly IReadOnlyDictionary<string, string> _context;
    private int _next;

    private WorkflowExpression(string expression, IReadOnlyDictionary<string, string> context)
    {
        _tokens = Tokenize(expression.Trim().TrimStart('$').Trim('{', '}', ' '));
        _context = context;
    }

    public static bool IsTrue(string expression, IReadOnlyDictionary<string, string> context)
    {
        var parser = new WorkflowExpression(expression, context);
        var value = parser.Or();
        if (parser._next != parser._tokens.Count) throw new FormatException($"unexpected '{parser._tokens[parser._next]}' in {expression}");
        return Truthy(value);
    }

    private object? Or()
    {
        var left = And();
        while (Accept("||"))
        {
            var right = And();
            left = Truthy(left) ? left : right;
        }

        return left;
    }

    private object? And()
    {
        var left = Comparison();
        while (Accept("&&"))
        {
            var right = Comparison();
            left = Truthy(left) ? right : left;
        }

        return left;
    }

    private object? Comparison()
    {
        var left = Unary();
        if (Accept("==")) return Same(left, Unary());
        if (Accept("!=")) return !Same(left, Unary());
        return left;
    }

    private object? Unary()
    {
        if (Accept("!")) return !Truthy(Unary());
        if (Accept("("))
        {
            var inner = Or();
            if (!Accept(")")) throw new FormatException("missing ')'");
            return inner;
        }

        var token = _tokens[_next++];
        return token switch
        {
            "true" => true,
            "false" => false,
            "null" => null,
            _ when token.StartsWith('\'') => token[1..^1].Replace("''", "'"),
            _ => _context.TryGetValue(token, out var value) ? value : null,
        };
    }

    private bool Accept(string token)
    {
        if (_next < _tokens.Count && _tokens[_next] == token)
        {
            _next++;
            return true;
        }

        return false;
    }

    private static bool Same(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
        _ => Equals(left, right),
    };

    private static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        _ => true,
    };

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '\'')
            {
                var end = i + 1;
                while (end < text.Length && !(text[end] == '\'' && (end + 1 == text.Length || text[end + 1] != '\''))) end += text[end] == '\'' ? 2 : 1;
                tokens.Add(text[i..(end + 1)]);
                i = end + 1;
                continue;
            }

            var two = i + 1 < text.Length ? text.Substring(i, 2) : "";
            if (two is "==" or "!=" or "&&" or "||") { tokens.Add(two); i += 2; continue; }
            if (c is '!' or '(' or ')') { tokens.Add(c.ToString()); i++; continue; }

            var start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_' or '-')) i++;
            if (start == i) throw new FormatException($"unexpected '{c}'");
            tokens.Add(text[start..i]);
        }

        return tokens;
    }
}
