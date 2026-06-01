using System;
using System.Globalization;

namespace Fuse.ExternalEditor.Logic;

/// <summary>
/// Small recursive-descent arithmetic evaluator for the calculator panel:
/// + - * / ^ (right-assoc), parentheses, unary +/-. Pure and culture-invariant.
/// </summary>
public static class ExpressionEvaluator
{
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new FormatException("Empty expression.");
        }

        var pos = 0;
        var value = ParseExpr(expression, ref pos);
        SkipWhitespace(expression, ref pos);
        if (pos != expression.Length)
        {
            throw new FormatException($"Unexpected '{expression[pos]}' at {pos}.");
        }

        return value;
    }

    private static double ParseExpr(string s, ref int p)
    {
        var v = ParseTerm(s, ref p);
        while (true)
        {
            SkipWhitespace(s, ref p);
            if (p < s.Length && (s[p] == '+' || s[p] == '-'))
            {
                var op = s[p++];
                var r = ParseTerm(s, ref p);
                v = op == '+' ? v + r : v - r;
            }
            else
            {
                return v;
            }
        }
    }

    private static double ParseTerm(string s, ref int p)
    {
        var v = ParseFactor(s, ref p);
        while (true)
        {
            SkipWhitespace(s, ref p);
            if (p < s.Length && (s[p] == '*' || s[p] == '/'))
            {
                var op = s[p++];
                var r = ParseFactor(s, ref p);
                v = op == '*' ? v * r : v / r;
            }
            else
            {
                return v;
            }
        }
    }

    private static double ParseFactor(string s, ref int p)
    {
        var b = ParseUnary(s, ref p);
        SkipWhitespace(s, ref p);
        if (p < s.Length && s[p] == '^')
        {
            p++;
            var e = ParseFactor(s, ref p); // right-associative
            return Math.Pow(b, e);
        }

        return b;
    }

    private static double ParseUnary(string s, ref int p)
    {
        SkipWhitespace(s, ref p);
        if (p < s.Length && s[p] == '-')
        {
            p++;
            return -ParseUnary(s, ref p);
        }

        if (p < s.Length && s[p] == '+')
        {
            p++;
            return ParseUnary(s, ref p);
        }

        return ParsePrimary(s, ref p);
    }

    private static double ParsePrimary(string s, ref int p)
    {
        SkipWhitespace(s, ref p);
        if (p < s.Length && s[p] == '(')
        {
            p++;
            var v = ParseExpr(s, ref p);
            SkipWhitespace(s, ref p);
            if (p >= s.Length || s[p] != ')')
            {
                throw new FormatException("Expected ')'.");
            }

            p++;
            return v;
        }

        var start = p;
        while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.'))
        {
            p++;
        }

        if (p == start)
        {
            throw new FormatException("Expected a number.");
        }

        return double.Parse(s.Substring(start, p - start), CultureInfo.InvariantCulture);
    }

    private static void SkipWhitespace(string s, ref int p)
    {
        while (p < s.Length && char.IsWhiteSpace(s[p]))
        {
            p++;
        }
    }
}
