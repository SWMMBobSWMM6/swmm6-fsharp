#!/usr/bin/env python3
"""What the SWMM6 1D engine's reachable code uses: the call graph from the C API the command line calls, then
counts of the C++ features and standard-library calls in the reachable functions only.

  census.py DECLS.jsonl.gz OUT.json [--roots NAME,NAME,...]

Reachability: a function is reached when a reached function names it (a call, or taking its address for a
function pointer or std::function), when a reached function constructs or destroys an object of its class
(constructors and the destructor), or when it overrides a reached virtual method. Lambdas are part of the
function that contains them. Global variables with initializers are roots.
Written by Claude (Anthropic); see AI-USE.md."""
import argparse, collections, gzip, json, re, sys

FUNC_KINDS = {'FunctionDecl', 'CXXMethodDecl', 'CXXConstructorDecl', 'CXXDestructorDecl', 'CXXConversionDecl'}
RECORD_KINDS = {'CXXRecordDecl', 'ClassTemplateSpecializationDecl'}


def tstr(t):
    return (t or {}).get('desugaredQualType') or (t or {}).get('qualType') or ''


RECORD_RE = re.compile(r'\b(openswmm::[A-Za-z0-9_:<>, ]+?)(?=[\s&*\[\],>)]|$)')


def params_sig(t):
    """A method's function type without its return type, '(params) quals', the part an override must repeat: the
    return type may be spelled another way (an out-of-line definition of RunStats run_stats() const writes
    INetworkSolver::RunStats)."""
    depth = 0
    for i, ch in enumerate(t):
        if ch == '<':
            depth += 1
        elif ch == '>':
            depth -= 1
        elif ch == '(' and depth == 0 and t[i + 1:i + 2] != '*' and t[i + 1:i + 2] != '&':
            return t[i:]
    return t


