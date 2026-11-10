import sys, struct
sys.path.insert(0, sys.argv[1]); sys.path.insert(0, sys.argv[2])
import engine
for n in sorted(k for k in dir(engine) if k.startswith('F_dt_t') and k[6:7].isdigit()):
    try: r = getattr(engine, n)()
    except Exception as e: r = 'EXC ' + type(e).__name__ + ' ' + str(e)
    if type(r) is bool: v = 'True' if r else 'False'
    elif type(r) is float: v = 'd' + str(struct.unpack('<q', struct.pack('<d', r))[0])
    elif r is None: v = 'None'
    else: v = str(r)
    print(n[5:] + '=' + v)
