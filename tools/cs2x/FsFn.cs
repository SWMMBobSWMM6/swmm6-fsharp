// FsFn.cs - method bodies. Each C# body (an IOperation tree) is first built into a small statement tree (blocks, lets,
// ifs, loops, switches, trys and jumps) and then restructured and printed, because F# has no return, break, continue or
// goto and an if, a loop or a match is an expression:
//
//  1. Code after a statement that cannot complete normally is dropped (as C#'s compiler drops it).
//  2. In `if (c) <jump>; rest`, where one branch cannot complete and the other can, the rest of the block moves into
//     the branch that completes: `if c then <jump> else rest`. This turns most early returns and continues into
//     structure; a chain of them becomes if ... elif ... else.
//  3. A jump in tail position for its target needs nothing: a return whose value is the value of the method's last
//     expression, a continue at the end of a loop body, the last break of a switch section. Every other jump sets a
//     flag (ret', brk'N, cnt'N, sw'N, jmp'N; a return also stores its value in rv'), and the statements the jump
//     skips are wrapped in `if not (flags) then`; in a block whose value is the method's result, the wrapper is
//     `if ret' then rv' else rest`.
//  4. A loop tests its flags before C#'s condition, `while not brk'3 && not ret' && cond do`, so the condition is not
//     evaluated after a break; a for loop's step runs after a continue but not after a break or a return; a do-while
//     loop evaluates its condition at the end of the body through go'N.
//  5. switch becomes match on constants (enums through their underlying values), default last; C# sections never fall
//     through. goto (seven, in UebReference, every label in the method's top-level block) becomes a dispatch loop:
//     `while st'N >= 0 do match st'N with | 0 -> segment 0 ...`, a goto setting st'N (and jmp'N unless it is the last
//     statement of its segment).
//
// Expressions are F# expressions (F# has sequencing, let and if inside expressions), so the only thing hoisted out of
// an expression is a lambda with a statement body, which is bound to a name just before the statement that uses it
// (creating a delegate has no side effects).
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

public static partial class Cs2Fs
{
    // ------------------------------------------------------------------------------------------------ names of locals
    static readonly Dictionary<ISymbol, string> localNames = new(SymbolEqualityComparer.Default);

    static string LocalName(ISymbol s)
    {
        if (localNames.TryGetValue(s, out var n)) return n;
        n = s.Name.TrimStart('@');
        if (n.Length == 0 || n == "_") n = "__";
        if (s is IParameterSymbol && n.Length > 0 && char.IsUpper(n[0])) n = "p_" + n;     // lessons/34 of the SWMM5 ports
        if (FsShadow.Contains(n)) n += "_";
        n = Fs(n);
        localNames[s] = n;
        return n;
    }

    static int tmpCounter = 0;
    static string Tmp(string kind) => kind + "'" + (++tmpCounter);

    // ------------------------------------------------------------------------------------------------ the statement tree
    abstract class Node { public List<string> Pre = new(); }
    sealed class NLine : Node { public string Text = ""; }
    sealed class NDecl : Node { public string Text = ""; }
    sealed class NBlock : Node { public List<Node> Items = new(); }
    sealed class NIf : Node { public string Cond = ""; public NBlock Then = new(); public NBlock? Else; }
    sealed class NLoop : Node { public int Id; public string? Cond; public NBlock Body = new(); public NBlock? Step; public bool DoWhile; }
    sealed class NSection { public List<string> Labels = new(); public bool IsDefault; public NBlock Body = new(); }
    sealed class NSwitch : Node { public int Id; public string Value = ""; public List<NSection> Sections = new(); }
    sealed class NCatch { public string Pat = ""; public NBlock Body = new(); }
    sealed class NTry : Node { public NBlock Body = new(); public List<NCatch> Catches = new(); public NBlock? Finally; }
    enum JK { Return, Break, Continue, Goto, Exit }
    sealed class NJump : Node { public JK Kind; public int Target; public int Label; public string? Value; }
    sealed class NThrow : Node { public string? Expr; }
    sealed class NLabel : Node { public int Label; }
    sealed class NDispatch : Node { public int Id; public List<NBlock> Segments = new(); }

    sealed class Tail
    {
        public bool Ret;          // falling off the end returns from the function
        public bool Valued;       // ... and this block must give the function's value
        public int Cont = -1;     // falling off the end continues loop (or dispatch) Cont
        public int Brk = -1;      // falling off the end leaves switch Brk
        public static readonly Tail None = new();
        public Tail With(int brk) => new Tail { Ret = Ret, Valued = Valued, Cont = Cont, Brk = brk };
    }

    /// <summary>The F# text of one function body (method, constructor, lambda, accessor) at a given indentation.</summary>
    static List<string> FunctionBody(IMethodSymbol m, IOperation body, int ind)
    {
        var fn = new Fn(m, ind);
        return fn.Body(body);
    }

    sealed class Fn
    {
        public readonly IMethodSymbol? Method;
        public readonly int Indent;
        public List<string> Pre = new();                 // statement lambdas hoisted out of the expression being written
        bool usesRet, usesRv;
        readonly bool isVoid, isRefReturn;
        readonly ITypeSymbol? retType;
        int nextId = 0;
        readonly Dictionary<ILabelSymbol, (JK kind, int id)> labelTargets = new(SymbolEqualityComparer.Default);
        readonly Dictionary<ILabelSymbol, int> gotoLabels = new(SymbolEqualityComparer.Default);
        int dispatchId = -1;
        readonly Stack<string> condAccess = new();
        bool hoist;                                       // locals declared at the top (a method with goto)
        readonly List<string> hoisted = new();
        string? implicitReceiver;

        public Fn(IMethodSymbol? m, int indent)
        {
            Method = m;
            Indent = indent;
            isVoid = m == null || m.ReturnsVoid || m.MethodKind == MethodKind.Constructor;
            isRefReturn = m != null && (m.ReturnsByRef || m.ReturnsByRefReadonly);
            retType = m?.ReturnType;
        }

        // ---------------------------------------------------------------------------------------- body
        public List<string> Body(IOperation body)
        {
            var root = new NBlock();
            // labels at the top level: the body becomes a dispatch loop
            var top = body is IBlockOperation bo ? bo.Operations : ImmutableArray.Create(body);
            var labels = top.OfType<ILabeledOperation>().ToList();
            foreach (var l in body.DescendantsAndSelf().OfType<ILabeledOperation>())
                if (!labels.Contains(l)) Todo("label below the top level of a method");
            if (labels.Count > 0)
            {
                // a local declared in one segment may be used in another: every local of the method is declared
                // before the dispatch loop, and its declaration becomes an assignment
                hoist = true;
                foreach (var vd in body.Descendants().OfType<IVariableDeclaratorOperation>())
                {
                    bool inLambda = false; for (var pa = vd.Parent; pa != null; pa = pa.Parent) if (pa is IAnonymousFunctionOperation) { inLambda = true; break; }
                    if (inLambda) continue;
                    var ls = vd.Symbol;
                    if (ls.RefKind != RefKind.None) Todo("ref local in a method with goto");
                    hoisted.Add(Ind(Indent) + (refCells.Contains(ls) ? "let " + LocalName(ls) + " : " + T(ls.Type) + " ref = ref (" + DefaultOf(ls.Type) + ")" : "let mutable " + LocalName(ls) + " : " + T(ls.Type) + " = " + DefaultOf(ls.Type)));
                }
                dispatchId = nextId++;
                int k = 1;
                foreach (var l in labels) gotoLabels[l.Label] = k++;
                var d = new NDispatch { Id = dispatchId };
                var seg = new NBlock();
                d.Segments.Add(seg);
                foreach (var s in top)
                {
                    if (s is ILabeledOperation lo)
                    {
                        seg = new NBlock();
                        d.Segments.Add(seg);
                        if (lo.Operation != null) Stmt(lo.Operation, seg.Items);
                    }
                    else Stmt(s, seg.Items);
                }
                // each segment that can complete goes on to the next one, the last leaves the loop
                for (int i = 0; i < d.Segments.Count; i++)
                    if (CanComplete(d.Segments[i]))
                        d.Segments[i].Items.Add(i + 1 < d.Segments.Count ? new NJump { Kind = JK.Goto, Target = dispatchId, Label = i + 1 } : new NJump { Kind = JK.Exit, Target = dispatchId });
                root.Items.Add(d);
                Stat("goto: methods made into a dispatch loop");
            }
            else Stmt(body, root.Items);
            var outl = new List<string>();
            var tail = new Tail { Ret = true, Valued = !isVoid };
            EmitList(root.Items, Indent, tail, outl);
            // prologue: parameter copies and the return flags
            var pro = new List<string>();
            if (Method != null)
                foreach (var p in Method.Parameters)
                {
                    if (p.RefKind != RefKind.None) continue;
                    var n = LocalName(p);
                    if (refCells.Contains(p)) pro.Add(Ind(Indent) + "let " + n + " = ref " + n);
                    else if (assignedSyms.Contains(p) || addrSyms.Contains(p)) pro.Add(Ind(Indent) + "let mutable " + n + " = " + n);
                }
            pro.AddRange(hoisted);
            if (usesRet) pro.Add(Ind(Indent) + "let mutable ret' = false");
            if (usesRv)
            {
                if (isRefReturn) Todo("a ref-returning method with a return that is not in tail position");
                pro.Add(Ind(Indent) + "let mutable rv' = " + DefaultOf(retType!));
            }
            pro.AddRange(outl);
            return pro;
        }

        // ---------------------------------------------------------------------------------------- building the tree
        NBlock Block(IOperation? op)
        {
            var b = new NBlock();
            if (op != null) Stmt(op, b.Items);
            return b;
        }

        List<string> TakePre() { var p = Pre; Pre = new List<string>(); return p; }

