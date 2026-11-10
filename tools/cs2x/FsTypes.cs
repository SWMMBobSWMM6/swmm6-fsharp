// FsTypes.cs - the declarations of the F# engine: enums, delegates, classes (fields, constructors, methods, interface
// implementations), the holders of the static fields and the start-up code that sets them, and the analysis of which
// locals F# must keep in reference cells.
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

public static partial class Cs2Fs
{
    // ------------------------------------------------------------------------------------------------ analysis
    // F# closures cannot capture a mutable local. A local or parameter that a lambda captures and that is assigned
    // anywhere after its declaration (or whose address is taken) lives in a reference cell (ref v; read x.Value,
    // address &x.contents). A by-value parameter the body assigns, or whose address it takes, is copied into a mutable
    // local of the same name, since F#'s parameters are immutable.
    static readonly HashSet<ISymbol> refCells = new(SymbolEqualityComparer.Default);
    static readonly HashSet<ISymbol> assignedSyms = new(SymbolEqualityComparer.Default);
    static readonly HashSet<ISymbol> addrSyms = new(SymbolEqualityComparer.Default);
    static readonly HashSet<ISymbol> capturedSyms = new(SymbolEqualityComparer.Default);

    static void Analyze(List<SyntaxTree> gen)
    {
        foreach (var t in gen)
        {
            var m = models[t];
            foreach (var node in t.GetRoot().DescendantNodes().Where(n => n is BaseMethodDeclarationSyntax || n is VariableDeclaratorSyntax && n.Parent?.Parent is FieldDeclarationSyntax))
            {
                var root = m.GetOperation(node is VariableDeclaratorSyntax vd ? (SyntaxNode?)vd.Initializer ?? vd : node);
                if (root == null) continue;
                Scan(root);
            }
        }
        foreach (var s in capturedSyms) if ((assignedSyms.Contains(s) || addrSyms.Contains(s)) && !IsRefSym(s)) refCells.Add(s);
        Stat("analysis: locals and parameters in reference cells", refCells.Count);
    }

    static void Scan(IOperation root)
    {
        foreach (var op in root.DescendantsAndSelf())
        {
            switch (op)
            {
                case IArgumentOperation a when a.Parameter != null && (a.Parameter.RefKind == RefKind.Ref || a.Parameter.RefKind == RefKind.Out || a.Parameter.RefKind == RefKind.In):
                    Mark(a.Value, addrSyms); break;
                case IVariableDeclaratorOperation vd when vd.Symbol.RefKind != RefKind.None && vd.Initializer != null:
                    Mark(vd.Initializer.Value, addrSyms); break;
                case IReturnOperation r when r.ReturnedValue != null && IsRefReturn(r):
                    Mark(r.ReturnedValue, addrSyms); break;
                case IConditionalOperation c when c.IsRef:
                    Mark(c.WhenTrue, addrSyms); if (c.WhenFalse != null) Mark(c.WhenFalse, addrSyms); break;
                case IAssignmentOperation asg:
                    Mark(asg.Target, assignedSyms);
                    if (asg is ISimpleAssignmentOperation sa && sa.IsRef) Mark(sa.Value, addrSyms);
                    break;
                case IIncrementOrDecrementOperation inc:
                    Mark(inc.Target, assignedSyms); break;
                case IAnonymousFunctionOperation af:
                    foreach (var r in af.Body.Descendants())
                    {
                        ISymbol? s = r is ILocalReferenceOperation lr ? lr.Local : r is IParameterReferenceOperation pr ? pr.Parameter : null;
                        if (s == null) continue;
                        if (!IsInside(s.ContainingSymbol, af.Symbol)) capturedSyms.Add(s);
                    }
                    break;
                case ILocalFunctionOperation:
                    Note("local function"); break;
            }
        }
    }

    static bool IsRefSym(ISymbol s) => s is ILocalSymbol l && l.RefKind != RefKind.None || s is IParameterSymbol p && p.RefKind != RefKind.None;

    static bool IsInside(ISymbol? s, ISymbol lambda)
    {
        for (var x = s; x != null; x = x.ContainingSymbol) if (SymbolEqualityComparer.Default.Equals(x, lambda)) return true;
        return false;
    }

