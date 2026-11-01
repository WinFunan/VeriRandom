using System;
using System.Collections.Generic;
using System.Globalization;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     A deliberately tiny integer predicate over a single variable <c>X</c>, used by the enumeration generator.
/// </summary>
/// <remarks>
///     <para>
///         <b>This is not a scripting engine, and must never become one.</b> Embedding a real one (Roslyn scripting,
///         Lua, JS) to evaluate a teacher's one-line condition would hand arbitrary code execution to a text box for no
///         benefit: what a classroom needs is "even numbers", "not a multiple of ten", "between 5 and 9". The supported
///         grammar is exactly that much — integer literals, <c>+ - * / %</c>, comparisons, <c>&amp;&amp; || !</c>,
///         parentheses, and the variable <c>X</c>. There is no I/O, no reflection, no assignment, no loops, and no way
///         to name anything else.
///     </para>
///     <para>
///         Compilation is separate from evaluation: the expression is parsed once into a node tree, then tested per
///         value. A malformed predicate therefore costs one parse, not one failure per candidate.
///     </para>
///     <para>
///         Division and modulo by zero are reported as errors rather than silently excluded: a teacher who wrote
///         <c>X%0==0</c> made a mistake and should be told, not given an empty set.
///     </para>
/// </remarks>
public sealed class RawPredicate
{
    private readonly Node _root;

    private RawPredicate(Node root) => _root = root;

    /// <summary>Compiles a predicate. Fails with a human-readable reason, never with an exception.</summary>
    public static bool TryCompile(string? text, out RawPredicate? predicate, out string? error)
    {
        predicate = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            var parser = new Parser(text);
            var root = parser.ParseExpression();
            parser.ExpectEnd();
            predicate = new RawPredicate(root);
            return true;
        }
        catch (PredicateException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Evaluates the predicate for one value. Throws when the expression divides by zero.</summary>
    public bool Test(long x) => _root.Evaluate(x) != 0;

    private abstract class Node
    {
        public abstract long Evaluate(long x);
    }

    private sealed class Literal(long value) : Node
    {
        public override long Evaluate(long x) => value;
    }

    private sealed class Variable : Node
    {
        public override long Evaluate(long x) => x;
    }

    private sealed class Unary(char op, Node operand) : Node
    {
        public override long Evaluate(long x) => op switch
        {
            '-' => -operand.Evaluate(x),
            '!' => operand.Evaluate(x) == 0 ? 1 : 0,
            _ => throw new InvalidOperationException("Unsupported unary operator.")
        };
    }

    private sealed class Binary(char op, Node left, Node right) : Node
    {
        public override long Evaluate(long x)
        {
            var a = left.Evaluate(x);
            var b = right.Evaluate(x);

            return op switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b == 0 ? throw new DivideByZeroException() : a / b,
                '%' => b == 0 ? throw new DivideByZeroException() : a % b,
                '<' => a < b ? 1 : 0,
                'l' => a <= b ? 1 : 0,
                '>' => a > b ? 1 : 0,
                'g' => a >= b ? 1 : 0,
                'e' => a == b ? 1 : 0,
                'n' => a != b ? 1 : 0,
                '&' => a != 0 && b != 0 ? 1 : 0,
                '|' => a != 0 || b != 0 ? 1 : 0,
                _ => throw new InvalidOperationException("Unsupported binary operator.")
            };
        }
    }

    private sealed class PredicateException(string message) : Exception(message);

    /// <summary>Recursive-descent parser with the usual precedence ladder.</summary>
    private sealed class Parser(string text)
    {
        private int _position;

        public Node ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_position < text.Length)
                throw new PredicateException($"Cannot read the predicate at position {_position + 1}.");
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (Match("||"))
                left = new Binary('|', left, ParseAnd());
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseEquality();
            while (Match("&&"))
                left = new Binary('&', left, ParseEquality());
            return left;
        }

        private Node ParseEquality()
        {
            var left = ParseRelational();
            while (true)
            {
                if (Match("=="))
                    left = new Binary('e', left, ParseRelational());
                else if (Match("!="))
                    left = new Binary('n', left, ParseRelational());
                else
                    return left;
            }
        }

        private Node ParseRelational()
        {
            var left = ParseAdditive();
            while (true)
            {
                if (Match("<="))
                    left = new Binary('l', left, ParseAdditive());
                else if (Match(">="))
                    left = new Binary('g', left, ParseAdditive());
                else if (Match("<"))
                    left = new Binary('<', left, ParseAdditive());
                else if (Match(">"))
                    left = new Binary('>', left, ParseAdditive());
                else
                    return left;
            }
        }

        private Node ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (Match("+"))
                    left = new Binary('+', left, ParseMultiplicative());
                else if (Match("-"))
                    left = new Binary('-', left, ParseMultiplicative());
                else
                    return left;
            }
        }

        private Node ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Match("*"))
                    left = new Binary('*', left, ParseUnary());
                else if (Match("/"))
                    left = new Binary('/', left, ParseUnary());
                else if (Match("%"))
                    left = new Binary('%', left, ParseUnary());
                else
                    return left;
            }
        }

        private Node ParseUnary()
        {
            if (Match("!"))
                return new Unary('!', ParseUnary());
            if (Match("-"))
                return new Unary('-', ParseUnary());
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            SkipWhitespace();

            if (_position < text.Length && text[_position] == '(')
            {
                _position++;
                var inner = ParseExpression();
                SkipWhitespace();
                if (_position >= text.Length || text[_position] != ')')
                    throw new PredicateException("A closing bracket is missing.");
                _position++;
                return inner;
            }

            if (_position < text.Length && (char.IsDigit(text[_position])))
            {
                var start = _position;
                while (_position < text.Length && char.IsDigit(text[_position]))
                    _position++;

                var digits = text[start.._position];
                if (!long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    throw new PredicateException($"The number {digits} does not fit.");

                return new Literal(value);
            }

            if (_position < text.Length && (text[_position] is 'X' or 'x'))
            {
                _position++;
                return new Variable();
            }

            throw new PredicateException(_position < text.Length
                ? $"Unexpected character '{text[_position]}' at position {_position + 1}."
                : "The predicate ends too early.");
        }

        private bool Match(string token)
        {
            SkipWhitespace();
            if (_position + token.Length > text.Length)
                return false;

            if (!text.AsSpan(_position, token.Length).SequenceEqual(token))
                return false;

            _position += token.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
                _position++;
        }
    }
}