        void Stmt(IOperation op, List<Node> into)
        {
            switch (op)
            {
                case IBlockOperation b:
                    for (int i = 0; i < b.Operations.Length; i++)
                    {
                        var s = b.Operations[i];
                        if (s is IUsingDeclarationOperation ud)
                        {
                            // using var x = e; rest  ->  let x = e; try rest finally dispose x (C#'s scope is the block)
                            UsingDecl(ud, b.Operations.Skip(i + 1).ToList(), into);
                            return;
                        }
                        Stmt(s, into);
                    }
                    return;
                case IVariableDeclarationGroupOperation g:
                    foreach (var d in g.Declarations)
                        foreach (var v in d.Declarators) into.Add(Decl(v, d.Initializer));
                    return;
                case IExpressionStatementOperation es:
                    if (es.Operation is IThrowOperation th) { into.Add(Throw(th)); return; }
                    {
                        var t = ExprStmt(es.Operation);
                        into.Add(new NLine { Pre = TakePre(), Text = t });
                    }
                    return;
                case IThrowOperation th2: into.Add(Throw(th2)); return;
                case IReturnOperation r:
                    {
                        string? v = null;
                        if (r.ReturnedValue != null)
                            v = isRefReturn ? AddrExpr(r.ReturnedValue) : E(r.ReturnedValue);
                        into.Add(new NJump { Kind = JK.Return, Value = v, Pre = TakePre() });
                    }
                    return;
                case IBranchOperation br:
                    {
                        if (br.BranchKind == BranchKind.GoTo)
                        {
                            if (!gotoLabels.TryGetValue(br.Target, out var lab)) { Todo("goto to an unknown label"); return; }
                            into.Add(new NJump { Kind = JK.Goto, Target = dispatchId, Label = lab });
                            return;
                        }
                        if (!labelTargets.TryGetValue(br.Target, out var tg)) { Todo("branch to an unknown target " + br.BranchKind); return; }
                        into.Add(new NJump { Kind = tg.kind, Target = tg.id });
                    }
                    return;
                case IConditionalOperation c:
                    {
                        var cond = E(c.Condition);
                        var n = new NIf { Pre = TakePre(), Cond = cond, Then = Block(c.WhenTrue) };
                        if (c.WhenFalse != null) n.Else = Block(c.WhenFalse);
                        into.Add(n);
                    }
                    return;
                case IWhileLoopOperation w:
                    {
                        int id = nextId++;
                        labelTargets[w.ExitLabel] = (JK.Break, id);
                        labelTargets[w.ContinueLabel] = (JK.Continue, id);
                        string? cond = null;
                        var pre = new List<string>();
                        if (w.Condition != null && !(w.Condition.ConstantValue.HasValue && w.Condition.ConstantValue.Value is true && !w.ConditionIsUntil))
                        {
                            cond = E(w.Condition);
                            if (w.ConditionIsUntil) cond = "not " + P(cond);
                            pre = TakePre();
                        }
                        var n = new NLoop { Id = id, Cond = cond, Pre = pre, Body = Block(w.Body), DoWhile = !w.ConditionIsTop };
                        if (w.IgnoredCondition != null) Todo("while loop with an ignored condition");
                        into.Add(n);
                    }
                    return;
                case IForLoopOperation f:
                    {
                        int id = nextId++;
                        labelTargets[f.ExitLabel] = (JK.Break, id);
                        labelTargets[f.ContinueLabel] = (JK.Continue, id);
                        foreach (var b0 in f.Before) Stmt(b0, into);
                        string? cond = null;
                        var pre = new List<string>();
                        if (f.Condition != null && !(f.Condition.ConstantValue.HasValue && f.Condition.ConstantValue.Value is true))
                        {
                            cond = E(f.Condition);
                            pre = TakePre();
                        }
                        var step = new NBlock();
                        foreach (var s in f.AtLoopBottom) Stmt(s, step.Items);
                        var n = new NLoop { Id = id, Cond = cond, Pre = pre, Body = Block(f.Body), Step = step.Items.Count > 0 ? step : null };
                        into.Add(n);
                    }
                    return;
                case IForEachLoopOperation fe:
                    ForEach(fe, into);
                    return;
                case ISwitchOperation sw:
                    Switch(sw, into);
                    return;
                case ITryOperation tr:
                    {
                        var n = new NTry { Body = Block(tr.Body) };
                        foreach (var cc in tr.Catches)
                        {
                            if (cc.Filter != null) Todo("catch with a filter");
                            var c = new NCatch();
                            var et = cc.ExceptionType;
                            string? vn = null;
                            if (cc.ExceptionDeclarationOrExpression is IVariableDeclaratorOperation vd) vn = LocalName(vd.Symbol);
                            if (et == null || et.ToDisplayString() == "System.Exception") c.Pat = vn ?? "_";
                            else c.Pat = ":? " + T(et) + (vn != null ? " as " + vn : "");
                            if (vn != null && cc.ExceptionDeclarationOrExpression is IVariableDeclaratorOperation vd2 && (assignedSyms.Contains(vd2.Symbol) || refCells.Contains(vd2.Symbol))) Todo("assigned catch variable");
                            c.Body = Block(cc.Handler);
                            n.Catches.Add(c);
                        }
                        if (tr.Finally != null) n.Finally = Block(tr.Finally);
                        into.Add(n);
                    }
                    return;
                case IUsingOperation us:
                    {
                        // using (x = e) body  ->  let x = e in try body finally dispose
                        var inner = new List<Node>();
                        string res;
                        if (us.Resources is IVariableDeclarationGroupOperation vg)
                        {
                            var decls = vg.Declarations.SelectMany(d => d.Declarators).ToList();
                            if (decls.Count != 1) Todo("using with several resources");
                            into.Add(Decl(decls[0], decls[0].Initializer ?? vg.Declarations[0].Initializer));
                            res = LocalName(decls[0].Symbol);
                        }
                        else
                        {
                            res = Tmp("u");
                            into.Add(new NDecl { Text = "let " + res + " = " + E(us.Resources), Pre = TakePre() });
                        }
                        var tn = new NTry { Body = Block(us.Body), Finally = new NBlock() };
                        tn.Finally.Items.Add(new NLine { Text = "if not (obj.ReferenceEquals(" + res + ", null)) then (" + res + " :> System.IDisposable).Dispose()" });
                        into.Add(tn);
                    }
                    return;
                case ILockOperation lk:
                    into.Add(new NLine { Text = E(lk.LockedValue) + " |> ignore", Pre = TakePre() });
                    Stmt(lk.Body, into);
                    return;
                case IEmptyOperation: return;
                case ILabeledOperation lo2: Todo("label below the top level"); return;
                case ILocalFunctionOperation: Todo("local function"); return;
                default:
                    Todo("statement " + op.Kind);
                    return;
            }
        }

        void UsingDecl(IUsingDeclarationOperation ud, List<IOperation> rest, List<Node> into)
        {
            var decls = ud.DeclarationGroup.Declarations.SelectMany(d => d.Declarators).ToList();
            foreach (var v in decls) into.Add(Decl(v, v.Initializer ?? ud.DeclarationGroup.Declarations[0].Initializer));
            var body = new NBlock();
            foreach (var s in rest)
            {
                if (s is IUsingDeclarationOperation ud2) { UsingDecl(ud2, rest.SkipWhile(x => x != s).Skip(1).ToList(), body.Items); break; }
                Stmt(s, body.Items);
            }
            var tn = new NTry { Body = body, Finally = new NBlock() };
            foreach (var v in Enumerable.Reverse(decls))
            {
                var n = LocalName(v.Symbol);
                var rd = refCells.Contains(v.Symbol) ? n + ".Value" : n;
                tn.Finally.Items.Add(new NLine { Text = "if not (obj.ReferenceEquals(" + rd + ", null)) then (" + rd + " :> System.IDisposable).Dispose()" });
            }
            into.Add(tn);
            Stat("using declarations");
        }

        NThrow Throw(IThrowOperation th)
        {
            if (th.Exception == null) return new NThrow { Expr = null };
            var e = E(th.Exception);
            return new NThrow { Expr = e, Pre = TakePre() };
        }

        Node Decl(IVariableDeclaratorOperation v, IVariableInitializerOperation? init)
        {
            var s = v.Symbol;
            var n = LocalName(s);
            var ini = v.Initializer ?? init;
            if (s.RefKind != RefKind.None)
            {
                if (ini == null) Todo("ref local without initializer");
                var a = AddrExpr(ini!.Value);
                return new NDecl { Text = "let " + n + " = " + a, Pre = TakePre() };
            }
            string val = ini != null ? E(ini.Value) : DefaultOf(s.Type);
            var pre = TakePre();
            if (hoist)
            {
                if (ini == null) return new NLine { Text = "()", Pre = pre };
                return new NLine { Text = (refCells.Contains(s) ? n + ".Value" : n) + " <- " + val, Pre = pre };
            }
            if (refCells.Contains(s)) return new NDecl { Text = "let " + n + " : " + T(s.Type) + " ref = ref (" + val + ")", Pre = pre };
            if (capturedSyms.Contains(s) && !assignedSyms.Contains(s) && !addrSyms.Contains(s)) return new NDecl { Text = "let " + n + " : " + T(s.Type) + " = " + val, Pre = pre };
            return new NDecl { Text = "let mutable " + n + " : " + T(s.Type) + " = " + val, Pre = pre };
        }

        void ForEach(IForEachLoopOperation fe, List<Node> into)
        {
            int id = nextId++;
            labelTargets[fe.ExitLabel] = (JK.Break, id);
            labelTargets[fe.ContinueLabel] = (JK.Continue, id);
            var ct = fe.Collection is IConversionOperation cv && cv.IsImplicit ? cv.Operand : fe.Collection;
            var elemT = fe.LoopControlVariable is IVariableDeclaratorOperation lv ? lv.Symbol.Type : null;
            if (elemT == null) { Todo("foreach without a declared variable"); return; }
            var en = Tmp("e");
            var coll = E(ct);
            var pre = TakePre();
            Use("foreach over " + ct.Type?.ToDisplayString());
            into.Add(new NDecl { Pre = pre, Text = "let " + en + " = (" + coll + " :> System.Collections.Generic.IEnumerable<" + T(elemT) + ">).GetEnumerator()" });
            var body = new NBlock();
            var vs = ((IVariableDeclaratorOperation)fe.LoopControlVariable).Symbol;
            body.Items.Add(new NDecl { Text = (refCells.Contains(vs) ? "let " + LocalName(vs) + " = ref (" + en + ".Current)" : "let " + LocalName(vs) + " : " + T(elemT) + " = " + en + ".Current") });
            Stmt(fe.Body, body.Items);
            into.Add(new NLoop { Id = id, Cond = en + ".MoveNext()", Body = body });
        }

        void Switch(ISwitchOperation sw, List<Node> into)
        {
            int id = nextId++;
            labelTargets[sw.ExitLabel] = (JK.Break, id);
            var vt = sw.Value.Type!;
            string val = E(sw.Value);
            bool isEnum = vt.TypeKind == TypeKind.Enum;
            if (isEnum) val = "(LanguagePrimitives.EnumToValue " + P(val) + ")";
            else if (!(IsPrimitiveNumeric(vt) || vt.SpecialType == SpecialType.System_String || vt.SpecialType == SpecialType.System_Boolean)) Todo("switch on " + vt.ToDisplayString());
            var n = new NSwitch { Id = id, Value = val, Pre = TakePre() };
            foreach (var c in sw.Cases)
            {
                var sec = new NSection();
                foreach (var cl in c.Clauses)
                {
                    if (cl is IDefaultCaseClauseOperation) sec.IsDefault = true;
                    else if (cl is ISingleValueCaseClauseOperation sv)
                    {
                        if (!sv.Value.ConstantValue.HasValue) Todo("case label that is not a constant");
                        var cvv = sv.Value.ConstantValue.Value;
                        var lt = isEnum ? ((INamedTypeSymbol)vt).EnumUnderlyingType! : vt;
                        sec.Labels.Add(Lit(cvv, lt, false));
                    }
                    else Todo("case clause " + cl.CaseKind);
                }
                foreach (var s in c.Body) Stmt(s, sec.Body.Items);
                n.Sections.Add(sec);
            }
            into.Add(n);
        }