def walk(n, f):
    f(n)
    for c in n.get('inner', ()):
        walk(c, f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('decls'); ap.add_argument('out')
    ap.add_argument('--roots', default='swmm_engine_create,swmm_engine_destroy,swmm_engine_open,swmm_engine_initialize,'
                    'swmm_engine_start,swmm_engine_step,swmm_engine_end,swmm_engine_report,swmm_engine_close,'
                    'swmm_get_last_error_msg')
    a = ap.parse_args()

    # ---- pass 1: the graph ----
    meta = {}                          # key -> (kind, name, parent, file, virtual, signature)
    edges = collections.defaultdict(set)
    types_used = collections.defaultdict(set)   # function key -> record names in its types
    ctors = collections.defaultdict(list)       # record key -> constructor keys
    dtors = {}
    bases = {}
    records = {}
    globals_init = []
    for line in gzip.open(a.decls, 'rt'):
        d = json.loads(line)
        k, key = d['kind'], d['key']
        meta[key] = (k, d.get('name'), d.get('parent'), d.get('file'), d.get('virtual'), tstr(d.get('type')))
        if k in RECORD_KINDS:
            records[key] = d.get('name')
            bases[key] = [tstr(b.get('type')) for b in d.get('bases', [])]
            for fld in d.get('fields', []):
                for m in RECORD_RE.findall(tstr(fld.get('type'))):
                    types_used[key].add(m.strip())
            continue
        if k == 'CXXConstructorDecl' and d.get('parent'):
            ctors[d['parent']].append(key)
        if k == 'CXXDestructorDecl' and d.get('parent'):
            dtors[d['parent']] = key
        if k == 'VarDecl' and d.get('inner') and not d.get('parent'):
            globals_init.append(key)
        out = edges[key]
        tu = types_used[key]

        def visit(n):
            rd = n.get('referencedDecl')
            if isinstance(rd, dict) and rd.get('key'):
                out.add(rd['key'])
            rm = n.get('referencedMemberDecl')
            if isinstance(rm, str):
                out.add(rm)
            t = tstr(n.get('type'))
            if 'openswmm::' in t:
                for m in RECORD_RE.findall(t):
                    tu.add(m.strip())
        walk(d, visit)
        if d.get('parent'):
            out.add(d['parent'])
        for lr in d.get('local_records', ()):
            out.add(lr)             # a class defined in the body is used by it
    # a function object's operator() is called by the standard library (a hasher, key-equal or comparator given to
    # a container), where the census cannot see the call: it is reachable with its class
    calls_op = collections.defaultdict(list)
    for key, (k, nm, par, fl, virt, sig) in meta.items():
        if k == 'CXXMethodDecl' and par and nm == 'operator()':
            calls_op[par].append(key)
    # class -> its constructors and destructor become reachable when the class is used
    for rec in list(records):
        for c in calls_op.get(rec, []):
            edges[rec].add(c)
        for c in ctors.get(rec, []):
            edges[rec].add(c)
        if rec in dtors:
            edges[rec].add(dtors[rec])
        for fu in types_used.get(rec, ()):
            edges[rec].add(fu)
    for fk, recs in types_used.items():
        for r in recs:
            edges[fk].add(r)
    # virtual overrides: same name and parameter list in a derived record
    by_rec_name = collections.defaultdict(list)
    for key, (k, nm, par, fl, virt, sig) in meta.items():
        if k == 'CXXMethodDecl' and par:
            by_rec_name[(par, nm, params_sig(sig))].append(key)
    derived = collections.defaultdict(set)
    for rec, bs in bases.items():
        for b in bs:
            derived[b].add(rec)

    def all_derived(r, acc):
        for d2 in derived.get(r, ()):
            if d2 not in acc:
                acc.add(d2)
                all_derived(d2, acc)
        return acc
    for key, (k, nm, par, fl, virt, sig) in meta.items():
        if k == 'CXXMethodDecl' and virt and par:
            for d2 in all_derived(par, set()):
                for o in by_rec_name.get((d2, nm, params_sig(sig)), []):
                    edges[key].add(o)

    names = collections.defaultdict(list)
    for key, m in meta.items():
        names[m[1]].append(key)
    roots = [k for r in a.roots.split(',') for k in names.get(r, [])] + globals_init
    seen, stack = set(), list(roots)
    while stack:
        x = stack.pop()
        if x in seen:
            continue
        seen.add(x)
        stack.extend(edges.get(x, ()))
    reach_funcs = [k for k in seen if meta.get(k, ('',))[0] in FUNC_KINDS]
    all_funcs = [k for k, m in meta.items() if m[0] in FUNC_KINDS]
    print('roots %d; reachable: %d of %d functions, %d of %d records' % (
        len(roots), len(reach_funcs), len(all_funcs),
        sum(1 for k in seen if k in records), len(records)))

    # ---- pass 2: what the reachable code uses ----
    kinds = collections.Counter()
    std_calls = collections.Counter()
    std_types = collections.Counter()
    casts = collections.Counter()
    lines_by_file = collections.Counter()
    feats = collections.Counter()
    reach = set(reach_funcs)
    STD_T = re.compile(r'\bstd::[a-z_0-9]+(?:<|\b)')
    for line in gzip.open(a.decls, 'rt'):
        d = json.loads(line)
        if d['key'] not in reach:
            continue
        lines_by_file[d.get('file')] += 1

        def visit(n):
            k = n.get('kind')
            kinds[k] += 1
            t = tstr(n.get('type'))
            for m in STD_T.findall(t):
                std_types[m.rstrip('<')] += 1
            if k in ('ImplicitCastExpr', 'CStyleCastExpr', 'CXXStaticCastExpr', 'CXXReinterpretCastExpr',
                     'CXXConstCastExpr', 'CXXFunctionalCastExpr', 'CXXDynamicCastExpr'):
                casts[(k, n.get('castKind'))] += 1
            rd = n.get('referencedDecl')
            if isinstance(rd, dict) and not rd.get('key') and rd.get('kind') in FUNC_KINDS:
                std_calls[rd.get('name')] += 1
            if k == 'MemberExpr' and not n.get('referencedMemberDecl'):
                inner = n.get('inner') or [{}]
                std_calls['%s.%s' % (re.sub(r'<.*', '<>', tstr(inner[0].get('type')).replace('const ', '')), n.get('name'))] += 1
            if k == 'CXXConstructExpr':
                std_types['construct ' + re.sub(r'<.*', '<>', t)] += 1
            if k == 'VarDecl' and n.get('storageClass') == 'static':
                feats['static local'] += 1
            if k == 'CXXThrowExpr':
                feats['throw'] += 1
            if k == 'CXXTryStmt':
                feats['try'] += 1
            if k == 'LambdaExpr':
                feats['lambda'] += 1
            if k in ('CXXDynamicCastExpr',):
                feats['dynamic_cast'] += 1
            if k == 'GotoStmt':
                feats['goto'] += 1
        walk(d, visit)
        if d['kind'] == 'CXXMethodDecl' and d.get('virtual'):
            feats['virtual method'] += 1
        if d['kind'] == 'CXXDestructorDecl' and not d.get('isImplicit') and any(c.get('kind') == 'CompoundStmt' and c.get('inner') for c in d.get('inner', [])):
            feats['user destructor with a body'] += 1
    res = {'reachable_functions': len(reach_funcs), 'all_functions': len(all_funcs),
           'kinds': kinds.most_common(), 'std_calls': std_calls.most_common(), 'std_types': std_types.most_common(),
           'casts': [[list(k), v] for k, v in casts.most_common()], 'features': feats.most_common(),
           'functions_by_file': lines_by_file.most_common(),
           'reachable': sorted(reach | {k for k in seen if k in records or meta.get(k, ('',))[0] == 'VarDecl'})}
    json.dump(res, open(a.out, 'w'), indent=1)
    print('features', feats.most_common())
    print('std calls (top 60):', std_calls.most_common(60))
    print('std types (top 40):', std_types.most_common(40))


if __name__ == '__main__':
    main()
