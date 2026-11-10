Java and Python bind every instance method by name on the object: a C# method that hides a base method of the same signature becomes an override, and a call that C# binds to the base (`base.M()`) reaches the derived class.

The first Java build ran, and every model stopped before reading its input: "Error: failed to create engine
instance." With the engine's exception trace on (SWMM6_CS_TRACE=1), the cause was a stack overflow in

```java
public void __init_d__() {
	this.__init_d__();
}
```

The C# that it came from initialises each class of a hierarchy with a non-virtual method of the same name,
the derived one calling the base one:

```csharp
public void __init_d()          // BuiltinDefaultInputPluginInfo : IPluginComponentInfo
{
    base.__init_d();
    ...
```

Two translator rules combined. `base.__init_d()` was printed as `this.__init_d__()`, because the base-call test
looked only for virtual methods; and both classes' methods had the same Java name, so in Java the derived one
overrides the base one and `this.__init_d__()` calls itself. In C# there are two separate methods and the
compiler picks one by the static type.

The fixes (tools/cs2x: Cs2Java.cs MethodIdent and HidesOrIsHidden, JavaFn.cs Invoke): a non-virtual method that
hides, or is hidden by, one of the same signature elsewhere in its hierarchy gets its class's name appended
(`__init_d___IPluginComponentInfo`), so no Java override can happen by accident; and any call written
`base.M()` in C# is `super.M()` in Java. The 8 smoke models then passed.

Methods that implement a runtime interface (ICopy's Clone and Assign) keep their names on purpose: Java must
see them as the interface's methods. There the override is real, and harmless, because a container that copies
elements holds them by value and their dynamic type is their static type.

Python has the same trap, and it was found the same way, by the Python runtime's part A agent running the
engine's copy constructors: Python looks a method up by name on the object, so `base.Assign(s)` printed as
`self.Assign(s)` calls the derived Assign, which calls itself. The Python translator already gave hidden methods
their class's name, but Assign and MoveFrom keep theirs because they implement the runtime's ICopy, and the
base-call rule looked only for virtual methods. The fix (tools/cs2x/PyFn.cs Invoke): any call written `base.M()`
in C# is printed `Base.M(self, ...)`, the base class's function called directly. It changed 35 calls (11 Assign,
11 MoveFrom, 13 constructor helpers).
