A C++ pointer to a field (`&obj.x`) has to reach the field itself in a port, because code may keep the pointer and read it after the field changes.

C# cannot take the address of a field, so the first C# back end turned `&x` into a one-element array holding a
copy of x, and copied the array back into x after the call that received it. That is right for an out-parameter,
`time(&now)`, and it is how the SWMM5 ports handled `&x` too. It is wrong when the pointer outlives the call.
SWMM6 builds its flow-routing continuity report this way:

```cpp
const double* routing_terms[] = {&mbr.routing_dry_weather, ..., &mbr.routing_external, ...};
double routing_before[11];
for (int k = 0; k < 11; ++k) routing_before[k] = *routing_terms[k];
updateRoutingMassBalance(dt_next);      // adds this step's volumes to the mbr fields
... (*routing_terms[k] - routing_before[k]) / dt_next ...
```

Every array element was a frozen copy, so each step's change read zero and the report showed External Inflow
0.000 and a continuity error of -147 percent, while the hydraulics (and the binary output file) were already exact.

The fix gives the runtime's pointer type a second form: an object and a function that returns a reference to one
of its fields (`new Ptr<double>(mbr, o => ref ((MassBalance)o).routing_external)`). Reads and writes through it go
to the field. The object is evaluated once, when the address is taken, as in C++. Locals keep the copy-and-copy-back
form, because C# lambdas cannot return references to locals; a pointer to a local that outlives its call would
need a different fix, and none has turned up yet.
