using System.Globalization;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Application.Forms;

/// <summary>Value produced by a form formula: a number, text, true/false, a list (a table column), or nothing.</summary>
public readonly record struct FValue(object? Raw)
{
    public static readonly FValue Empty = new(null);
    public static FValue Num(decimal d) => new(d);
    public static FValue Text(string s) => new(s);
    public static FValue Bool(bool b) => new(b);
    public static FValue List(IReadOnlyList<FValue> l) => new(l);
    public bool IsEmpty => Raw is null || Raw is string { Length: 0 };
    public bool IsError => Raw is FormulaError;
    public IReadOnlyList<FValue> Items => Raw as IReadOnlyList<FValue> ?? (IsEmpty ? [] : [this]);

    public decimal? AsNumber() => Raw switch
    {
        decimal d => d,
        bool b => b ? 1 : 0,
        string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };
    public bool AsBool() => Raw switch
    {
        bool b => b,
        decimal d => d != 0,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("pass", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
    public string AsText() => Raw switch
    {
        null => "",
        decimal d => d.ToString("0.##########", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        FormulaError e => e.Code,
        IReadOnlyList<FValue> l => string.Join(", ", l.Select(x => x.AsText())),
        _ => Raw.ToString() ?? "",
    };
}

public sealed record FormulaError(string Code)
{
    public static readonly FormulaError Div0 = new("#DIV/0!");
    public static readonly FormulaError Value = new("#VALUE!");
    public static readonly FormulaError Num = new("#NUM!");
    public static readonly FormulaError NA = new("#N/A");
}

/// <summary>
/// Safe formula language for controlled forms. Parsed into a tree and evaluated by an interpreter: no reflection, no code execution,
/// bounded length, depth and work. Operators: + - * / ^ (power) &amp; (join text) = &lt;&gt; &lt; &gt; &lt;= &gt;= and parentheses.
/// References: field keys (<c>length</c>), table columns as lists (<c>results.measured</c>) and, inside a table row, the row's own
/// column keys. Text literals use double quotes.
/// </summary>
public static class FormulaEngine
{
    public const int MaxLength = 2000;
    public const int MaxDepth = 60;
    public const int MaxListItems = 50_000;

    /// <summary>Supported functions with their argument counts (min, max; -1 = any).</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Functions = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
    {
        ["SUM"] = (1, -1), ["AVERAGE"] = (1, -1), ["MIN"] = (1, -1), ["MAX"] = (1, -1), ["COUNT"] = (1, -1), ["COUNTA"] = (1, -1),
        ["COUNTBLANK"] = (1, -1), ["COUNTIF"] = (2, 2), ["SUMIF"] = (2, 3), ["AVERAGEIF"] = (2, 3), ["MEDIAN"] = (1, -1),
        ["STDEV"] = (1, -1), ["STDEVP"] = (1, -1), ["VAR"] = (1, -1), ["RANGE"] = (1, -1), ["PRODUCT"] = (1, -1),
        ["IF"] = (2, 3), ["IFS"] = (2, -1), ["IFERROR"] = (2, 2), ["AND"] = (1, -1), ["OR"] = (1, -1), ["NOT"] = (1, 1), ["XOR"] = (1, -1),
        ["ISBLANK"] = (1, 1), ["ISNUMBER"] = (1, 1), ["CHOOSE"] = (2, -1), ["SWITCH"] = (3, -1),
        ["ROUND"] = (1, 2), ["ROUNDUP"] = (1, 2), ["ROUNDDOWN"] = (1, 2), ["TRUNC"] = (1, 2), ["INT"] = (1, 1), ["ABS"] = (1, 1), ["SQRT"] = (1, 1),
        ["POWER"] = (2, 2), ["MOD"] = (2, 2), ["SIGN"] = (1, 1), ["CEILING"] = (1, 2), ["FLOOR"] = (1, 2), ["PI"] = (0, 0),
        ["CONCAT"] = (1, -1), ["LEN"] = (1, 1), ["UPPER"] = (1, 1), ["LOWER"] = (1, 1), ["TRIM"] = (1, 1), ["LEFT"] = (1, 2), ["RIGHT"] = (1, 2),
        ["TEXT"] = (2, 2), ["VALUE"] = (1, 1), ["EXACT"] = (2, 2),
        ["TODAY"] = (0, 0), ["DAYS"] = (2, 2), ["EDATE"] = (2, 2), ["DATEADD"] = (2, 2), ["YEAR"] = (1, 1), ["MONTH"] = (1, 1), ["DAY"] = (1, 1),
        ["WITHIN"] = (3, 3), ["CP"] = (3, 3), ["CPK"] = (3, 3),
        // spreadsheet conversion: CELLS(a, b, ...) is a list of individual values (a converted cell range); multi-criteria and lookup functions
        ["CELLS"] = (1, -1), ["COUNTIFS"] = (2, -1), ["SUMIFS"] = (3, -1), ["AVERAGEIFS"] = (3, -1), ["MAXIFS"] = (3, -1), ["MINIFS"] = (3, -1),
        ["INDEX"] = (2, 2), ["MATCH"] = (2, 3), ["ISERROR"] = (1, 1), ["ISTEXT"] = (1, 1), ["DATE"] = (3, 3), ["LARGE"] = (2, 2), ["SMALL"] = (2, 2),
        ["MID"] = (3, 3), ["SUBSTITUTE"] = (3, 3), ["NA"] = (0, 0),
    };

    public static Node Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new AppException("A formula is empty.");
        if (source.Length > MaxLength) throw new AppException($"A formula is longer than {MaxLength} characters.");
        var p = new Parser(Tokenize(source.TrimStart('=')), source);
        var n = p.Expr(0);
        if (!p.AtEnd) throw new AppException($"Formula '{source}' is not valid near '{p.Peek}'.");
        return n;
    }

    /// <summary>Returns a description of the problem, or null when the formula parses.</summary>
    public static string? Problem(string source)
    {
        try { Parse(source); return null; } catch (AppException e) { return e.Message; }
    }

    /// <summary>References used by a formula (field keys and table.column pairs).</summary>
    public static IReadOnlySet<string> References(string source)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        Walk(Parse(source));
        return set;
        void Walk(Node n)
        {
            switch (n)
            {
                case RefNode r: set.Add(r.Name); break;
                case UnaryNode u: Walk(u.Operand); break;
                case BinaryNode b: Walk(b.Left); Walk(b.Right); break;
                case CallNode c: foreach (var a in c.Args) Walk(a); break;
            }
        }
    }

    public static FValue Evaluate(string source, Func<string, FValue?> resolve, DateOnly? today = null)
        => Evaluate(Parse(source), resolve, today);

    public static FValue Evaluate(Node node, Func<string, FValue?> resolve, DateOnly? today = null)
    {
        var ctx = new Ctx(resolve, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
        try { return ctx.Eval(node, 0); }
        catch (DivideByZeroException) { return new FValue(FormulaError.Div0); }
        catch (OverflowException) { return new FValue(FormulaError.Num); }
    }

    // ---------------------------------------------------------------- tree
    public abstract record Node;
    public sealed record NumNode(decimal Value) : Node;
    public sealed record TextNode(string Value) : Node;
    public sealed record BoolNode(bool Value) : Node;
    public sealed record RefNode(string Name) : Node;
    public sealed record UnaryNode(string Op, Node Operand) : Node;
    public sealed record BinaryNode(string Op, Node Left, Node Right) : Node;
    public sealed record CallNode(string Name, IReadOnlyList<Node> Args) : Node;

    // ---------------------------------------------------------------- tokens
    private enum T { Num, Str, Id, Op, LParen, RParen, Comma }
    private sealed record Tok(T Kind, string Text);

    private static List<Tok> Tokenize(string s)
    {
        var list = new List<Tok>();
        for (var i = 0; i < s.Length;)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { list.Add(new(T.LParen, "(")); i++; continue; }
            if (c == ')') { list.Add(new(T.RParen, ")")); i++; continue; }
            if (c is ',' or ';') { list.Add(new(T.Comma, ",")); i++; continue; }
            if (c == '"')
            {
                var sb = new System.Text.StringBuilder(); i++;
                while (true)
                {
                    if (i >= s.Length) throw new AppException("A formula has text without a closing quote.");
                    if (s[i] == '"') { if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; } i++; break; }
                    sb.Append(s[i++]);
                }
                list.Add(new(T.Str, sb.ToString())); continue;
            }
            if (i + 1 < s.Length && (s.Substring(i, 2) is "<=" or ">=" or "<>")) { list.Add(new(T.Op, s.Substring(i, 2))); i += 2; continue; }
            if ("+-*/^&=<>%".Contains(c)) { list.Add(new(T.Op, c.ToString())); i++; continue; }
            if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                var st = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (i < s.Length && s[i] is 'e' or 'E' && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || s[i + 1] is '-' or '+')) { i += 2; while (i < s.Length && char.IsDigit(s[i])) i++; }
                list.Add(new(T.Num, s[st..i])); continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.')) i++;
                list.Add(new(T.Id, s[st..i])); continue;
            }
            throw new AppException($"A formula contains an invalid character '{c}'.");
        }
        return list;
    }

    private sealed class Parser(List<Tok> toks, string src)
    {
        private int pos;
        public bool AtEnd => pos >= toks.Count;
        public string Peek => AtEnd ? "end" : toks[pos].Text;
        private static int Prec(string op) => op switch { "=" or "<>" or "<" or ">" or "<=" or ">=" => 1, "&" => 2, "+" or "-" => 3, "*" or "/" => 4, "^" => 5, _ => -1 };

        public Node Expr(int depth)
        {
            if (depth > MaxDepth) throw new AppException("A formula is nested too deeply.");
            return Binary(1, depth);
        }

        private Node Binary(int minPrec, int depth)
        {
            var left = Unary(depth);
            while (!AtEnd && toks[pos].Kind == T.Op && Prec(toks[pos].Text) >= minPrec)
            {
                var op = toks[pos++].Text;
                var prec = Prec(op);
                var right = Binary(op == "^" ? prec : prec + 1, depth + 1);
                left = new BinaryNode(op, left, right);
            }
            return left;
        }

        private Node Unary(int depth)
        {
            if (!AtEnd && toks[pos].Kind == T.Op && toks[pos].Text is "-" or "+") { var op = toks[pos++].Text; return new UnaryNode(op, Unary(depth + 1)); }
            var n = Primary(depth);
            if (!AtEnd && toks[pos].Kind == T.Op && toks[pos].Text == "%") { pos++; n = new BinaryNode("/", n, new NumNode(100)); }
            return n;
        }

        private Node Primary(int depth)
        {
            if (AtEnd) throw new AppException($"Formula '{src}' ends unexpectedly.");
            var t = toks[pos++];
            switch (t.Kind)
            {
                case T.Num:
                    if (!decimal.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) throw new AppException($"'{t.Text}' is not a valid number.");
                    return new NumNode(d);
                case T.Str: return new TextNode(t.Text);
                case T.LParen:
                    var inner = Expr(depth + 1);
                    if (AtEnd || toks[pos++].Kind != T.RParen) throw new AppException($"Formula '{src}' has unbalanced parentheses.");
                    return inner;
                case T.Id:
                    if (!AtEnd && toks[pos].Kind == T.LParen)
                    {
                        pos++;
                        if (!Functions.TryGetValue(t.Text, out var arity)) throw new AppException($"Function {t.Text.ToUpperInvariant()} is not supported in form formulas.");
                        var args = new List<Node>();
                        if (!AtEnd && toks[pos].Kind == T.RParen) pos++;
                        else
                        {
                            while (true)
                            {
                                args.Add(Expr(depth + 1));
                                if (AtEnd) throw new AppException($"Formula '{src}' has unbalanced parentheses.");
                                var sep = toks[pos++];
                                if (sep.Kind == T.RParen) break;
                                if (sep.Kind != T.Comma) throw new AppException($"Formula '{src}' is not valid near '{sep.Text}'.");
                            }
                        }
                        if (args.Count < arity.Min || (arity.Max >= 0 && args.Count > arity.Max))
                            throw new AppException($"{t.Text.ToUpperInvariant()} takes {(arity.Min == arity.Max ? arity.Min.ToString() : arity.Max < 0 ? $"at least {arity.Min}" : $"{arity.Min} to {arity.Max}")} argument(s).");
                        return new CallNode(t.Text.ToUpperInvariant(), args);
                    }
                    if (t.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new BoolNode(true);
                    if (t.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new BoolNode(false);
                    return new RefNode(t.Text);
                default: throw new AppException($"Formula '{src}' is not valid near '{t.Text}'.");
            }
        }
    }

    // ---------------------------------------------------------------- evaluation
    private sealed class Ctx(Func<string, FValue?> resolve, DateOnly today)
    {
        private int steps;

        public FValue Eval(Node n, int depth)
        {
            if (++steps > 200_000 || depth > MaxDepth * 2) throw new AppException("A formula needs too much work to calculate.");
            switch (n)
            {
                case NumNode x: return FValue.Num(x.Value);
                case TextNode x: return FValue.Text(x.Value);
                case BoolNode x: return FValue.Bool(x.Value);
                case RefNode r: return resolve(r.Name) ?? throw new AppException($"Formula refers to '{r.Name}', which is not on this form.");
                case UnaryNode u:
                    {
                        var v = Eval(u.Operand, depth + 1);
                        if (v.IsError) return v;
                        if (v.IsEmpty) return FValue.Empty;
                        var num = v.AsNumber();
                        return num is null ? new FValue(FormulaError.Value) : FValue.Num(u.Op == "-" ? -num.Value : num.Value);
                    }
                case BinaryNode b: return Binary(b, depth);
                case CallNode c: return Call(c, depth);
            }
            throw new AppException("Unknown formula element.");
        }

        private FValue Binary(BinaryNode b, int depth)
        {
            var l = Eval(b.Left, depth + 1); var r = Eval(b.Right, depth + 1);
            if (l.IsError) return l; if (r.IsError) return r;
            if (b.Op == "&") return FValue.Text(l.AsText() + r.AsText());
            if (b.Op is "=" or "<>" or "<" or ">" or "<=" or ">=")
            {
                int cmp;
                var ln = l.AsNumber(); var rn = r.AsNumber();
                if (ln is not null && rn is not null && !l.IsEmpty && !r.IsEmpty) cmp = ln.Value.CompareTo(rn.Value);
                else cmp = string.Compare(l.AsText(), r.AsText(), StringComparison.OrdinalIgnoreCase);
                return FValue.Bool(b.Op switch { "=" => cmp == 0, "<>" => cmp != 0, "<" => cmp < 0, ">" => cmp > 0, "<=" => cmp <= 0, _ => cmp >= 0 });
            }
            if (l.IsEmpty || r.IsEmpty) return FValue.Empty; // arithmetic waits for both inputs
            var a = l.AsNumber(); var c = r.AsNumber();
            if (a is null || c is null) return new FValue(FormulaError.Value);
            return b.Op switch
            {
                "+" => FValue.Num(a.Value + c.Value),
                "-" => FValue.Num(a.Value - c.Value),
                "*" => FValue.Num(a.Value * c.Value),
                "/" => c.Value == 0 ? new FValue(FormulaError.Div0) : FValue.Num(a.Value / c.Value),
                "^" => Pow(a.Value, c.Value),
                _ => new FValue(FormulaError.Value),
            };
        }

        private static FValue Pow(decimal a, decimal b)
        {
            var r = Math.Pow((double)a, (double)b);
            return double.IsNaN(r) || double.IsInfinity(r) || Math.Abs(r) > 7.9e27 ? new FValue(FormulaError.Num) : FValue.Num((decimal)r);
        }

        private List<FValue> Flat(IEnumerable<Node> args, int depth)
        {
            var list = new List<FValue>();
            foreach (var a in args)
            {
                var v = Eval(a, depth + 1);
                list.AddRange(v.Raw is IReadOnlyList<FValue> l ? l : [v]);
                if (list.Count > MaxListItems) throw new AppException("A formula refers to too many values.");
            }
            return list;
        }

        private static List<decimal> Numbers(List<FValue> vals) => vals.Where(v => !v.IsEmpty && v.Raw is not bool).Select(v => v.AsNumber()).Where(x => x is not null).Select(x => x!.Value).ToList();

        private FValue Call(CallNode c, int depth)
        {
            FValue Arg(int i) => Eval(c.Args[i], depth + 1);
            decimal? NumArg(int i) => Arg(i).AsNumber();
            switch (c.Name)
            {
                case "IF": { var t = Arg(0); if (t.IsError) return t; return t.AsBool() ? Arg(1) : c.Args.Count > 2 ? Arg(2) : FValue.Bool(false); }
                case "IFS":
                    if (c.Args.Count % 2 != 0) throw new AppException("IFS needs condition and result pairs.");
                    for (var i = 0; i < c.Args.Count; i += 2) if (Arg(i).AsBool()) return Arg(i + 1);
                    return new FValue(FormulaError.NA);
                case "IFERROR": { var v = Arg(0); return v.IsError ? Arg(1) : v; }
                case "SWITCH":
                    {
                        var key = Arg(0).AsText();
                        int i = 1;
                        for (; i + 1 < c.Args.Count; i += 2) if (string.Equals(Arg(i).AsText(), key, StringComparison.OrdinalIgnoreCase)) return Arg(i + 1);
                        return i < c.Args.Count ? Arg(i) : new FValue(FormulaError.NA);
                    }
                case "CHOOSE": { var k = (int)(NumArg(0) ?? 0); return k >= 1 && k < c.Args.Count ? Arg(k) : new FValue(FormulaError.Value); }
                case "CELLS": return FValue.List(Flat(c.Args, depth));
                case "ISERROR": return FValue.Bool(Arg(0).IsError);
                case "NA": return new FValue(FormulaError.NA);
                case "COUNTIFS": case "SUMIFS": case "AVERAGEIFS": case "MAXIFS": case "MINIFS":
                    {
                        var counting = c.Name == "COUNTIFS";
                        var first = counting ? 0 : 1;
                        if ((c.Args.Count - first) % 2 != 0) throw new AppException($"{c.Name} needs range and criteria pairs.");
                        var target = counting ? null : Eval(c.Args[0], depth + 1).Items;
                        var pairs = new List<(IReadOnlyList<FValue> Range, string Crit)>();
                        for (var i = first; i < c.Args.Count; i += 2) pairs.Add((Eval(c.Args[i], depth + 1).Items, Eval(c.Args[i + 1], depth + 1).AsText()));
                        var n = target?.Count ?? pairs[0].Range.Count;
                        var picked = new List<decimal>(); var count = 0;
                        for (var ri = 0; ri < n; ri++)
                        {
                            if (!pairs.All(p => ri < p.Range.Count && Criteria(p.Range[ri], p.Crit))) continue;
                            count++;
                            if (target is not null && target[ri].AsNumber() is { } x && !target[ri].IsEmpty) picked.Add(x);
                        }
                        return c.Name switch
                        {
                            "COUNTIFS" => FValue.Num(count),
                            "SUMIFS" => FValue.Num(picked.Sum()),
                            "AVERAGEIFS" => picked.Count == 0 ? new FValue(FormulaError.Div0) : FValue.Num(picked.Average()),
                            "MAXIFS" => FValue.Num(picked.Count == 0 ? 0 : picked.Max()),
                            _ => FValue.Num(picked.Count == 0 ? 0 : picked.Min()),
                        };
                    }
                case "INDEX":
                    {
                        var list = Eval(c.Args[0], depth + 1).Items; var k = (int)(NumArg(1) ?? 0);
                        return k >= 1 && k <= list.Count ? list[k - 1] : new FValue(FormulaError.Value);
                    }
                case "MATCH":
                    {
                        var key = Arg(0); var list = Eval(c.Args[1], depth + 1).Items;
                        var mode = c.Args.Count > 2 ? (int)(NumArg(2) ?? 1) : 1;
                        if (mode == 0)
                        {
                            for (var i = 0; i < list.Count; i++)
                            {
                                var kn = key.AsNumber(); var ln = list[i].AsNumber();
                                if (kn is not null && ln is not null && !key.IsEmpty && !list[i].IsEmpty ? kn == ln : string.Equals(list[i].AsText(), key.AsText(), StringComparison.OrdinalIgnoreCase)) return FValue.Num(i + 1);
                            }
                            return new FValue(FormulaError.NA);
                        }
                        // approximate match on a sorted list: largest value <= key (mode 1) or smallest value >= key (mode -1)
                        var kv = key.AsNumber(); if (kv is null) return new FValue(FormulaError.NA);
                        var best = -1;
                        for (var i = 0; i < list.Count; i++)
                            if (list[i].AsNumber() is { } v && !list[i].IsEmpty && (mode > 0 ? v <= kv : v >= kv)) best = i;
                        return best < 0 ? new FValue(FormulaError.NA) : FValue.Num(best + 1);
                    }
            }
            var vals = Flat(c.Args, depth);
            var err = vals.FirstOrDefault(v => v.IsError);
            if (err.IsError && c.Name is not ("ISBLANK" or "ISNUMBER" or "COUNTBLANK" or "COUNTA")) return err;
            var nums = Numbers(vals);
            switch (c.Name)
            {
                case "SUM": return FValue.Num(nums.Sum());
                case "PRODUCT": return nums.Count == 0 ? FValue.Num(0) : FValue.Num(nums.Aggregate(1m, (a, b) => a * b));
                case "AVERAGE": return nums.Count == 0 ? new FValue(FormulaError.Div0) : FValue.Num(nums.Average());
                case "MIN": return nums.Count == 0 ? FValue.Num(0) : FValue.Num(nums.Min());
                case "MAX": return nums.Count == 0 ? FValue.Num(0) : FValue.Num(nums.Max());
                case "RANGE": return nums.Count == 0 ? FValue.Num(0) : FValue.Num(nums.Max() - nums.Min());
                case "COUNT": return FValue.Num(nums.Count);
                case "COUNTA": return FValue.Num(vals.Count(v => !v.IsEmpty));
                case "COUNTBLANK": return FValue.Num(vals.Count(v => v.IsEmpty));
                case "MEDIAN":
                    {
                        if (nums.Count == 0) return new FValue(FormulaError.Num);
                        var s = nums.OrderBy(x => x).ToList();
                        return FValue.Num(s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2);
                    }
                case "STDEV": case "STDEVP": case "VAR":
                    {
                        var sample = c.Name != "STDEVP";
                        if (nums.Count < (sample ? 2 : 1)) return new FValue(FormulaError.Div0);
                        var mean = nums.Average();
                        var ss = nums.Sum(x => (x - mean) * (x - mean));
                        var variance = ss / (sample ? nums.Count - 1 : nums.Count);
                        return c.Name == "VAR" ? FValue.Num(variance) : FValue.Num((decimal)Math.Sqrt((double)variance));
                    }
                case "COUNTIF": case "SUMIF": case "AVERAGEIF":
                    {
                        var range = Eval(c.Args[0], depth + 1).Items;
                        var crit = Eval(c.Args[1], depth + 1).AsText();
                        var sumRange = c.Args.Count > 2 ? Eval(c.Args[2], depth + 1).Items : range;
                        var picked = new List<decimal>(); var count = 0;
                        for (var ri = 0; ri < range.Count; ri++)
                            if (Criteria(range[ri], crit)) { count++; if (ri < sumRange.Count && sumRange[ri].AsNumber() is { } n) picked.Add(n); }
                        return c.Name switch
                        {
                            "COUNTIF" => FValue.Num(count),
                            "SUMIF" => FValue.Num(picked.Sum()),
                            _ => picked.Count == 0 ? new FValue(FormulaError.Div0) : FValue.Num(picked.Average()),
                        };
                    }
                case "AND": return FValue.Bool(vals.Where(v => !v.IsEmpty).All(v => v.AsBool()));
                case "OR": return FValue.Bool(vals.Any(v => v.AsBool()));
                case "XOR": return FValue.Bool(vals.Count(v => v.AsBool()) % 2 == 1);
                case "NOT": return FValue.Bool(!vals.FirstOrDefault().AsBool());
                case "ISBLANK": return FValue.Bool(vals.Count == 0 || vals.All(v => v.IsEmpty));
                case "ISTEXT": return FValue.Bool(vals.Count == 1 && vals[0].Raw is string { Length: > 0 } t && decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out _) == false);
                case "DATE":
                    {
                        if (NumArg(0) is not { } y || NumArg(1) is not { } mo || NumArg(2) is not { } dd) return new FValue(FormulaError.Value);
                        try { return FValue.Text(new DateOnly((int)y, 1, 1).AddMonths((int)mo - 1).AddDays((int)dd - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); }
                        catch (ArgumentOutOfRangeException) { return new FValue(FormulaError.Num); }
                    }
                case "LARGE": case "SMALL":
                    {
                        var data = Numbers(Eval(c.Args[0], depth + 1).Items.ToList()); var k = (int)(NumArg(1) ?? 0);
                        if (k < 1 || k > data.Count) return new FValue(FormulaError.Num);
                        var sorted = c.Name == "LARGE" ? data.OrderByDescending(x => x).ToList() : data.OrderBy(x => x).ToList();
                        return FValue.Num(sorted[k - 1]);
                    }
                case "MID":
                    {
                        var s = Arg(0).AsText(); var st = (int)(NumArg(1) ?? 1); var len = (int)(NumArg(2) ?? 0);
                        if (st < 1 || len < 0) return new FValue(FormulaError.Value);
                        return st > s.Length ? FValue.Text("") : FValue.Text(s.Substring(st - 1, Math.Min(len, s.Length - st + 1)));
                    }
                case "SUBSTITUTE":
                    {
                        var s = Arg(0).AsText(); var from = Arg(1).AsText();
                        return from.Length == 0 ? FValue.Text(s) : FValue.Text(s.Replace(from, Arg(2).AsText(), StringComparison.Ordinal));
                    }
                case "ISNUMBER": return FValue.Bool(vals.Count == 1 && !vals[0].IsEmpty && vals[0].AsNumber() is not null && vals[0].Raw is not bool);
                case "ROUND": case "ROUNDUP": case "ROUNDDOWN": case "TRUNC":
                    {
                        var x = NumArg(0); if (x is null) return Arg(0).IsEmpty ? FValue.Empty : new FValue(FormulaError.Value);
                        var digits = c.Args.Count > 1 ? (int)(NumArg(1) ?? 0) : 0;
                        digits = Math.Clamp(digits, -10, 10);
                        var f = (decimal)Math.Pow(10, digits);
                        return c.Name switch
                        {
                            "ROUND" => FValue.Num(digits >= 0 ? Math.Round(x.Value, digits, MidpointRounding.AwayFromZero) : Math.Round(x.Value * f, MidpointRounding.AwayFromZero) / f),
                            "ROUNDUP" => FValue.Num((x.Value >= 0 ? Math.Ceiling(x.Value * f) : Math.Floor(x.Value * f)) / f),
                            _ => FValue.Num(Math.Truncate(x.Value * f) / f),
                        };
                    }
                case "INT": return NumArg(0) is { } i ? FValue.Num(Math.Floor(i)) : new FValue(FormulaError.Value);
                case "ABS": return NumArg(0) is { } a ? FValue.Num(Math.Abs(a)) : Arg(0).IsEmpty ? FValue.Empty : new FValue(FormulaError.Value);
                case "SIGN": return NumArg(0) is { } sg ? FValue.Num(Math.Sign(sg)) : new FValue(FormulaError.Value);
                case "SQRT": return NumArg(0) is { } q ? q < 0 ? new FValue(FormulaError.Num) : FValue.Num((decimal)Math.Sqrt((double)q)) : new FValue(FormulaError.Value);
                case "POWER": return NumArg(0) is { } pb && NumArg(1) is { } pe ? Pow(pb, pe) : new FValue(FormulaError.Value);
                case "MOD": return NumArg(0) is { } m1 && NumArg(1) is { } m2 ? m2 == 0 ? new FValue(FormulaError.Div0) : FValue.Num(m1 - m2 * Math.Floor(m1 / m2)) : new FValue(FormulaError.Value);
                case "CEILING": case "FLOOR":
                    {
                        var x = NumArg(0); var sig = c.Args.Count > 1 ? NumArg(1) ?? 1 : 1;
                        if (x is null) return new FValue(FormulaError.Value); if (sig == 0) return FValue.Num(0);
                        return FValue.Num((c.Name == "CEILING" ? Math.Ceiling(x.Value / sig) : Math.Floor(x.Value / sig)) * sig);
                    }
                case "PI": return FValue.Num((decimal)Math.PI);
                case "CONCAT": return FValue.Text(string.Concat(vals.Select(v => v.AsText())));
                case "LEN": return FValue.Num(Arg(0).AsText().Length);
                case "UPPER": return FValue.Text(Arg(0).AsText().ToUpperInvariant());
                case "LOWER": return FValue.Text(Arg(0).AsText().ToLowerInvariant());
                case "TRIM": return FValue.Text(System.Text.RegularExpressions.Regex.Replace(Arg(0).AsText().Trim(), " +", " "));
                case "LEFT": case "RIGHT":
                    {
                        var s = Arg(0).AsText(); var n = Math.Clamp((int)(c.Args.Count > 1 ? NumArg(1) ?? 1 : 1), 0, s.Length);
                        return FValue.Text(c.Name == "LEFT" ? s[..n] : s[(s.Length - n)..]);
                    }
                case "EXACT": return FValue.Bool(Arg(0).AsText() == Arg(1).AsText());
                case "VALUE": return NumArg(0) is { } vv ? FValue.Num(vv) : new FValue(FormulaError.Value);
                case "TEXT":
                    {
                        var x = NumArg(0); var fmt = Arg(1).AsText();
                        if (x is null) return FValue.Text(Arg(0).AsText());
                        if (fmt.Length > 40) return new FValue(FormulaError.Value);
                        try { return FValue.Text(x.Value.ToString(fmt, CultureInfo.InvariantCulture)); }
                        catch (FormatException) { return new FValue(FormulaError.Value); }
                    }
                case "TODAY": return FValue.Text(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                case "DAYS":
                    {
                        if (Date(Arg(0)) is not { } end || Date(Arg(1)) is not { } start) return Arg(0).IsEmpty || Arg(1).IsEmpty ? FValue.Empty : new FValue(FormulaError.Value);
                        return FValue.Num(end.DayNumber - start.DayNumber);
                    }
                case "DATEADD": case "EDATE":
                    {
                        if (Date(Arg(0)) is not { } d) return Arg(0).IsEmpty ? FValue.Empty : new FValue(FormulaError.Value);
                        var n = (int)(NumArg(1) ?? 0);
                        return FValue.Text((c.Name == "EDATE" ? d.AddMonths(n) : d.AddDays(n)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                case "YEAR": case "MONTH": case "DAY":
                    {
                        if (Date(Arg(0)) is not { } d) return Arg(0).IsEmpty ? FValue.Empty : new FValue(FormulaError.Value);
                        return FValue.Num(c.Name == "YEAR" ? d.Year : c.Name == "MONTH" ? d.Month : d.Day);
                    }
                case "WITHIN":
                    {
                        // WITHIN(value, lower, upper): true when lower <= value <= upper; empty value -> empty
                        var x = Arg(0); if (x.IsEmpty) return FValue.Empty;
                        var v = x.AsNumber(); var lo = NumArg(1); var hi = NumArg(2);
                        if (v is null) return new FValue(FormulaError.Value);
                        return FValue.Bool((lo is null || v >= lo) && (hi is null || v <= hi));
                    }
                case "CP": case "CPK":
                    {
                        // CP(values, lsl, usl) / CPK(values, lsl, usl) using the sample standard deviation
                        var data = Numbers(Eval(c.Args[0], depth + 1).Items.ToList());
                        var lsl = NumArg(1); var usl = NumArg(2);
                        if (data.Count < 2 || lsl is null || usl is null) return new FValue(FormulaError.Div0);
                        var mean = data.Average();
                        var sd = (decimal)Math.Sqrt((double)(data.Sum(x => (x - mean) * (x - mean)) / (data.Count - 1)));
                        if (sd == 0) return new FValue(FormulaError.Div0);
                        return FValue.Num(c.Name == "CP" ? (usl.Value - lsl.Value) / (6 * sd) : Math.Min(usl.Value - mean, mean - lsl.Value) / (3 * sd));
                    }
            }
            throw new AppException($"Function {c.Name} is not supported.");
        }

        private static DateOnly? Date(FValue v) =>
            DateOnly.TryParseExact(v.AsText().Length >= 10 ? v.AsText()[..10] : v.AsText(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        private static bool Criteria(FValue v, string crit)
        {
            var ops = new[] { ">=", "<=", "<>", ">", "<", "=" };
            var op = ops.FirstOrDefault(crit.StartsWith) ?? "=";
            var rhs = crit.StartsWith(op) ? crit[op.Length..] : crit;
            var vn = v.AsNumber();
            if (decimal.TryParse(rhs, NumberStyles.Number, CultureInfo.InvariantCulture, out var rn) && vn is not null)
            {
                var cmp = vn.Value.CompareTo(rn);
                return op switch { ">=" => cmp >= 0, "<=" => cmp <= 0, "<>" => cmp != 0, ">" => cmp > 0, "<" => cmp < 0, _ => cmp == 0 };
            }
            var eq = string.Equals(v.AsText(), rhs, StringComparison.OrdinalIgnoreCase);
            return op == "<>" ? !eq : op == "=" && eq;
        }
    }
}