        // ---------------------------------------------------------------------------------------- analysis of the tree
        readonly Dictionary<Node, bool> canComplete = new();

        bool CanComplete(Node n)
        {
            if (canComplete.TryGetValue(n, out var r)) return r;
            r = n switch
            {
                NLine or NDecl or NLabel => true,
                NJump or NThrow => false,
                NBlock b => b.Items.All(CanComplete),
                NIf i => CanComplete(i.Then) || i.Else == null || CanComplete(i.Else),
                NLoop l => l.Cond != null || HasJump(l.Body, JK.Break, l.Id),
                NSwitch s => !s.Sections.Any(x => x.IsDefault) || s.Sections.Any(x => CanComplete(x.Body)) || s.Sections.Any(x => HasJump(x.Body, JK.Break, s.Id)),
                NTry t => (CanComplete(t.Body) || t.Catches.Any(c => CanComplete(c.Body))) && (t.Finally == null || CanComplete(t.Finally)),
                NDispatch d => true,
                _ => true
            };
            canComplete[n] = r;
            return r;
        }

        static bool HasJump(Node n, JK k, int id) => n switch
        {
            NJump j => j.Kind == k && j.Target == id,
            NBlock b => b.Items.Any(x => HasJump(x, k, id)),
            NIf i => HasJump(i.Then, k, id) || (i.Else != null && HasJump(i.Else, k, id)),
            NLoop l => HasJump(l.Body, k, id) || (l.Step != null && HasJump(l.Step, k, id)),
            NSwitch s => s.Sections.Any(x => HasJump(x.Body, k, id)),
            NTry t => HasJump(t.Body, k, id) || t.Catches.Any(c => HasJump(c.Body, k, id)) || (t.Finally != null && HasJump(t.Finally, k, id)),
            NDispatch d => d.Segments.Any(x => HasJump(x, k, id)),
            _ => false
        };

        // ---------------------------------------------------------------------------------------- printing
        static void AddPre(List<string> pre, int ind, List<string> outl)
        {
            foreach (var p in pre) outl.Add(Ind(ind) + p);
        }

        /// <summary>Prints a list of statements; returns the flags its jumps may have set that the caller must test.</summary>
        HashSet<string> EmitList(List<Node> items0, int ind, Tail tail, List<string> outl)
        {
            var flags = new HashSet<string>();
            var items = Flatten(items0);
            if (items.Count == 0) { outl.Add(Ind(ind) + (tail.Valued ? Unreachable() : "()")); return flags; }
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                bool last = i == items.Count - 1;
                if (!last && s is NIf nif)
                {
                    bool tc = CanComplete(nif.Then), ec = nif.Else == null || CanComplete(nif.Else);
                    if (tc != ec)
                    {
                        var rest = items.Skip(i + 1).ToList();
                        var ni = new NIf { Pre = nif.Pre, Cond = nif.Cond, Then = nif.Then, Else = nif.Else };
                        if (tc) { var nb = new NBlock(); nb.Items.AddRange(nif.Then.Items); nb.Items.AddRange(rest); ni.Then = nb; }
                        else { var nb = new NBlock(); if (nif.Else != null) nb.Items.AddRange(nif.Else.Items); nb.Items.AddRange(rest); ni.Else = nb; }
                        Stat("early exits made into an else");
                        s = ni; last = true;
                    }
                }
                if (!last && !CanComplete(s))
                {
                    Stat("statements dropped after a jump", items.Count - i - 1);
                    last = true;
                }
                var (f, valued) = EmitStmt(s, ind, last ? tail : Tail.None, outl);
                if (last)
                {
                    if (tail.Valued && !valued)
                    {
                        if (CanComplete(s) && !f.Contains("ret'")) outl.Add(Ind(ind) + Unreachable());
                        else { outl.Add(Ind(ind) + "rv'"); usesRv = true; }
                    }
                    else if (s is NDecl) outl.Add(Ind(ind) + "()");
                    flags.UnionWith(f);
                    return flags;
                }
                if (f.Count > 0)
                {
                    var rest = items.Skip(i + 1).ToList();
                    if (tail.Valued)
                    {
                        if (!(f.Count == 1 && f.Contains("ret'"))) Todo("flags other than ret' before the value of a function");
                        outl.Add(Ind(ind) + "if ret' then rv'");
                        usesRv = true;
                        outl.Add(Ind(ind) + "else");
                        var rf = EmitList(rest, ind + 1, tail, outl);
                        flags.UnionWith(f); flags.UnionWith(rf);
                        Stat("statements guarded by flags");
                        return flags;
                    }
                    outl.Add(Ind(ind) + "if not (" + string.Join(" || ", f.OrderBy(x => x, StringComparer.Ordinal)) + ") then");
                    var rf2 = EmitList(rest, ind + 1, tail, outl);
                    flags.UnionWith(f); flags.UnionWith(rf2);
                    Stat("statements guarded by flags");
                    return flags;
                }
            }
            return flags;
        }

        string Unreachable() => "failwith \"unreachable\"";

        static List<Node> Flatten(List<Node> items)
        {
            if (!items.Any(x => x is NBlock)) return items;
            var r = new List<Node>();
            foreach (var x in items)
            {
                if (x is NBlock b) r.AddRange(Flatten(b.Items));
                else r.Add(x);
            }
            return r;
        }

        (HashSet<string> flags, bool valued) EmitStmt(Node s, int ind, Tail tail, List<string> outl)
        {
            var none = new HashSet<string>();
            switch (s)
            {
                case NLine l:
                    AddPre(l.Pre, ind, outl);
                    outl.Add(Ind(ind) + l.Text);
                    return (none, false);
                case NDecl d:
                    AddPre(d.Pre, ind, outl);
                    outl.Add(Ind(ind) + d.Text);
                    return (none, false);
                case NThrow t:
                    AddPre(t.Pre, ind, outl);
                    outl.Add(Ind(ind) + (t.Expr == null ? "reraise ()" : "raise (" + t.Expr + ")"));
                    return (none, tail.Valued);
                case NJump j:
                    return EmitJump(j, ind, tail, outl);
                case NIf i:
                    {
                        AddPre(i.Pre, ind, outl);
                        var flags = new HashSet<string>();
                        bool valued = true;
                        string kw = "if ";
                        NIf cur = i;
                        while (true)
                        {
                            outl.Add(Ind(ind) + kw + cur.Cond + " then");
                            flags.UnionWith(EmitList(cur.Then.Items, ind + 1, tail, outl));
                            if (cur.Else == null)
                            {
                                if (tail.Valued) { outl.Add(Ind(ind) + "else"); outl.Add(Ind(ind + 1) + Unreachable()); }
                                else valued = false;
                                break;
                            }
                            var ei = Flatten(cur.Else.Items);
                            if (ei.Count == 1 && ei[0] is NIf ni && ni.Pre.Count == 0) { cur = ni; kw = "elif "; continue; }
                            outl.Add(Ind(ind) + "else");
                            flags.UnionWith(EmitList(cur.Else.Items, ind + 1, tail, outl));
                            break;
                        }
                        return (flags, valued && tail.Valued);
                    }
                case NLoop lp:
                    return (EmitLoop(lp, ind, outl), false);
                case NSwitch sw:
                    return EmitSwitch(sw, ind, tail, outl);
                case NTry tr:
                    {
                        var flags = new HashSet<string>();
                        bool both = tr.Catches.Count > 0 && tr.Finally != null;
                        outl.Add(Ind(ind) + "try");
                        int bi = both ? ind + 2 : ind + 1;
                        if (both) outl.Add(Ind(ind + 1) + "try");
                        flags.UnionWith(EmitList(tr.Body.Items, bi, tail, outl));
                        if (tr.Catches.Count > 0)
                        {
                            int ci = both ? ind + 1 : ind;
                            outl.Add(Ind(ci) + "with");
                            foreach (var c in tr.Catches)
                            {
                                outl.Add(Ind(ci) + "| " + c.Pat + " ->");
                                flags.UnionWith(EmitList(c.Body.Items, ci + 1, tail, outl));
                            }
                        }
                        if (tr.Finally != null)
                        {
                            outl.Add(Ind(ind) + "finally");
                            var ff = EmitList(tr.Finally.Items, ind + 1, Tail.None, outl);
                            if (ff.Count > 0) Todo("jump out of a finally block");
                        }
                        return (flags, tail.Valued);
                    }
                case NDispatch dp:
                    return (EmitDispatch(dp, ind, outl), false);
                case NBlock b:
                    return (EmitList(b.Items, ind, tail, outl), tail.Valued);
                default:
                    Todo("node " + s.GetType().Name);
                    return (none, false);
            }
        }

        (HashSet<string>, bool) EmitJump(NJump j, int ind, Tail tail, List<string> outl)
        {
            var flags = new HashSet<string>();
            AddPre(j.Pre, ind, outl);
            switch (j.Kind)
            {
                case JK.Return:
                    if (tail.Ret)
                    {
                        Stat("returns: in tail position");
                        if (tail.Valued) { outl.Add(Ind(ind) + j.Value); return (flags, true); }
                        outl.Add(Ind(ind) + "()");
                        return (flags, false);
                    }
                    Stat("returns: by flag");
                    usesRet = true;
                    if (j.Value != null && !isVoid) { usesRv = true; outl.Add(Ind(ind) + "rv' <- " + j.Value); }
                    else if (j.Value != null) outl.Add(Ind(ind) + j.Value + " |> ignore");
                    outl.Add(Ind(ind) + "ret' <- true");
                    flags.Add("ret'");
                    return (flags, false);
                case JK.Break:
                    if (tail.Brk == j.Target) { Stat("breaks: last in a switch section"); outl.Add(Ind(ind) + "()"); return (flags, false); }
                    Stat("breaks: by flag");
                    var bf = (switchIds.Contains(j.Target) ? "sw'" : "brk'") + j.Target;
                    outl.Add(Ind(ind) + bf + " <- true");
                    flags.Add(bf);
                    return (flags, false);
                case JK.Continue:
                    if (tail.Cont == j.Target) { Stat("continues: at the end of the body"); outl.Add(Ind(ind) + "()"); return (flags, false); }
                    Stat("continues: by flag");
                    outl.Add(Ind(ind) + "cnt'" + j.Target + " <- true");
                    flags.Add("cnt'" + j.Target);
                    return (flags, false);
                case JK.Goto:
                    outl.Add(Ind(ind) + "st'" + j.Target + " <- " + j.Label);
                    if (tail.Cont == j.Target) { Stat("gotos: at the end of a segment"); return (flags, false); }
                    Stat("gotos: by flag");
                    outl.Add(Ind(ind) + "jmp'" + j.Target + " <- true");
                    flags.Add("jmp'" + j.Target);
                    return (flags, false);
                case JK.Exit:
                    outl.Add(Ind(ind) + "st'" + j.Target + " <- -1");
                    if (tail.Cont == j.Target) return (flags, false);
                    outl.Add(Ind(ind) + "jmp'" + j.Target + " <- true");
                    flags.Add("jmp'" + j.Target);
                    return (flags, false);
            }
            return (flags, false);
        }