    static bool IsRefReturn(IReturnOperation r)
    {
        for (var p = r.Parent; p != null; p = p.Parent)
        {
            if (p is IAnonymousFunctionOperation af) return af.Symbol.RefKind != RefKind.None;
            if (p is IMethodBodyOperation || p is IBlockOperation && p.Parent == null) break;
        }
        var sm = r.SemanticModel;
        var md = r.Syntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return md != null && sm != null && sm.GetDeclaredSymbol(md) is IMethodSymbol ms && ms.RefKind != RefKind.None;
    }

    static void Mark(IOperation op, HashSet<ISymbol> set)
    {
        while (op is IConversionOperation cv && cv.Conversion.IsIdentity) op = cv.Operand;
        while (op is IParenthesizedOperation po) op = po.Operand;
        if (op is ILocalReferenceOperation l) set.Add(l.Local);
        else if (op is IParameterReferenceOperation p) set.Add(p.Parameter);
        else if (op is IConditionalOperation c && c.IsRef) { Mark(c.WhenTrue, set); if (c.WhenFalse != null) Mark(c.WhenFalse, set); }
    }

    // ------------------------------------------------------------------------------------------------ declarations
    static string Ind(int n) => new string(' ', 4 * n);

    static void TypeDecl(INamedTypeSymbol t, StringBuilder sb)
    {
        switch (t.TypeKind)
        {
            case TypeKind.Enum: EnumDecl(t, sb); return;
            case TypeKind.Delegate: DelegateDecl(t, sb); return;
            case TypeKind.Class: ClassDecl(t, sb); return;
            default: Todo("type kind " + t.TypeKind); return;
        }
    }

    static string EnumLit(object v, SpecialType ut)
    {
        var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;
        return ut switch
        {
            SpecialType.System_SByte => s + "y", SpecialType.System_Byte => s + "uy", SpecialType.System_Int16 => s + "s",
            SpecialType.System_UInt16 => s + "us", SpecialType.System_UInt32 => s + "u", SpecialType.System_Int64 => s + "L",
            SpecialType.System_UInt64 => s + "UL", _ => s
        };
    }

    static void EnumDecl(INamedTypeSymbol t, StringBuilder sb)
    {
        var ut = t.EnumUnderlyingType!.SpecialType;
        sb.Append("type ").Append(TypeIdent(t)).Append(" =\n");
        var fields = t.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();
        // F# has no empty enum: one that C# declares with no members (plugins' PluginType) gets a placeholder
        if (fields.Count == 0) { sb.Append("    | ``(no members in C#)`` = ").Append(EnumLit(0, ut)).Append("\n\n"); Note("empty enum given a placeholder member: " + TypeIdent(t)); return; }
        foreach (var f in fields) sb.Append("    | ").Append(Fs(f.Name)).Append(" = ").Append(EnumLit(f.ConstantValue!, ut)).Append('\n');
        sb.Append('\n');
    }

    static string ParamType(IParameterSymbol p) => p.RefKind != RefKind.None ? "byref<" + T(p.Type) + ">" : T(p.Type);
    static string RetType(IMethodSymbol m) => m.ReturnsByRef || m.ReturnsByRefReadonly ? "byref<" + T(m.ReturnType) + ">" : T(m.ReturnType);

    static void DelegateDecl(INamedTypeSymbol t, StringBuilder sb)
    {
        var inv = t.DelegateInvokeMethod!;
        sb.Append("type ").Append(TypeIdent(t)).Append(" = delegate of ");
        sb.Append(inv.Parameters.Length == 0 ? "unit" : string.Join(" * ", inv.Parameters.Select(ParamType)));
        sb.Append(" -> ").Append(RetType(inv)).Append("\n\n");
    }

    static IEnumerable<ISymbol> MembersInOrder(INamedTypeSymbol t) =>
        t.GetMembers().Where(m => !m.IsImplicitlyDeclared || m is IMethodSymbol { MethodKind: MethodKind.Constructor })
            .OrderBy(m => m.Locations.Length > 0 && m.Locations[0].IsInSource ? m.Locations[0].SourceTree!.FilePath : "~", StringComparer.Ordinal)
            .ThenBy(m => m.Locations.Length > 0 && m.Locations[0].IsInSource ? m.Locations[0].SourceSpan.Start : int.MaxValue);

