#!/usr/bin/env python3
"""Byte gate: run an engine on the corpus models and compare with the SWMM6 reference outputs
(gate/ref_outputs, made by gate/make_ref.sh with the single-threaded swmm6_rel engine).

  gate.py NAME "ENGINE COMMAND" [--ids FILE] [-j N] [--timeout S] [--prune]

PASS for a model = .out byte-identical (when the reference wrote one) and .rpt identical except the three
timestamp lines (Analysis begun / Analysis ended / Total elapsed time), and the same exit code (the SWMM6
command line exits nonzero when a model stops on an input error). Every result records the SHA-256 of
the engine's .out and of its .rpt without those lines, so a run can be checked again after --prune has
deleted the output files of the models that passed (the disk holds about one full set of outputs).
Each result is also appended to results/NAME.jsonl as soon as its model finishes, so a run that is stopped keeps
what it did; --resume skips the models already in that file (a slow engine's gate runs in several sittings).
Adapted from the SWMM5 gate by Claude (Anthropic); see AI-USE.md."""
import argparse, concurrent.futures as cf, json, os, re, subprocess, time, hashlib

HERE = os.path.dirname(os.path.abspath(__file__))
TS = re.compile(rb'Analysis begun|Analysis ended|Total elapsed time')


def rpt_lines(p):
    try:
        return [l for l in open(p, 'rb').read().split(b'\n') if not TS.search(l)]
    except FileNotFoundError:
        return None


def run(args, mid, path):
    out = os.path.join(HERE, 'results', args.name, mid)
    os.makedirs(out, exist_ok=True)
    for f in ('r.rpt', 'r.out'):
        try: os.remove(os.path.join(out, f))
        except FileNotFoundError: pass
    t0 = time.time()
    try:
        p = subprocess.run(args.engine.split() + [os.path.basename(path), out + '/r.rpt', out + '/r.out'],
                           cwd=os.path.dirname(path), capture_output=True, timeout=args.timeout)
        rc = p.returncode; err = p.stderr.decode(errors='replace')[-2000:]
        if rc < 0:
            rc = 128 - rc     # killed by signal N: the reference's exit code was recorded by bash, as 128 + N
    except subprocess.TimeoutExpired:
        rc = 'timeout'; err = ''
    sec = time.time() - t0
    ref = os.path.join(args.ref, mid)
    a, b = rpt_lines(ref + '/r.rpt'), rpt_lines(out + '/r.rpt')
    st = []
    try:
        ref_rc = int(open(ref + '/rc').read().strip())
    except (FileNotFoundError, ValueError):
        ref_rc = 0
    if rc == 'timeout':
        st.append('TIMEOUT')
    elif rc != ref_rc:
        st.append('CRASH' if rc < 0 or rc > 128 or ref_rc == 0 else 'RCDIFF')   # SWMM6 exits nonzero on input errors
    if a != b:
        st.append('RPTDIFF')
    ro, eo = ref + '/r.out', out + '/r.out'
    if os.path.exists(ro) and os.path.getsize(ro) > 0:
        try:
            if open(ro, 'rb').read() != open(eo, 'rb').read():
                st.append('OUTDIFF')
        except FileNotFoundError:
            st.append('OUTDIFF')
    elif os.path.exists(eo) and os.path.getsize(eo) > 0:
        st.append('OUTDIFF')          # EPA wrote no results file; the engine must not write one either
    first = None
    if a is not None and b is not None and a != b:
        for i, (x, y) in enumerate(zip(a, b)):
            if x != y:
                first = (i, x.decode(errors='replace').strip()[:100], y.decode(errors='replace').strip()[:100]); break
        if first is None:
            first = (min(len(a), len(b)), 'len %d' % len(a), 'len %d' % len(b))
    status = '+'.join(st) or 'PASS'
    rh = hashlib.sha256(b'\n'.join(b)).hexdigest() if b is not None else None
    try:
        oh = hashlib.sha256(open(eo, 'rb').read()).hexdigest()
    except FileNotFoundError:
        oh = None
    if args.prune and status == 'PASS':
        for f in ('r.rpt', 'r.out', 'stdout.txt', 'stderr.txt'):
            try: os.remove(os.path.join(out, f))
            except FileNotFoundError: pass
    return {'id': mid, 'path': path, 'status': status, 'sec': round(sec, 3), 'rc': rc,
            'first_rpt_diff': first, 'stderr': err if st and st[0] in ('CRASH', 'RCDIFF') else '', 'ref_rc': ref_rc,
            'rpt_sha256': rh, 'out_sha256': oh}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('name'); ap.add_argument('engine')
    ap.add_argument('--ids'); ap.add_argument('-j', type=int, default=2); ap.add_argument('--timeout', type=float, default=120)
    ap.add_argument('--prune', action='store_true')
    ap.add_argument('--resume', action='store_true')
    ap.add_argument('--models', default=os.path.join(HERE, 'models_all.tsv'), help='id<TAB>path list (default the 480)')
    ap.add_argument('--ref', default=os.path.join(HERE, 'ref_outputs'), help='reference outputs, one folder per id')
    ap.add_argument('--root', help='folder that relative model paths start from (default: the corpus checkout, SWMM_CORPUS); '
                                   'for gate/models_extra.tsv, the folder holding clones of openswmm.engine and swmm6-rust')
    args = ap.parse_args()
    models = [l.rstrip('\n').split('\t', 1) for l in open(args.models) if l.strip()]
    # paths may be relative to the corpus checkout (SWMM_CORPUS, default: next to this repository) or to --root
    corpus = args.root or os.environ.get('SWMM_CORPUS', os.path.join(HERE, '..', '..', '1729-SWMM5-Models-2030'))
    models = [[m, p if os.path.isabs(p) else os.path.join(corpus, p)] for m, p in models]
    if args.ids:
        keep = set(open(args.ids).read().split())
        models = [m for m in models if m[0] in keep]
    os.makedirs(os.path.join(HERE, 'results'), exist_ok=True)
    jl = os.path.join(HERE, 'results', args.name + '.jsonl')
    res = []
    if args.resume and os.path.exists(jl):
        done = {}
        for l in open(jl):
            if l.strip():
                r = json.loads(l); done[r['id']] = r
        res = [done[m[0]] for m in models if m[0] in done]
        models = [m for m in models if m[0] not in done]
        print('%s: resuming, %d models already done, %d to run' % (args.name, len(res), len(models)), flush=True)
    elif os.path.exists(jl):
        os.remove(jl)
    import threading
    lock = threading.Lock()

    def run_log(m):
        r = run(args, *m)
        with lock:
            with open(jl, 'a') as f: f.write(json.dumps(r) + '\n')
        return r
    with cf.ThreadPoolExecutor(args.j) as ex:
        for r in ex.map(run_log, models):
            res.append(r)
    res.sort(key=lambda r: r['path'])
    os.makedirs(os.path.join(HERE, 'results'), exist_ok=True)
    json.dump(res, open(os.path.join(HERE, 'results', args.name + '.json'), 'w'), indent=1)
    from collections import Counter
    c = Counter(r['status'] for r in res)
    print('%s: %d models  %s' % (args.name, len(res), ', '.join('%s %d' % kv for kv in c.most_common())))
    for r in res:
        if r['status'] != 'PASS':
            print('  %-16s %s  %s' % (r['status'], os.path.relpath(r['path'], '/home/claude/swmmbobswmm6/1729-swmm5-models-2030'), r['first_rpt_diff'] or r['stderr'][:200].replace('\n', ' | ')))


if __name__ == '__main__':
    main()