        readonly HashSet<int> switchIds = new();

        HashSet<string> EmitLoop(NLoop lp, int ind, List<string> outl)
        {
            var body = new List<string>();
            int bind = ind + 1;
            var bf = EmitList(lp.Body.Items, bind, new Tail { Cont = lp.Id }, body);
            var brk = "brk'" + lp.Id;
            var cnt = "cnt'" + lp.Id;
            bool needBrk = bf.Contains(brk), needCnt = bf.Contains(cnt);
            var escaping = new HashSet<string>(bf.Where(f => f != brk && f != cnt));
            var stops = new List<string>();
            if (needBrk) stops.Add(brk);
            stops.AddRange(escaping.OrderBy(x => x, StringComparer.Ordinal));
            var guard = string.Join(" && ", stops.Select(x => "not " + x));
            AddPre(lp.Pre, ind, outl);
            if (needBrk) outl.Add(Ind(ind) + "let mutable " + brk + " = false");
            if (needCnt) outl.Add(Ind(ind) + "let mutable " + cnt + " = false");
            if (lp.DoWhile)
            {
                var go = "go'" + lp.Id;
                outl.Add(Ind(ind) + "let mutable " + go + " = true");
                outl.Add(Ind(ind) + "while " + go + " do");
                if (needCnt) outl.Add(Ind(bind) + cnt + " <- false");
                outl.AddRange(body);
                var c = lp.Cond ?? "true";
                outl.Add(Ind(bind) + go + " <- " + (guard.Length > 0 ? guard + " && " : "") + c);
                Stat("loops: do-while");
            }
            else
            {
                var c = lp.Cond != null ? (guard.Length > 0 ? guard + " && " + lp.Cond : lp.Cond) : (guard.Length > 0 ? guard : "true");
                outl.Add(Ind(ind) + "while " + c + " do");
                if (needCnt) outl.Add(Ind(bind) + cnt + " <- false");
                outl.AddRange(body);
                if (lp.Step != null)
                {
                    var sb = new List<string>();
                    if (stops.Count > 0)
                    {
                        outl.Add(Ind(bind) + "if not (" + string.Join(" || ", stops) + ") then");
                        var sf = EmitList(lp.Step.Items, bind + 1, Tail.None, outl);
                        if (sf.Count > 0) Todo("jump in a for loop's step");
                    }
                    else
                    {
                        var sf = EmitList(lp.Step.Items, bind, Tail.None, outl);
                        if (sf.Count > 0) Todo("jump in a for loop's step");
                    }
                }
                Stat("loops: while/for");
            }
            return escaping;
        }

        (HashSet<string>, bool) EmitSwitch(NSwitch sw, int ind, Tail tail, List<string> outl)
        {
            switchIds.Add(sw.Id);
            var flags = new HashSet<string>();
            var arms = new List<string>();
            bool valued = true;
            var secTail = tail.With(sw.Id);
            var ordered = sw.Sections.Where(x => !x.IsDefault).Concat(sw.Sections.Where(x => x.IsDefault)).ToList();
            foreach (var sec in ordered)
            {
                var pat = sec.IsDefault ? "_" : string.Join(" | ", sec.Labels);
                arms.Add(Ind(ind) + "| " + pat + " ->");
                var f = EmitList(sec.Body.Items, ind + 1, secTail, arms);
                flags.UnionWith(f);
            }
            if (!sw.Sections.Any(x => x.IsDefault))
            {
                arms.Add(Ind(ind) + "| _ -> " + (tail.Valued ? Unreachable() : "()"));
                if (tail.Valued) valued = false;
            }
            var swf = "sw'" + sw.Id;
            AddPre(sw.Pre, ind, outl);
            if (flags.Contains(swf)) outl.Add(Ind(ind) + "let mutable " + swf + " = false");
            outl.Add(Ind(ind) + "match " + sw.Value + " with");
            outl.AddRange(arms);
            flags.Remove(swf);
            Stat("switches");
            return (flags, valued && tail.Valued);
        }

        HashSet<string> EmitDispatch(NDispatch d, int ind, List<string> outl)
        {
            var arms = new List<string>();
            var flags = new HashSet<string>();
            var segTail = new Tail { Cont = d.Id };
            for (int k = 0; k < d.Segments.Count; k++)
            {
                arms.Add(Ind(ind + 1) + "| " + k + " ->");
                flags.UnionWith(EmitList(d.Segments[k].Items, ind + 2, segTail, arms));
            }
            arms.Add(Ind(ind + 1) + "| _ -> st'" + d.Id + " <- -1");
            var jmp = "jmp'" + d.Id;
            bool needJmp = flags.Remove(jmp);
            var stops = flags.OrderBy(x => x, StringComparer.Ordinal).ToList();
            outl.Add(Ind(ind) + "let mutable st'" + d.Id + " = 0");
            if (needJmp) outl.Add(Ind(ind) + "let mutable " + jmp + " = false");
            outl.Add(Ind(ind) + "while st'" + d.Id + " >= 0" + string.Concat(stops.Select(x => " && not " + x)) + " do");
            if (needJmp) outl.Add(Ind(ind + 1) + jmp + " <- false");
            outl.Add(Ind(ind + 1) + "match st'" + d.Id + " with");
            outl.AddRange(arms);
            return flags;
        }

        // ---------------------------------------------------------------------------------------- expressions
        /// <summary>An expression in a statement position: assignments and increments as F# assignments, calls whose
        /// value is not used piped to ignore.</summary>
        public string ExprStmt(IOperation op)
        {
            switch (op)
            {
                case ISimpleAssignmentOperation sa:
                    if (sa.Target is IDiscardOperation) return ExprStmt(sa.Value);
                    return Assign(sa, false);
                case ICompoundAssignmentOperation ca: return Compound(ca, false);
                case IIncrementOrDecrementOperation inc: return IncDec(inc, false);
                case IInvocationOperation inv:
                    {
                        var c = Invoke(inv);
                        return inv.TargetMethod.ReturnsVoid ? c : c + " |> ignore";
                    }
                case IConditionalAccessOperation ca2:
                    {
                        var t = Tmp("c");
                        var recv = E(ca2.Operation);
                        condAccess.Push(t);
                        var w = ExprStmt(ca2.WhenNotNull);
                        condAccess.Pop();
                        return "(let " + t + " = " + recv + " in if not (obj.ReferenceEquals(" + t + ", null)) then " + w + ")";
                    }
                case IConversionOperation cv when cv.Type?.SpecialType == SpecialType.System_Void: return ExprStmt(cv.Operand);
                case IParenthesizedOperation p: return ExprStmt(p.Operand);
                case IThrowOperation th: return "raise (" + E(th.Exception!) + ")";
                case IDeconstructionAssignmentOperation: return Todo("deconstruction");
                default:
                    return E(op) + " |> ignore";
            }
        }

        /// <summary>x as the argument of a prefix function (int, not, box, ...): parenthesized unless it is an atom,
        /// since F# reads `int a.M(b)` as two arguments.</summary>
        public static string P(string x)
        {
            if (x.Length == 0) return x;
            if (x[0] == '(' && Close(x, 0) == x.Length - 1) return x;
            if (x[0] == '-') return "(" + x + ")";
            int depth = 0;
            for (int i = 0; i < x.Length; i++)
            {
                char c = x[i];
                if (c == '"') { i++; while (i < x.Length && x[i] != '"') { if (x[i] == '\\') i++; i++; } continue; }
                if (c == '\'' && i + 2 < x.Length && (x[i + 2] == '\'' || x[i + 1] == '\\')) { int j = x.IndexOf('\'', i + 1 + (x[i + 1] == '\\' ? 2 : 1)); if (j > 0) { i = j; continue; } }
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (depth == 0 && (c == ' ' || c == '(')) return "(" + x + ")";
                else if (c == '(') depth++;
                else if (c == ')') depth--;
            }
            return x;
        }

