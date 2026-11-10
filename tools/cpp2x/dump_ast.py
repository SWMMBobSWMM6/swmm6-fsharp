#!/usr/bin/env python3
"""Dump clang's JSON AST for every translation unit of the SWMM6 1D engine, keeping only the engine's own
declarations (tools/cpp2x/astfilter drops the C++ standard library and system headers).

  dump_ast.py BUILD_DIR SRC_DIR OUT_DIR [-j N]

BUILD_DIR is the reference CMake build (its compile database comes from `ninja -t compdb`), SRC_DIR the
openswmm.engine checkout. Writes OUT_DIR/<n>.jsonl.gz per translation unit and OUT_DIR/tus.json.
Written by Claude (Anthropic); see AI-USE.md."""
import argparse, concurrent.futures as cf, gzip, json, os, shlex, subprocess, time

HERE = os.path.dirname(os.path.abspath(__file__))
FILTER = os.path.join(HERE, '..', '..', 'bin', 'astfilter')


def clang_args(cmd, no_openmp=False):
    a = shlex.split(cmd)
    out, skip = [], False
    for x in a[1:]:
        if skip:
            skip = False
            continue
        if x in ('-o', '-MT', '-MF'):
            skip = True
            continue
        if x.startswith('-flto') or x in ('-fno-fat-lto-objects', '-MD', '-c'):
            continue
        if no_openmp and x == '-fopenmp':
            # clang 18 rejects some OpenMP regions GCC accepts; the reference runs on one thread, so parse the file
            # as GCC sees it with OpenMP's macro defined and its pragmas ignored
            out += ['-fno-openmp', '-D_OPENMP=201511']
            continue
        out.append(x)
    # clang looks for its own libomp omp.h; the engine is built against GCC's libgomp, whose header is linked
    # into tools/cpp2x/ompinc (a folder holding only omp.h, so none of GCC's other builtin headers leak in)
    return (['clang++'] + out + ['-isystem', os.path.join(HERE, 'ompinc'), '-fsyntax-only', '-Wno-everything',
                                 '-Xclang', '-ast-dump=json'])


def dump(job):
    r = dump1(job, False)
    if r['rc'] != (0, 0) and 'not yet supported in OpenMP' in r.get('err', ''):
        r = dump1(job, True)
        r['openmp'] = 'off (clang limitation; _OPENMP defined)'
    r.pop('err', None)
    return r


def dump1(job, no_openmp):
    n, c, src, outdir = job
    dst = os.path.join(outdir, '%03d.jsonl.gz' % n)
    t0 = time.time()
    errf = open(dst + '.err', 'w')
    clang = subprocess.Popen(clang_args(c['command'], no_openmp), cwd=c['directory'], stdout=subprocess.PIPE, stderr=errf)
    filt = subprocess.Popen([FILTER, os.path.join(src, 'src', 'engine') + '/', os.path.join(src, 'include') + '/'],
                            stdin=clang.stdout, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    clang.stdout.close()
    with gzip.open(dst, 'wb', compresslevel=3) as g:
        for chunk in iter(lambda: filt.stdout.read(1 << 20), b''):
            g.write(chunk)
    err = filt.stderr.read().decode()
    rc = (clang.wait(), filt.wait())
    errf.close()
    cerr = open(dst + '.err').read()
    if os.path.getsize(dst + '.err') == 0 or rc == (0, 0):
        os.remove(dst + '.err')
    return {'n': n, 'file': c['file'], 'rc': rc, 'sec': round(time.time() - t0, 1), 'filter': err.strip(), 'err': cerr}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('build'); ap.add_argument('src'); ap.add_argument('out')
    ap.add_argument('-j', type=int, default=2)
    ap.add_argument('--only', help='dump only translation units whose path contains this')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    cdb = json.loads(subprocess.run(['ninja', '-C', a.build, '-t', 'compdb'], capture_output=True, text=True).stdout)
    # compile commands only (ninja's compdb also lists the link)
    tus = [c for c in cdb if 'openswmm_engine.dir' in c['command'] and ' -c ' in ' ' + c['command'] + ' '
           and c['file'].endswith(('.cpp', '.c', '.cc'))]
    tus.sort(key=lambda c: c['file'])
    idx = list(range(len(tus)))
    res = []
    with cf.ThreadPoolExecutor(a.j) as ex:
        for r in ex.map(dump, [(i, c, a.src, a.out) for i, c in enumerate(tus) if not a.only or a.only in c['file']]):
            res.append(r)
            print('%3d %5.1fs %s %s %s' % (r['n'], r['sec'], r['rc'], os.path.relpath(r['file'], a.src), r['filter']), flush=True)
    json.dump(res, open(os.path.join(a.out, 'tus.json'), 'w'), indent=1)
    bad = [r for r in res if r['rc'] != (0, 0)]
    print('%d translation units, %d failed' % (len(res), len(bad)))


if __name__ == '__main__':
    main()