    static void ClassDecl(INamedTypeSymbol t, StringBuilder sb)
    {
        var name = TypeIdent(t);
        if (t.IsStatic) sb.Append("[<AbstractClass; Sealed>]\n");
        else
        {
            sb.Append("[<AllowNullLiteral>]\n");
            if (t.IsAbstract) sb.Append("[<AbstractClass>]\n");
        }
        sb.Append("type ").Append(name).Append(" =\n");
        int lines0 = sb.Length;
        var bt = t.BaseType;
        bool hasBase = bt != null && bt.SpecialType != SpecialType.System_Object;
        if (hasBase) sb.Append("    inherit ").Append(T(bt!)).Append('\n');
        // instance fields (static fields live in the holder, see StaticsAndInit)
        foreach (var f in MembersInOrder(t).OfType<IFieldSymbol>())
        {
            if (f.IsStatic || f.IsConst) continue;
            sb.Append("    [<DefaultValue(false)>] val mutable ").Append(FieldIdent(f)).Append(" : ").Append(T(f.Type)).Append('\n');
        }
        foreach (var m in MembersInOrder(t).OfType<IMethodSymbol>())
        {
            var mb = new StringBuilder();
            try
            {
                switch (m.MethodKind)
                {
                    case MethodKind.Constructor: CtorDecl(t, m, mb); break;
                    case MethodKind.Ordinary: MethodDecl(t, m, mb); break;
                    case MethodKind.StaticConstructor: Todo("static constructor"); break;
                    default: Todo("method kind " + m.MethodKind); break;
                }
            }
            catch (Todo e)
            {
                mb.Clear();
                mb.Append("    // TODO ").Append(m.Name).Append(": ").Append(e.Message.Replace('\n', ' ')).Append('\n');
            }
            sb.Append(mb);
        }
        InterfaceImpls(t, sb);
        if (sb.Length == lines0) sb.Append("    class end\n");
        sb.Append('\n');
    }