        static int Close(string x, int open)
        {
            int depth = 0;
            for (int i = open; i < x.Length; i++)
            {
                char c = x[i];
                if (c == '"') { i++; while (i < x.Length && x[i] != '"') { if (x[i] == '\\') i++; i++; } continue; }
                if (c == '\'' && i + 2 < x.Length && (x[i + 2] == '\'' || x[i + 1] == '\\')) { int j = x.IndexOf('\'', i + 1 + (x[i + 1] == '\\' ? 2 : 1)); if (j > 0) { i = j; continue; } }
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        static bool IsVoidish(IOperation op) => op.Type == null || op.Type.SpecialType == SpecialType.System_Void;

        /// <summary>An expression's value as F# text (parenthesized unless it is an atom).</summary>
        public string E(IOperation op)
        {
            if (op.ConstantValue.HasValue && op.Type != null && op is not IDefaultValueOperation || op is ILiteralOperation)
            {
                if (op.Type == null && op.ConstantValue.HasValue && op.ConstantValue.Value == null) return "null";
                if (op.Type != null) return Lit(op.ConstantValue.Value, op.Type, true);
            }
            switch (op)
            {
                case IParenthesizedOperation p: return E(p.Operand);
                case ILocalReferenceOperation l:
                    return refCells.Contains(l.Local) ? LocalName(l.Local) + ".Value" : LocalName(l.Local);
                case IParameterReferenceOperation pr:
                    return refCells.Contains(pr.Parameter) ? LocalName(pr.Parameter) + ".Value" : LocalName(pr.Parameter);
                case IInstanceReferenceOperation ir:
                    if (ir.ReferenceKind == InstanceReferenceKind.ImplicitReceiver) return implicitReceiver ?? Todo("implicit receiver outside an initializer");
                    return "this";
                case IFieldReferenceOperation fr: return FieldRef(fr);
                case IPropertyReferenceOperation pr2: return PropRef(pr2);
                case IArrayElementReferenceOperation ae: return ArrayElem(ae);
                case IInvocationOperation inv: return Invoke(inv);
                case IObjectCreationOperation oc: return NewObj(oc);
                case IArrayCreationOperation ac: return NewArray(ac);
                case IConversionOperation cv: return Conv(cv);
                case IBinaryOperation b: return Binary(b);
                case IUnaryOperation u: return Unary(u);
                case IConditionalOperation c:
                    if (c.WhenFalse == null) return Todo("conditional expression without else");
                    return "(if " + E(c.Condition) + " then " + E(c.WhenTrue) + " else " + E(c.WhenFalse) + ")";
                case ISimpleAssignmentOperation sa: return Assign(sa, true);
                case ICompoundAssignmentOperation ca: return Compound(ca, true);
                case IIncrementOrDecrementOperation inc: return IncDec(inc, true);
                case IConditionalAccessOperation ca2:
                    {
                        var t = Tmp("c");
                        var recv = E(ca2.Operation);
                        condAccess.Push(t);
                        var w = E(ca2.WhenNotNull);
                        condAccess.Pop();
                        var rt = ca2.Type!;
                        if (rt.IsValueType) return Todo("?. giving a value type");
                        return "(let " + t + " = " + recv + " in if obj.ReferenceEquals(" + t + ", null) then " + NullOf(rt) + " else " + w + ")";
                    }
                case IConditionalAccessInstanceOperation: return condAccess.Count > 0 ? condAccess.Peek() : Todo("?. instance outside ?.");
                case IDefaultValueOperation dv: return DefaultOf(dv.Type!);
                case IDelegateCreationOperation dc: return Delegate(dc);
                case IIsTypeOperation it:
                    {
                        var r = "(box (" + E(it.ValueOperand) + ") :? " + T(it.TypeOperand) + ")";
                        return it.IsNegated ? "(not " + P(r) + ")" : r;
                    }
                case ITypeOfOperation to: return "typeof<" + T(to.TypeOperand) + ">";
                case IThrowOperation th: return "raise (" + E(th.Exception!) + ")";
                case ICoalesceOperation co:
                    {
                        var t = Tmp("c");
                        return "(let " + t + " = " + E(co.Value) + " in if obj.ReferenceEquals(" + t + ", null) then " + E(co.WhenNull) + " else " + t + ")";
                    }
                case ILiteralOperation lit: return Lit(lit.ConstantValue.Value, lit.Type, true);
                default:
                    return Todo("expression " + op.Kind);
            }
        }

        static string NullOf(ITypeSymbol t) =>
            t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Array || t.SpecialType == SpecialType.System_String || t.SpecialType == SpecialType.System_Object
                ? "null" : "Unchecked.defaultof<" + T(t) + ">";

        // ---------------------------------------------------------------------------------------- literals
        public string Lit(object? v, ITypeSymbol? t, bool paren)
        {
            if (v == null)
            {
                if (t == null) return "null";
                return NullOf(t);
            }
            if (t != null && t.TypeKind == TypeKind.Enum)
            {
                var et = (INamedTypeSymbol)t;
                foreach (var f in et.GetMembers().OfType<IFieldSymbol>())
                    if (f.HasConstantValue && Equals(f.ConstantValue, v)) return T(et) + "." + Fs(f.Name);
                return "(LanguagePrimitives.EnumOfValue<" + T(et.EnumUnderlyingType!) + ", " + T(et) + "> " + Lit(v, et.EnumUnderlyingType, true) + ")";
            }
            string s;
            switch (v)
            {
                case bool b: return b ? "true" : "false";
                case string str: return StrLit(str);
                case char ch: return CharLit(ch);
                case double d:
                    if (t != null && t.SpecialType == SpecialType.System_Single) return Lit((float)d, t, paren);
                    s = DblLit(d); break;
                case float f:
                    if (t != null && t.SpecialType == SpecialType.System_Double) return Lit((double)f, t, paren);
                    s = FltLit(f); break;
                default:
                    if (t != null && IsPrimitiveNumeric(t) && v is IConvertible ic)
                    {
                        // a constant of one type converted at compile time to another: the value in the target type
                        try { v = ConvertConst(v, t); } catch (Exception) { return Todo("constant " + v + " as " + t.ToDisplayString()); }
                        if (v is double dd) { s = DblLit(dd); break; }
                        if (v is float ff) { s = FltLit(ff); break; }
                        if (v is char cc) return CharLit(cc);
                    }
                    s = IntLit(v);
                    break;
            }
            return paren && s.StartsWith("-") ? "(" + s + ")" : s;
        }

        static object ConvertConst(object v, ITypeSymbol t)
        {
            var ci = CultureInfo.InvariantCulture;
            unchecked
            {
                long l = v switch { ulong u => (long)u, double d => (long)d, float f => (long)f, char c => c, _ => Convert.ToInt64(v, ci) };
                ulong ul = v switch { ulong u => u, long ll => (ulong)ll, int i => (ulong)(long)i, double d => (ulong)d, _ => (ulong)l };
                return t.SpecialType switch
                {
                    SpecialType.System_SByte => (sbyte)l, SpecialType.System_Byte => (byte)l, SpecialType.System_Int16 => (short)l,
                    SpecialType.System_UInt16 => (ushort)l, SpecialType.System_Char => (char)l, SpecialType.System_Int32 => (int)l,
                    SpecialType.System_UInt32 => (uint)l, SpecialType.System_Int64 => v is ulong u2 ? (long)u2 : l, SpecialType.System_UInt64 => ul,
                    SpecialType.System_Single => v is double d2 ? (float)d2 : v is ulong u3 ? (float)u3 : (float)l,
                    SpecialType.System_Double => v is float f2 ? (double)f2 : v is ulong u4 ? (double)u4 : (double)l,
                    _ => v
                };
            }
        }

        static string IntLit(object v)
        {
            var ci = CultureInfo.InvariantCulture;
            switch (v)
            {
                case int i: return i == int.MinValue ? "System.Int32.MinValue" : i.ToString(ci);
                case uint u: return u.ToString(ci) + "u";
                case long l: return l == long.MinValue ? "System.Int64.MinValue" : l.ToString(ci) + "L";
                case ulong ul: return ul.ToString(ci) + "UL";
                case short sh: return sh == short.MinValue ? "System.Int16.MinValue" : sh.ToString(ci) + "s";
                case ushort us: return us.ToString(ci) + "us";
                case sbyte sb: return sb == sbyte.MinValue ? "System.SByte.MinValue" : sb.ToString(ci) + "y";
                case byte b: return b.ToString(ci) + "uy";
                default: return Todo("literal of type " + v.GetType().Name);
            }
        }

        static string DblLit(double d)
        {
            if (double.IsNaN(d)) return BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(double.NaN) ? "System.Double.NaN" : "(System.BitConverter.Int64BitsToDouble " + BitConverter.DoubleToInt64Bits(d) + "L)";
            if (double.IsPositiveInfinity(d)) return "System.Double.PositiveInfinity";
            if (double.IsNegativeInfinity(d)) return "System.Double.NegativeInfinity";
            if (d == 0 && double.IsNegative(d)) return "-0.0";
            var s = d.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e')) s += ".0";
            if (s.Contains('E') && !s.Contains('.')) { var k = s.IndexOf('E'); s = s.Substring(0, k) + ".0" + s.Substring(k); }
            return s;
        }

        static string FltLit(float f)
        {
            if (float.IsNaN(f)) return "System.Single.NaN";
            if (float.IsPositiveInfinity(f)) return "System.Single.PositiveInfinity";
            if (float.IsNegativeInfinity(f)) return "System.Single.NegativeInfinity";
            if (f == 0 && float.IsNegative(f)) return "-0.0f";
            var s = f.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains('.') && !s.Contains('E') && !s.Contains('e')) s += ".0";
            if (s.Contains('E') && !s.Contains('.')) { var k = s.IndexOf('E'); s = s.Substring(0, k) + ".0" + s.Substring(k); }
            return s + "f";
        }

        static string StrLit(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32 || ch > 126) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static string CharLit(char ch)
        {
            if (ch == '\\') return "'\\\\'";
            if (ch == '\'') return "'\\''";
            if (ch >= 32 && ch <= 126) return "'" + ch + "'";
            return "'\\u" + ((int)ch).ToString("x4") + "'";
        }

        // ---------------------------------------------------------------------------------------- references
        string FieldRef(IFieldReferenceOperation fr)
        {
            var f = fr.Field;
            if (f.IsStatic)
            {
                if (IsStaticField(f)) return StaticFieldRef(f.OriginalDefinition);
                if (IsSystem(f)) { Use("System field " + f.ContainingType.ToDisplayString() + "." + f.Name); return SysName(f.ContainingType) + "." + f.Name; }
                Use("static field " + f.ContainingType.OriginalDefinition.ToDisplayString() + "." + f.Name);
                return T(f.ContainingType) + "." + Fs(f.Name);
            }
            if (IsRuntime(f.ContainingType)) Use("field " + f.ContainingType.OriginalDefinition.ToDisplayString() + "." + f.Name);
            return Recv(fr.Instance!) + "." + FieldIdent(f.OriginalDefinition is IFieldSymbol of ? of : f);
        }

        /// <summary>A receiver: an atom, or a parenthesized expression.</summary>
        string Recv(IOperation inst)
        {
            if (inst is IInstanceReferenceOperation ir && ir.ReferenceKind != InstanceReferenceKind.ImplicitReceiver) return "this";
            return E(inst);
        }

        string PropRef(IPropertyReferenceOperation pr)
        {
            var p = pr.Property;
            if (IsRuntime(p.ContainingType)) Use("property " + p.ContainingType.OriginalDefinition.ToDisplayString() + "." + p.Name + (p.IsIndexer ? "[" + string.Join(",", p.Parameters.Select(x => x.Type.ToDisplayString())) + "]" : "") + (p.ReturnsByRef ? " (ref)" : ""));
            else if (IsSystem(p.ContainingType)) Use("System property " + p.ContainingType.ToDisplayString() + "." + p.Name);
            else Todo("engine property " + p.Name);
            string recv = p.IsStatic ? (IsSystem(p.ContainingType) ? SysName(p.ContainingType) : T(p.ContainingType)) : Recv(pr.Instance!);
            if (p.IsIndexer)
            {
                var args = pr.Arguments.OrderBy(a => a.Parameter!.Ordinal).Select(a => E(a.Value)).ToList();
                return recv + ".[" + string.Join(", ", args) + "]";
            }
            return recv + "." + Fs(p.Name);
        }

        string ArrayElem(IArrayElementReferenceOperation ae)
        {
            if (ae.Indices.Length != 1) return Todo("array with several indices");
            return E(ae.ArrayReference) + ".[" + Index(ae.Indices[0]) + "]";
        }

        string Index(IOperation i)
        {
            var t = i.Type!;
            if (t.SpecialType == SpecialType.System_Int32) return E(i);
            if (i.ConstantValue.HasValue && i.ConstantValue.Value is IConvertible) return Lit(Convert.ToInt32(i.ConstantValue.Value, CultureInfo.InvariantCulture), comp.GetSpecialType(SpecialType.System_Int32), false);
            return "int " + P(E(i));
        }

        /// <summary>The address of a location, for a ref argument or a ref local: &amp;x, &amp;o.f, &amp;a.[i], &amp;(v.[i]).
        /// A ref conditional cannot be written as an F# address expression; Args binds it to a byref local first.</summary>
        public string AddrExpr(IOperation op)
        {
            while (op is IConversionOperation cv && cv.Conversion.IsIdentity) op = cv.Operand;
            switch (op)
            {
                case IParenthesizedOperation p: return AddrExpr(p.Operand);
                case ILocalReferenceOperation l:
                    if (refCells.Contains(l.Local)) return "&" + LocalName(l.Local) + ".contents";
                    return "&" + LocalName(l.Local);
                case IParameterReferenceOperation pr:
                    if (refCells.Contains(pr.Parameter)) return "&" + LocalName(pr.Parameter) + ".contents";
                    return "&" + LocalName(pr.Parameter);
                case IFieldReferenceOperation fr: return "&" + FieldRef(fr);
                case IArrayElementReferenceOperation ae: return "&" + ArrayElem(ae);
                case IPropertyReferenceOperation pr2:
                    if (!pr2.Property.ReturnsByRef) return Todo("address of a property without a ref getter");
                    return "&(" + PropRef(pr2) + ")";
                case IInvocationOperation inv:
                    if (!inv.TargetMethod.ReturnsByRef) return Todo("address of a call that does not return a reference");
                    return "&(" + Invoke(inv) + ")";
                case IConditionalOperation c when c.IsRef:
                    return "(if " + E(c.Condition) + " then " + AddrExpr(c.WhenTrue) + " else " + AddrExpr(c.WhenFalse!) + ")";
                default:
                    return Todo("address of " + op.Kind);
            }
        }

        // ---------------------------------------------------------------------------------------- calls
        /// <summary>The arguments of a call in parameter order; a ref argument that is a ref conditional is bound to a
        /// byref local first, which the returned binder wraps around the call.</summary>
        public string Args(IMethodSymbol m, ImmutableArray<IArgumentOperation> args) => ArgsB(m, args, out var b) is var s && b.Count == 0 ? s : Todo("ref conditional argument outside a call");

        string ArgsB(IMethodSymbol m, ImmutableArray<IArgumentOperation> args, out List<string> binders)
        {
            binders = new List<string>();
            var parts = new List<string>();
            foreach (var a in args.OrderBy(a => a.Parameter?.Ordinal ?? 0))
            {
                var p = a.Parameter;
                if (p != null && p.RefKind != RefKind.None)
                {
                    var v = a.Value;
                    while (v is IConversionOperation cv && cv.Conversion.IsIdentity) v = cv.Operand;
                    if (v is IConditionalOperation c && c.IsRef)
                    {
                        var t = Tmp("r");
                        binders.Add("let " + t + " = " + AddrExpr(v) + " in ");
                        parts.Add("&" + t);
                    }
                    else parts.Add(AddrExpr(v));
                    continue;
                }
                if (a.ArgumentKind == ArgumentKind.ParamArray && a.Value is IArrayCreationOperation pac)
                {
                    parts.Add(NewArray(pac));
                    continue;
                }
                parts.Add(E(a.Value));
            }
            return string.Join(", ", parts);
        }

        string Wrap(List<string> binders, string call) => binders.Count == 0 ? call : "(" + string.Concat(binders) + call + ")";

        string TypeArgs(IMethodSymbol m) => m.TypeArguments.Length == 0 ? "" : "<" + string.Join(", ", m.TypeArguments.Select(T)) + ">";

        string Invoke(IInvocationOperation inv)
        {
            var m = inv.TargetMethod;
            var argText = ArgsB(m, inv.Arguments, out var binders);
            string call;
            if (m.MethodKind == MethodKind.DelegateInvoke)
            {
                call = E(inv.Instance!) + ".Invoke(" + argText + ")";
                return Wrap(binders, call);
            }
            if (IsSystem(m))
            {
                call = SystemCall(inv, m, argText);
                return Wrap(binders, call);
            }
            var name = MethodIdent(m);
            if (IsRuntime(m)) Use("method " + m.OriginalDefinition.ContainingType.ToDisplayString() + "." + m.OriginalDefinition.Name + "(" + string.Join(", ", m.OriginalDefinition.Parameters.Select(p => (p.RefKind != RefKind.None ? "ref " : "") + p.Type.ToDisplayString())) + ")");
            if (m.IsStatic) call = T(m.ContainingType) + "." + name + TypeArgs(m) + "(" + argText + ")";
            else
            {
                var inst = inv.Instance!;
                string recv;
                if (inst.Syntax is BaseExpressionSyntax) recv = "base";
                else recv = Recv(inst);
                call = recv + "." + name + TypeArgs(m) + "(" + argText + ")";
            }
            return Wrap(binders, call);
        }

        static readonly Dictionary<string, string> LibmCalls = new()
        {
            // the system's glibc, through the runtime's P/Invoke declarations (fsharp/runtime/LibM.fs)
            ["Exp"] = "Cpp.LibM.exp", ["Log"] = "Cpp.LibM.log", ["Log10"] = "Cpp.LibM.log10", ["Pow"] = "Cpp.LibM.pow",
            ["Sin"] = "Cpp.LibM.sin", ["Cos"] = "Cpp.LibM.cos", ["Tan"] = "Cpp.LibM.tan", ["Asin"] = "Cpp.LibM.asin",
            ["Acos"] = "Cpp.LibM.acos", ["Atan"] = "Cpp.LibM.atan", ["Atan2"] = "Cpp.LibM.atan2", ["Log2"] = "Cpp.LibM.log2",
            ["Sinh"] = "Cpp.LibM.sinh", ["Cosh"] = "Cpp.LibM.cosh", ["Tanh"] = "Cpp.LibM.tanh", ["Cbrt"] = "Cpp.LibM.cbrt",
            // exact operations (IEEE fixes the result): .NET's own
            ["Sqrt"] = "System.Math.Sqrt", ["Floor"] = "System.Math.Floor", ["Ceiling"] = "System.Math.Ceiling",
            ["Truncate"] = "System.Math.Truncate", ["CopySign"] = "System.Math.CopySign",
        };

        string SystemCall(IInvocationOperation inv, IMethodSymbol m, string argText)
        {
            var ct = m.ContainingType.ToDisplayString();
            Use("System call " + ct + "." + m.Name + "(" + string.Join(", ", m.Parameters.Select(p => p.Type.ToDisplayString())) + ")");
            if (ct == "System.Math")
            {
                if (m.Name == "Abs") return "System.Math.Abs(" + argText + ")";
                if (LibmCalls.TryGetValue(m.Name, out var f) && m.Parameters.All(p => p.Type.SpecialType == SpecialType.System_Double)) return f + "(" + argText + ")";
                return Todo("System.Math." + m.Name);
            }
            if (ct == "object" && m.Name == "ReferenceEquals") return "obj.ReferenceEquals(" + argText + ")";
            if (m.IsStatic) return SysName(m.ContainingType) + "." + m.Name + TypeArgs(m) + "(" + argText + ")";
            return Recv(inv.Instance!) + "." + m.Name + TypeArgs(m) + "(" + argText + ")";
        }

        string NewObj(IObjectCreationOperation oc)
        {
            var t = (INamedTypeSymbol)oc.Type!;
            var ctor = oc.Constructor!;
            var argText = ArgsB(ctor, oc.Arguments, out var binders);
            if (IsRuntime(t)) Use("new " + t.OriginalDefinition.ToDisplayString() + "(" + string.Join(", ", ctor.OriginalDefinition.Parameters.Select(p => p.Type.ToDisplayString())) + ")");
            else if (IsSystem(t)) Use("new " + t.ToDisplayString());
            var call = T(t) + "(" + argText + ")";
            if (t.TypeKind == TypeKind.Struct && oc.Arguments.Length == 0) call = "Unchecked.defaultof<" + T(t) + ">";
            call = Wrap(binders, call);
            if (oc.Initializer == null || oc.Initializer.Initializers.Length == 0) return call;
            var o = Tmp("o");
            var sb = new StringBuilder("(let " + o + " = " + call + " in ");
            var saved = implicitReceiver;
            implicitReceiver = o;
            foreach (var init in oc.Initializer.Initializers)
            {
                if (init is ISimpleAssignmentOperation sa) sb.Append(Assign(sa, false)).Append("; ");
                else Todo("object initializer element " + init.Kind);
            }
            implicitReceiver = saved;
            sb.Append(o).Append(')');
            return sb.ToString();
        }

        string NewArray(IArrayCreationOperation ac)
        {
            var at = (IArrayTypeSymbol)ac.Type!;
            if (at.Rank != 1) return Todo("multidimensional array creation");
            var et = T(at.ElementType);
            if (ac.Initializer != null)
            {
                var els = ac.Initializer.ElementValues.Select(E).ToList();
                if (els.Count == 0) return "(Array.empty<" + et + ">)";
                return "([| " + string.Join("; ", els) + " |] : " + et + "[])";
            }
            if (ac.DimensionSizes.Length != 1) return Todo("array creation with several sizes");
            return "(Array.zeroCreate<" + et + "> (" + Index(ac.DimensionSizes[0]) + "))";
        }

        // ---------------------------------------------------------------------------------------- delegates
        string Delegate(IDelegateCreationOperation dc)
        {
            var dt = (INamedTypeSymbol)dc.Type!;
            var dname = T(dt);
            var inv = dt.DelegateInvokeMethod!;
            switch (dc.Target)
            {
                case IAnonymousFunctionOperation af:
                    return Lambda(dname, af);
                case IMethodReferenceOperation mr:
                    {
                        var m = mr.Method;
                        var ps = inv.Parameters.Select((p, k) => "a" + k).ToList();
                        var lam = "fun " + (inv.Parameters.Length == 0 ? "()" : string.Join(" ", inv.Parameters.Select((p, k) => "(" + ps[k] + ": " + ParamType(p) + ")"))) + " -> ";
                        var args = string.Join(", ", inv.Parameters.Select((p, k) => p.RefKind != RefKind.None ? "&" + ps[k] : ps[k]));
                        string target;
                        string bind = "";
                        if (m.IsStatic) target = T(m.ContainingType) + "." + MethodIdent(m) + TypeArgs(m);
                        else
                        {
                            var inst = mr.Instance!;
                            string recv;
                            if (inst is IInstanceReferenceOperation) recv = "this";
                            else if (inst is ILocalReferenceOperation or IParameterReferenceOperation && !refCells.Contains(inst is ILocalReferenceOperation lr ? lr.Local : ((IParameterReferenceOperation)inst).Parameter)) recv = E(inst);
                            else { recv = Tmp("o"); bind = "let " + recv + " = " + E(inst) + " in "; }
                            target = recv + "." + MethodIdent(m) + TypeArgs(m);
                        }
                        var body = target + "(" + args + ")";
                        if (inv.ReturnsByRef) body = "&" + body;
                        Stat("method groups made into delegates");
                        return "(" + bind + dname + "(" + lam + body + "))";
                    }
                default:
                    return Todo("delegate from " + dc.Target.Kind);
            }
        }

        string Lambda(string dname, IAnonymousFunctionOperation af)
        {
            var sym = af.Symbol;
            var ps = sym.Parameters.Length == 0 ? "()" : string.Join(" ", sym.Parameters.Select(p => "(" + LocalName(p) + ": " + ParamType(p) + ")"));
            // an expression lambda whose body needs no statements stays inline
            var ops = af.Body.Operations;
            bool simple = ops.Length == 1 && (ops[0] is IReturnOperation r && r.ReturnedValue != null || ops[0] is IExpressionStatementOperation)
                && !sym.Parameters.Any(p => p.RefKind == RefKind.None && (assignedSyms.Contains(p) || addrSyms.Contains(p)))
                && !af.Body.Descendants().Any(d => d is IAnonymousFunctionOperation a2 && a2.Body.Operations.Length != 1);
            if (simple)
            {
                var inner = new Fn(sym, 0) { };
                inner.condAccessShare(condAccess);
                string body;
                if (ops[0] is IReturnOperation rr) body = sym.ReturnsByRef ? inner.AddrExpr(rr.ReturnedValue!) : inner.E(rr.ReturnedValue!);
                else body = inner.ExprStmt(((IExpressionStatementOperation)ops[0]).Operation);
                if (inner.Pre.Count == 0)
                {
                    Stat("lambdas: inline");
                    return dname + "(fun " + ps + " -> " + body + ")";
                }
            }
            // a statement lambda: bound to a name before the statement that uses it
            var name = Tmp("lam");
            var lines = FunctionBody(sym, af.Body, 1);
            Pre.Add("let " + name + " = " + dname + "(fun " + ps + " ->");
            Pre.AddRange(lines);
            Pre.Add("    )");
            Stat("lambdas: hoisted before their statement");
            return name;
        }

        void condAccessShare(Stack<string> outer) { foreach (var x in outer.Reverse()) condAccess.Push(x); }

        // ---------------------------------------------------------------------------------------- conversions
        string Conv(IConversionOperation cv)
        {
            var from = cv.Operand.Type;
            var to = cv.Type!;
            var c = cv.Conversion;
            if (c.IsIdentity) return E(cv.Operand);
            if (cv.OperatorMethod != null)
            {
                var om = cv.OperatorMethod;
                Use("conversion " + om.ContainingType.OriginalDefinition.ToDisplayString() + "." + om.Name + "(" + om.Parameters[0].Type.ToDisplayString() + ")");
                var arg = E(cv.Operand);
                if (from != null && !SymbolEqualityComparer.Default.Equals(from, om.Parameters[0].Type) && IsPrimitiveNumeric(om.Parameters[0].Type)) arg = ConvFn(om.Parameters[0].Type) + " " + arg;
                var r = "(" + T(om.ContainingType) + "." + om.Name + "(" + arg + ")" + ")";
                if (!SymbolEqualityComparer.Default.Equals(om.ReturnType, to)) return Todo("user conversion followed by another conversion");
                return r;
            }
            if (from == null)
            {
                if (cv.Operand.ConstantValue.HasValue && cv.Operand.ConstantValue.Value == null) return NullOf(to);
                return Todo("conversion from an expression without a type");
            }
            if (c.IsNullable) return Todo("nullable conversion");
            bool fromEnum = from.TypeKind == TypeKind.Enum, toEnum = to.TypeKind == TypeKind.Enum;
            if (fromEnum || toEnum)
            {
                string v = E(cv.Operand);
                if (fromEnum) { v = "(LanguagePrimitives.EnumToValue " + P(v) + ")"; from = ((INamedTypeSymbol)from).EnumUnderlyingType!; }
                if (toEnum)
                {
                    var ut = ((INamedTypeSymbol)to).EnumUnderlyingType!;
                    if (!SymbolEqualityComparer.Default.Equals(from, ut)) v = "(" + ConvFn(ut) + " " + P(v) + ")";
                    return "(LanguagePrimitives.EnumOfValue<" + T(ut) + ", " + T(to) + "> " + P(v) + ")";
                }
                if (SymbolEqualityComparer.Default.Equals(from, to)) return v;
                return "(" + ConvFn(to) + " " + P(v) + ")";
            }
            if (IsPrimitiveNumeric(from) && IsPrimitiveNumeric(to))
            {
                if (SymbolEqualityComparer.Default.Equals(from, to)) return E(cv.Operand);
                return "(" + ConvFn(to) + " " + P(E(cv.Operand)) + ")";
            }
            var cc = cv.GetConversion();
            if (cc.IsBoxing) return "(box " + P(E(cv.Operand)) + ")";
            if (cc.IsUnboxing) return "(unbox<" + T(to) + "> " + P(E(cv.Operand)) + ")";
            if (c.IsReference)
            {
                if (c.IsImplicit)
                {
                    if (to.SpecialType == SpecialType.System_Object) return "(box " + P(E(cv.Operand)) + ")";
                    return "(" + E(cv.Operand) + " :> " + T(to) + ")";
                }
                return "(" + E(cv.Operand) + " :?> " + T(to) + ")";
            }
            if (cc.IsDefaultLiteral) return DefaultOf(to);
            return Todo("conversion " + from.ToDisplayString() + " -> " + to.ToDisplayString() + " (" + c + ")");
        }

        // ---------------------------------------------------------------------------------------- operators
        static bool IsRefType(ITypeSymbol? t) => t != null && (t.IsReferenceType || t.TypeKind == TypeKind.TypeParameter && !t.IsValueType);

        string Binary(IBinaryOperation b)
        {
            var k = b.OperatorKind;
            if (b.OperatorMethod != null)
            {
                var om = b.OperatorMethod;
                Use("operator " + om.ContainingType.OriginalDefinition.ToDisplayString() + "." + om.Name);
                if (IsSystem(om) && om.ContainingType.SpecialType == SpecialType.System_String)
                    return "(" + E(b.LeftOperand) + " + " + E(b.RightOperand) + ")";
                if (IsSystem(om) && om.ContainingType.TypeKind == TypeKind.Delegate || om.ContainingType.ToDisplayString() == "System.Delegate")
                    return Todo("delegate operator");
                return T(om.ContainingType) + "." + om.Name + "(" + E(b.LeftOperand) + ", " + E(b.RightOperand) + ")";
            }
            var lt = b.LeftOperand.Type;
            var rt = b.RightOperand.Type;
            string l = E(b.LeftOperand), r = E(b.RightOperand);
            bool enumOps = lt?.TypeKind == TypeKind.Enum || rt?.TypeKind == TypeKind.Enum;
            switch (k)
            {
                case BinaryOperatorKind.ConditionalAnd: return "(" + l + " && " + r + ")";
                case BinaryOperatorKind.ConditionalOr: return "(" + l + " || " + r + ")";
                case BinaryOperatorKind.Equals:
                case BinaryOperatorKind.NotEquals:
                    {
                        string eq;
                        if (IsRefType(lt) && lt!.SpecialType != SpecialType.System_String || IsRefType(rt) && rt!.SpecialType != SpecialType.System_String || lt == null || rt == null)
                            eq = "obj.ReferenceEquals(" + l + ", " + r + ")";
                        else if (enumOps) eq = "(LanguagePrimitives.EnumToValue " + P(l) + " = LanguagePrimitives.EnumToValue " + P(r) + ")";
                        else eq = "(" + l + " = " + r + ")";
                        return k == BinaryOperatorKind.Equals ? eq : "(not " + P(eq) + ")";
                    }
            }
            string op;
            switch (k)
            {
                case BinaryOperatorKind.Add: op = "+"; break;
                case BinaryOperatorKind.Subtract: op = "-"; break;
                case BinaryOperatorKind.Multiply: op = "*"; break;
                case BinaryOperatorKind.Divide: op = "/"; break;
                case BinaryOperatorKind.Remainder: op = "%"; break;
                case BinaryOperatorKind.LeftShift: op = "<<<"; break;
                case BinaryOperatorKind.RightShift: op = ">>>"; break;
                case BinaryOperatorKind.LessThan: op = "<"; break;
                case BinaryOperatorKind.LessThanOrEqual: op = "<="; break;
                case BinaryOperatorKind.GreaterThan: op = ">"; break;
                case BinaryOperatorKind.GreaterThanOrEqual: op = ">="; break;
                case BinaryOperatorKind.And:
                    if (b.Type?.SpecialType == SpecialType.System_Boolean) return BoolOp(l, r, "&&");
                    op = "&&&"; break;
                case BinaryOperatorKind.Or:
                    if (b.Type?.SpecialType == SpecialType.System_Boolean) return BoolOp(l, r, "||");
                    op = "|||"; break;
                case BinaryOperatorKind.ExclusiveOr:
                    if (b.Type?.SpecialType == SpecialType.System_Boolean) return "(" + l + " <> " + r + ")";
                    op = "^^^"; break;
                default: return Todo("binary operator " + k);
            }
            if (enumOps && (op == "<" || op == "<=" || op == ">" || op == ">="))
                return "(LanguagePrimitives.EnumToValue " + P(l) + " " + op + " LanguagePrimitives.EnumToValue " + P(r) + ")";
            if (enumOps && (op == "+" || op == "-")) return Todo("enum arithmetic");
            if (b.Type?.SpecialType == SpecialType.System_String && op == "+")
            {
                if (lt?.SpecialType == SpecialType.System_String && rt?.SpecialType == SpecialType.System_String) return "(" + l + " + " + r + ")";
                return "System.String.Concat(box " + P(l) + ", box " + P(r) + ")";
            }
            return "(" + l + " " + op + " " + r + ")";
        }

        string BoolOp(string l, string r, string op)
        {
            // C#'s & and | on bools evaluate both sides
            var a = Tmp("a"); var b = Tmp("b");
            return "(let " + a + " = " + l + " in let " + b + " = " + r + " in " + a + " " + op + " " + b + ")";
        }

        string Unary(IUnaryOperation u)
        {
            if (u.OperatorMethod != null)
            {
                Use("operator " + u.OperatorMethod.ContainingType.OriginalDefinition.ToDisplayString() + "." + u.OperatorMethod.Name);
                return T(u.OperatorMethod.ContainingType) + "." + u.OperatorMethod.Name + "(" + E(u.Operand) + ")";
            }
            var x = E(u.Operand);
            return u.OperatorKind switch
            {
                UnaryOperatorKind.Minus => "(-" + P(x) + ")",
                UnaryOperatorKind.Plus => x,
                UnaryOperatorKind.Not => "(not " + P(x) + ")",
                UnaryOperatorKind.BitwiseNegation => "(~~~" + P(x) + ")",
                _ => Todo("unary operator " + u.OperatorKind)
            };
        }

        static string One(ITypeSymbol t) => t.SpecialType switch
        {
            SpecialType.System_SByte => "1y", SpecialType.System_Byte => "1uy", SpecialType.System_Int16 => "1s",
            SpecialType.System_UInt16 => "1us", SpecialType.System_Int32 => "1", SpecialType.System_UInt32 => "1u",
            SpecialType.System_Int64 => "1L", SpecialType.System_UInt64 => "1UL", SpecialType.System_Single => "1.0f",
            SpecialType.System_Double => "1.0", _ => Todo("increment of " + t.ToDisplayString())
        };

        // ---------------------------------------------------------------------------------------- assignments
        /// <summary>True when evaluating the location twice is the same as evaluating it once: locals, parameters,
        /// this, constants, and fields, elements and conversions of those.</summary>
        static bool Pure(IOperation op) => op switch
        {
            ILocalReferenceOperation or IParameterReferenceOperation or IInstanceReferenceOperation or ILiteralOperation => true,
            IFieldReferenceOperation f => f.Instance == null || Pure(f.Instance),
            IConversionOperation c => c.OperatorMethod == null && Pure(c.Operand),
            IParenthesizedOperation p => Pure(p.Operand),
            IBinaryOperation b => b.OperatorMethod == null && Pure(b.LeftOperand) && Pure(b.RightOperand),
            IArrayElementReferenceOperation a => Pure(a.ArrayReference) && a.Indices.All(Pure),
            _ => op.ConstantValue.HasValue
        };

        /// <summary>A location written with &lt;-: its text, and whether it can be read and written through that text
        /// twice. A property with a ref getter, an array element or a field is addressable.</summary>
        (string text, bool pure, bool addressable) LValue(IOperation t)
        {
            while (t is IParenthesizedOperation p) t = p.Operand;
            switch (t)
            {
                case ILocalReferenceOperation l:
                    return (refCells.Contains(l.Local) ? LocalName(l.Local) + ".Value" : LocalName(l.Local), true, true);
                case IParameterReferenceOperation pr:
                    return (refCells.Contains(pr.Parameter) ? LocalName(pr.Parameter) + ".Value" : LocalName(pr.Parameter), true, true);
                case IFieldReferenceOperation fr:
                    return (FieldRef(fr), fr.Instance == null || Pure(fr.Instance), true);
                case IArrayElementReferenceOperation ae:
                    return (ArrayElem(ae), Pure(ae), true);
                case IPropertyReferenceOperation pp:
                    {
                        bool pure = (pp.Instance == null || Pure(pp.Instance)) && pp.Arguments.All(a => Pure(a.Value));
                        if (pp.Property.ReturnsByRef && !pp.Property.IsIndexer) return ("(" + PropRef(pp) + ")", pure, true);
                        // a settable indexer of a struct (CPtr's) is set through a mutable copy of the struct, since F#
                        // calls a setter only on a mutable value. That is right only because CPtr is a view: the copy
                        // shares its array. A struct that held the value itself would lose the write, so any other
                        // struct stops the translation (lessons/42).
                        if (!pp.Property.ReturnsByRef && pp.Instance?.Type?.IsValueType == true)
                        {
                            var st = pp.Instance.Type;
                            if (!(IsRuntime(st) && st.Name == "CPtr")) Todo("setter on a copy of a struct that is not a view: " + st.ToDisplayString());
                            return (PropRef(pp), false, false);
                        }
                        return (PropRef(pp), pure, pp.Property.ReturnsByRef);
                    }
                case IInvocationOperation inv:
                    if (!inv.TargetMethod.ReturnsByRef) Todo("assignment to a call that does not return a reference");
                    return ("(" + Invoke(inv) + ")", false, true);
                default:
                    Todo("assignment to " + t.Kind);
                    return ("", false, false);
            }
        }

        /// <summary>Writes `loc &lt;- f(loc)`: directly when the location is pure, else through a byref local (a
        /// field, an element, a ref property or a ref call) or through temporaries for a settable indexer.</summary>
        string Update(IOperation target, Func<string, string> f, bool value)
        {
            while (target is IParenthesizedOperation p) target = p.Operand;
            var (lv, pure, addressable) = LValue(target);
            bool isRefCall = target is IInvocationOperation || target is IPropertyReferenceOperation pp0 && pp0.Property.ReturnsByRef && !pp0.Property.IsIndexer;
            if (pure && !isRefCall)
            {
                if (!value) return lv + " <- " + f(lv);
                var t = Tmp("t");
                return "(let " + t + " = " + f(lv) + " in " + lv + " <- " + t + "; " + t + ")";
            }
            if (addressable)
            {
                var r = Tmp("r");
                var addr = target switch
                {
                    IPropertyReferenceOperation pp => "&(" + PropRef(pp) + ")",
                    IInvocationOperation inv => "&(" + Invoke(inv) + ")",
                    _ => "&" + lv
                };
                if (!value) return "(let " + r + " = " + addr + " in " + r + " <- " + f(r) + ")";
                return "(let " + r + " = " + addr + " in " + r + " <- " + f(r) + "; " + r + ")";
            }
            if (target is IPropertyReferenceOperation ip && ip.Property.IsIndexer)
            {
                var o = Tmp("o");
                var sb = new StringBuilder("(let mutable " + o + " = " + Recv(ip.Instance!) + " in ");
                var idx = new List<string>();
                foreach (var a in ip.Arguments.OrderBy(a => a.Parameter!.Ordinal)) { var it = Tmp("i"); sb.Append("let " + it + " = " + E(a.Value) + " in "); idx.Add(it); }
                var loc = o + ".[" + string.Join(", ", idx) + "]";
                if (!value) return sb + loc + " <- " + f(loc) + ")";
                var t = Tmp("t");
                return sb + "let " + t + " = " + f(loc) + " in " + loc + " <- " + t + "; " + t + ")";
            }
            return Todo("update of " + target.Kind);
        }

        string Assign(ISimpleAssignmentOperation sa, bool value)
        {
            if (sa.IsRef) return Todo("ref reassignment");
            if (sa.Target is IDiscardOperation) return value ? E(sa.Value) : E(sa.Value) + " |> ignore";
            var v = E(sa.Value);
            var target = sa.Target;
            while (target is IParenthesizedOperation p) target = p.Operand;
            // C# evaluates the location, then the value; a pure location may be written after the value
            var (lv, pure, addressable) = LValue(target);
            bool isRefCall = target is IInvocationOperation || target is IPropertyReferenceOperation pp0 && pp0.Property.ReturnsByRef && !pp0.Property.IsIndexer;
            bool structSet = target is IPropertyReferenceOperation sp && !sp.Property.ReturnsByRef && sp.Instance?.Type?.IsValueType == true;
            if ((pure || !value) && !isRefCall && !structSet)
            {
                if (!value) return lv + " <- " + v;
                var t = Tmp("t");
                return "(let " + t + " = " + v + " in " + lv + " <- " + t + "; " + t + ")";
            }
            return Update(target, _ => v, value);
        }

        string Compound(ICompoundAssignmentOperation ca, bool value)
        {
            var tt = ca.Target.Type!;
            var v = E(ca.Value);
            var k = ca.OperatorKind;
            if (ca.OperatorMethod != null)
            {
                var om = ca.OperatorMethod;
                Use("operator " + om.ContainingType.OriginalDefinition.ToDisplayString() + "." + om.Name);
                if (IsSystem(om) && om.ContainingType.SpecialType == SpecialType.System_String) return Update(ca.Target, x => "(" + x + " + " + v + ")", value);
                return Update(ca.Target, x => T(om.ContainingType) + "." + om.Name + "(" + x + ", " + v + ")", value);
            }
            string op = k switch
            {
                BinaryOperatorKind.Add => "+", BinaryOperatorKind.Subtract => "-", BinaryOperatorKind.Multiply => "*",
                BinaryOperatorKind.Divide => "/", BinaryOperatorKind.Remainder => "%", BinaryOperatorKind.LeftShift => "<<<",
                BinaryOperatorKind.RightShift => ">>>", BinaryOperatorKind.And => "&&&", BinaryOperatorKind.Or => "|||",
                BinaryOperatorKind.ExclusiveOr => "^^^", _ => Todo("compound operator " + k)
            };
            if (tt.SpecialType == SpecialType.System_Boolean)
            {
                if (op == "&&&") return Update(ca.Target, x => BoolOp(x, v, "&&"), value);
                if (op == "|||") return Update(ca.Target, x => BoolOp(x, v, "||"), value);
                if (op == "^^^") return Update(ca.Target, x => "(" + x + " <> " + v + ")", value);
            }
            if (tt.TypeKind == TypeKind.Enum)
            {
                if (op == "&&&" || op == "|||" || op == "^^^") return Update(ca.Target, x => "(" + x + " " + op + " " + v + ")", value);
                return Todo("enum compound arithmetic");
            }
            if (tt.SpecialType == SpecialType.System_String && op == "+") return Update(ca.Target, x => "(" + x + " + " + v + ")", value);
            if (!IsPrimitiveNumeric(tt)) return Todo("compound assignment on " + tt.ToDisplayString());
            // the operation's type: the value's for arithmetic, the promoted target's for shifts
            ITypeSymbol opT;
            bool shift = op == "<<<" || op == ">>>";
            if (shift) opT = Promoted(tt);
            else opT = ca.Value.Type!;
            if (!shift && !IsPrimitiveNumeric(opT)) return Todo("compound value type " + opT.ToDisplayString());
            bool same = SymbolEqualityComparer.Default.Equals(opT, tt);
            return Update(ca.Target, x =>
            {
                var xin = same ? x : "(" + ConvFn(opT) + " " + P(x) + ")";
                var res = "(" + xin + " " + op + " " + v + ")";
                return same ? res : "(" + ConvFn(tt) + " " + P(res) + ")";
            }, value);
        }

        static ITypeSymbol Promoted(ITypeSymbol t) => t.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => comp.GetSpecialType(SpecialType.System_Int32),
            _ => t
        };

