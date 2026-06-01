using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Tolerant JSON reader for legacy Railroader mod data files.
    /// Port of <c>legacy_json.py</c>. Strips JSONC-style line
    /// (<c>//</c>) and block (<c>/* ... */</c>) comments, removes
    /// trailing commas, and (optionally) closes truncated documents
    /// by appending the matching <c>}</c>/<c>]</c> for any
    /// still-open structural brackets.
    /// </summary>
    /// <remarks>
    /// The repair is narrow on purpose: this is the worst-case
    /// recovery path for malformed legacy data, NOT a general JSON5
    /// implementation. Single quotes, unquoted keys, and other
    /// extensions stay unsupported — they'd hide the actual broken
    /// data from the modder.
    /// </remarks>
    internal static class LegacyJsonReader
    {
        public static JToken ReadJson(string path, bool repair = true)
        {
            var text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            // File.ReadAllText doesn't strip the BOM the way Python's
            // utf-8-sig encoding does. Strip it manually.
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1);
            }
            return Loads(text, repair);
        }

        public static JToken Loads(string text, bool repair = true)
        {
            var cleaned = StripComments(text);
            cleaned = RemoveTrailingCommas(cleaned);
            if (repair)
            {
                cleaned = CloseUnbalancedJson(cleaned);
                // Closing a truncated object can expose a trailing
                // comma that wasn't followed by a closing bracket
                // during the first pass.
                cleaned = RemoveTrailingCommas(cleaned);
            }
            return JToken.Parse(cleaned);
        }

        /// <summary>
        /// Removes <c>//</c> line comments and <c>/* ... */</c>
        /// block comments. String contents are preserved verbatim
        /// (including escape sequences) so a // or /* inside a JSON
        /// string isn't mistaken for a comment delimiter.
        /// </summary>
        public static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var sb = new StringBuilder(text.Length);
            bool inString = false;
            bool escaped = false;
            int index = 0;
            int length = text.Length;

            while (index < length)
            {
                char ch = text[index];

                if (inString)
                {
                    sb.Append(ch);
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') inString = false;
                    index++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    sb.Append(ch);
                    index++;
                    continue;
                }

                if (ch == '/' && index + 1 < length && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < length && text[index] != '\r' && text[index] != '\n')
                    {
                        index++;
                    }
                    continue;
                }

                if (ch == '/' && index + 1 < length && text[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < length && !(text[index] == '*' && text[index + 1] == '/'))
                    {
                        index++;
                    }
                    index = Math.Min(index + 2, length);
                    continue;
                }

                sb.Append(ch);
                index++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Removes commas that sit immediately before a closing
        /// <c>}</c> or <c>]</c>. Iterates until the text stabilises
        /// because removing one trailing comma can expose another
        /// (legal in JSON; common in JSON5 source files).
        /// </summary>
        /// <remarks>
        /// String-aware: the scan tracks string literals (and their
        /// escapes) so a comma inside a JSON string value — e.g.
        /// <c>"label": "before, ] after"</c> — is never touched. A
        /// naive regex over the whole document would silently corrupt
        /// such values, so this walks the text the same way
        /// <see cref="StripComments"/> does.
        /// </remarks>
        public static string RemoveTrailingCommas(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            string previous = null;
            string current = text;
            while (!ReferenceEquals(previous, current) && previous != current)
            {
                previous = current;
                current = RemoveTrailingCommasOnce(current);
            }
            return current;
        }

        private static string RemoveTrailingCommasOnce(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool inString = false;
            bool escaped = false;
            int length = text.Length;

            for (int i = 0; i < length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    sb.Append(ch);
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') inString = false;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    sb.Append(ch);
                    continue;
                }

                if (ch == ',')
                {
                    // Look ahead past whitespace for a closing bracket.
                    // If the next structural char is } or ], this comma
                    // is trailing — drop it. We're outside any string
                    // here, so commas inside string values are safe.
                    int j = i + 1;
                    while (j < length && char.IsWhiteSpace(text[j])) j++;
                    if (j < length && (text[j] == '}' || text[j] == ']'))
                    {
                        continue; // skip the comma
                    }
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Appends missing <c>}</c> / <c>]</c> characters to close
        /// any structural brackets that opened but never closed
        /// (typically because a legacy file was truncated mid-write).
        /// Strings are scanned so brackets inside a string don't
        /// affect the stack.
        /// </summary>
        public static string CloseUnbalancedJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var stack = new System.Collections.Generic.Stack<char>();
            bool inString = false;
            bool escaped = false;

            foreach (var ch in text)
            {
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') inString = false;
                    continue;
                }

                if (ch == '"') inString = true;
                else if (ch == '{') stack.Push('}');
                else if (ch == '[') stack.Push(']');
                else if (ch == '}' || ch == ']')
                {
                    if (stack.Count > 0 && stack.Peek() == ch)
                    {
                        stack.Pop();
                    }
                }
            }

            if (stack.Count == 0) return text;

            var sb = new StringBuilder(text.TrimEnd());
            sb.Append('\n');
            // The Python source reverses the stack (deepest opener
            // gets its matching closer first). C#'s Stack.ToArray()
            // already returns top-of-stack first, which IS the
            // reverse-insertion order.
            foreach (var closer in stack)
            {
                sb.Append(closer);
            }
            sb.Append('\n');
            return sb.ToString();
        }
    }
}