    static void InterfaceImpls(INamedTypeSymbol t, StringBuilder sb)
    {
        var inherited = t.BaseType?.AllInterfaces.ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) ?? new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var i in t.AllInterfaces)
        {
            if (inherited.Contains(i)) continue;
            sb.Append("    interface ").Append(T(i)).Append(" with\n");
            foreach (var im in i.GetMembers().OfType<IMethodSymbol>())
            {
                var impl = t.FindImplementationForInterfaceMember(im) as IMethodSymbol;
                if (impl == null) { Todo("no implementation of " + im.ToDisplayString() + " in " + t.ToDisplayString()); continue; }
                var ps = im.Parameters.Select((p, k) => "a" + k).ToList();
                sb.Append("        member this.").Append(Fs(im.Name)).Append(" (")
                  .Append(string.Join(", ", im.Parameters.Select((p, k) => ps[k] + ": " + ParamType(p)))).Append(") : ").Append(RetType(im)).Append(" = ");
                sb.Append("this.").Append(MethodIdent(impl)).Append("(").Append(string.Join(", ", im.Parameters.Select((p, k) => p.RefKind != RefKind.None ? "&" + ps[k] : ps[k]))).Append(")\n");
                Use("interface " + im.ContainingType.OriginalDefinition.ToDisplayString() + "." + im.Name);
            }
        }
    }

    static string ParamList(IMethodSymbol m) =>
        "(" + string.Join(", ", m.Parameters.Select(p => LocalName(p) + ": " + ParamType(p))) + ")";

    static string AbstractSig(IMethodSymbol m) =>
        (m.Parameters.Length == 0 ? "unit" : string.Join(" * ", m.Parameters.Select(p => LocalName(p) + ": " + ParamType(p)))) + " -> " + RetType(m);

    static void MethodDecl(INamedTypeSymbol t, IMethodSymbol m, StringBuilder sb)
    {
        if (m.IsGenericMethod) Todo("generic engine method");
        var name = MethodIdent(m);
        if (m.IsAbstract)
        {
            sb.Append("    abstract ").Append(name).Append(" : ").Append(AbstractSig(m)).Append('\n');
            return;
        }
        var syn = m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
        if (syn == null) { Todo("method without syntax " + m.ToDisplayString()); return; }
        var body = M(syn).GetOperation(syn) as IMethodBodyOperation;
        if (body == null) { Todo("method without body " + m.ToDisplayString()); return; }
        string head;
        if (m.IsStatic) head = "    static member " + name + " " + ParamList(m) + " : " + RetType(m) + " =";
        else if (m.IsOverride) head = "    override this." + name + " " + ParamList(m) + " : " + RetType(m) + " =";
        else if (m.IsVirtual)
        {
            sb.Append("    abstract ").Append(name).Append(" : ").Append(AbstractSig(m)).Append('\n');
            head = "    default this." + name + " " + ParamList(m) + " : " + RetType(m) + " =";
        }
        else head = "    member this." + name + " " + ParamList(m) + " : " + RetType(m) + " =";
        var lines = FunctionBody(m, (IOperation?)body.BlockBody ?? body.ExpressionBody!, 2);
        sb.Append(head).Append('\n');
        foreach (var l in lines) sb.Append(l).Append('\n');
    }

    static void CtorDecl(INamedTypeSymbol t, IMethodSymbol m, StringBuilder sb)
    {
        var bt = t.BaseType;
        bool hasBase = bt != null && bt.SpecialType != SpecialType.System_Object;
        if (m.IsImplicitlyDeclared)
        {
            if (t.IsStatic) return;
            sb.Append("    new () = { ").Append(hasBase ? "inherit " + T(bt!) + "() " : "").Append("}\n");
            return;
        }
        var syn = m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ConstructorDeclarationSyntax;
        if (syn == null) { Todo("constructor without syntax"); return; }
        var cb = M(syn).GetOperation(syn) as IConstructorBodyOperation;
        if (cb == null) { Todo("constructor without body"); return; }
        var fn = new Fn(m, 2);
        string init;
        var ci = cb.Initializer;
        IInvocationOperation? call = null;
        if (ci is IExpressionStatementOperation es) call = es.Operation as IInvocationOperation;
        else call = ci as IInvocationOperation;
        if (call != null && call.TargetMethod.MethodKind == MethodKind.Constructor)
        {
            var args = fn.Args(call.TargetMethod, call.Arguments);
            if (SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType.OriginalDefinition, t.OriginalDefinition))
                init = TypeIdent(t) + "(" + args + ")";
            else if (call.TargetMethod.ContainingType.SpecialType == SpecialType.System_Object) init = "{ }";
            else init = "{ inherit " + T(call.TargetMethod.ContainingType) + "(" + args + ") }";
        }
        else if (ci != null) { Todo("constructor initializer " + ci.Kind); return; }
        else init = hasBase ? "{ inherit " + T(bt!) + "() }" : "{ }";
        if (fn.Pre.Count > 0) Todo("constructor initializer with a statement lambda");
        var bodyOp = (IOperation?)cb.BlockBody ?? cb.ExpressionBody;
        var lines = bodyOp == null ? new List<string>() : FunctionBody(m, bodyOp, 3);
        bool empty = lines.All(l => l.Trim() == "()" || l.Trim().Length == 0);
        sb.Append("    new ").Append(ParamList(m)).Append(empty ? "" : " as this").Append(" =\n");
        sb.Append("        ").Append(init).Append('\n');
        if (!empty)
        {
            sb.Append("        then\n");
            foreach (var l in lines) sb.Append(l).Append('\n');
        }
    }

    // ------------------------------------------------------------------------------------------------ statics
    // C# initializes a class's static fields when the class is first used; the engine's initializers are values with
    // no side effects (literals, tables, new objects), so the F# engine gives each class with static fields a holder
    // object (TypeIdent__S, one instance, X__S.I) whose fields start at zero or null, and Init__.run sets every
    // initialized field before main does anything else, in an order where each initializer runs after the
    // initializers of the static fields it reads, directly or through the engine methods it calls.
    static readonly List<IFieldSymbol> staticFields = new();

    static bool IsStaticField(IFieldSymbol f) => f.IsStatic && !f.IsConst && !IsRuntime(f) && !IsSystem(f) && f.ContainingType.TypeKind != TypeKind.Enum;

    static string HolderName(INamedTypeSymbol t) => TypeIdent(t) + "__S";

    static string StaticFieldRef(IFieldSymbol f) => HolderName(f.ContainingType) + ".I." + FieldIdent(f);

    static void StaticsAndInit(StringBuilder sb)
    {
        var inits = new List<(IFieldSymbol f, IOperation v)>();
        foreach (var t in engineTypes)
        {
            if (t.TypeKind != TypeKind.Class) continue;
            var fs = MembersInOrder(t).OfType<IFieldSymbol>().Where(IsStaticField).ToList();
            if (fs.Count == 0) continue;
            var h = HolderName(t);
            sb.Append("[<AllowNullLiteral>]\ntype ").Append(h).Append("() =\n");
            foreach (var f in fs)
            {
                staticFields.Add(f);
                sb.Append("    [<DefaultValue(false)>] val mutable ").Append(FieldIdent(f)).Append(" : ").Append(T(f.Type)).Append('\n');
                foreach (var r in f.DeclaringSyntaxReferences)
                    if (r.GetSyntax() is VariableDeclaratorSyntax vd && vd.Initializer != null && M(vd).GetOperation(vd.Initializer) is IFieldInitializerOperation fi)
                        inits.Add((f, fi.Value));
            }
            sb.Append("    [<DefaultValue(false)>] static val mutable private i : ").Append(h).Append('\n');
            sb.Append("    static member I with get () : ").Append(h).Append(" = ").Append(h).Append(".i and set (v: ").Append(h).Append(") = ").Append(h).Append(".i <- v\n\n");
        }
        // the order: a depth-first walk over the static fields each initializer reads
        var byField = inits.ToDictionary(x => (ISymbol)x.f, x => x.v, SymbolEqualityComparer.Default);
        var order = new List<(IFieldSymbol f, IOperation v)>();
        var state = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
        var methodDeps = new Dictionary<IMethodSymbol, HashSet<ISymbol>>(SymbolEqualityComparer.Default);
        void Visit(IFieldSymbol f)
        {
            if (state.TryGetValue(f, out var st)) { if (st == 1) Note("cycle among static initializers at " + f.ToDisplayString()); return; }
            state[f] = 1;
            if (byField.TryGetValue(f, out var v))
            {
                foreach (var d in StaticDeps(v, methodDeps)) if (d is IFieldSymbol df && !SymbolEqualityComparer.Default.Equals(df, f)) Visit(df);
                order.Add((f, v));
            }
            state[f] = 2;
        }
        foreach (var (f, _) in inits) Visit(f);
        sb.Append("[<AbstractClass; Sealed>]\ntype Init__ =\n");
        int k = 0;
        var calls = new List<string>();
        foreach (var (f, v) in order)
        {
            k++;
            var fn = new Fn(null, 2);
            string val;
            try { val = fn.E(v); }
            catch (Todo e) { sb.Append("    // TODO initializer of ").Append(f.Name).Append(": ").Append(e.Message).Append('\n'); continue; }
            sb.Append("    static member private i").Append(k).Append(" () =\n");
            foreach (var p in fn.Pre) sb.Append(p).Append('\n');
            sb.Append("        ").Append(StaticFieldRef(f)).Append(" <- ").Append(val).Append('\n');
            calls.Add("Init__.i" + k + " ()");
        }
        Stat("static fields", staticFields.Count);
        Stat("static field initializers", order.Count);
        sb.Append("    /// Creates the holders of the static fields and runs the initializers; main calls it first.\n");
        sb.Append("    static member run () =\n");
        foreach (var t in engineTypes.Where(t => t.TypeKind == TypeKind.Class && t.GetMembers().OfType<IFieldSymbol>().Any(IsStaticField)))
            sb.Append("        ").Append(HolderName(t)).Append(".I <- ").Append(HolderName(t)).Append("()\n");
        foreach (var c in calls) sb.Append("        ").Append(c).Append('\n');
        sb.Append("        ()\n");
    }

    /// <summary>The engine static fields an initializer reads, directly or through the engine methods and
    /// constructors it calls (transitively).</summary>
    static HashSet<ISymbol> StaticDeps(IOperation v, Dictionary<IMethodSymbol, HashSet<ISymbol>> memo)
    {
        var res = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var seenM = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var work = new Stack<IOperation>();
        work.Push(v);
        while (work.Count > 0)
        {
            var op = work.Pop();
            foreach (var o in op.DescendantsAndSelf())
            {
                IMethodSymbol? callee = o switch
                {
                    IInvocationOperation inv => inv.TargetMethod,
                    IObjectCreationOperation oc => oc.Constructor,
                    IMethodReferenceOperation mr => mr.Method,
                    _ => null
                };
                if (o is IFieldReferenceOperation fr && IsStaticField(fr.Field)) res.Add(fr.Field.OriginalDefinition);
                if (callee == null || IsRuntime(callee) || IsSystem(callee) || !seenM.Add(callee.OriginalDefinition)) continue;
                foreach (var r in callee.OriginalDefinition.DeclaringSyntaxReferences)
                {
                    var syn = r.GetSyntax();
                    var body = M(syn).GetOperation(syn);
                    if (body != null) work.Push(body);
                }
            }
        }
        return res;
    }
}