        string IncDec(IIncrementOrDecrementOperation inc, bool value)
        {
            var tt = inc.Target.Type!;
            bool up = inc.Kind == OperationKind.Increment;
            Func<string, string> f;
            if (inc.OperatorMethod != null)
            {
                var om = inc.OperatorMethod;
                Use("operator " + om.ContainingType.OriginalDefinition.ToDisplayString() + "." + om.Name);
                f = x => T(om.ContainingType) + "." + om.Name + "(" + x + ")";
            }
            else if (tt.SpecialType == SpecialType.System_Char) f = x => "(char ((int " + P(x) + ") " + (up ? "+" : "-") + " 1))";
            else if (tt.TypeKind == TypeKind.Enum) return Todo("enum increment");
            else { var one = One(tt); f = x => "(" + x + (up ? " + " : " - ") + one + ")"; }
            if (!value || !inc.IsPostfix) return Update(inc.Target, f, value);
            // postfix value: the old value
            var target = inc.Target;
            while (target is IParenthesizedOperation p) target = p.Operand;
            var (lv, pure, addressable) = LValue(target);
            var t = Tmp("t");
            if (pure && !(target is IInvocationOperation))
                return "(let " + t + " = " + lv + " in " + lv + " <- " + f(t) + "; " + t + ")";
            if (addressable)
            {
                var r = Tmp("r");
                var addr = target switch
                {
                    IPropertyReferenceOperation pp => "&(" + PropRef(pp) + ")",
                    IInvocationOperation inv => "&(" + Invoke(inv) + ")",
                    _ => "&" + lv
                };
                return "(let " + r + " = " + addr + " in let " + t + " = " + r + " in " + r + " <- " + f(t) + "; " + t + ")";
            }
            return Todo("postfix increment of " + target.Kind);
        }
    }
}
