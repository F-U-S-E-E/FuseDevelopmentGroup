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
        var v = ParseUnary(s, ref p);
        while (true)
        {
            SkipWhitespace(s, ref p);
            if (p < s.Length && (s[p] == '*' || s[p] == '/'))
            {
                var op = s[p++];
                var r = ParseUnary(s, ref p);
                v = op == '*' ? v * r : v / r;
            }
            else
            {
                return v;
            }
        }
    }

    // Unary binds looser than '^' so that -2^2 == -(2^2) == -4.
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

        return ParsePower(s, ref p);
    }

    private static double ParsePower(string s, ref int p)
    {
        var b = ParsePrimary(s, ref p);
        SkipWhitespace(s, ref p);
        if (p < s.Length && s[p] == '^')
        {
            p++;
            var e = ParseUnary(s, ref p); // right-associative; allows a signed exponent (2^-3)
            return Math.Pow(b, e);
        }

        return b;
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
