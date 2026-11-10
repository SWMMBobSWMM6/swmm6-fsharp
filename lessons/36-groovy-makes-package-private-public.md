A Java member with no access modifier is package-private; the same text in Groovy is public, and a field with no modifier is a property.

UMap, the runtime's unordered_map, has two three-argument constructors:

```java
	/** UMap(h, eq, mk): hash null is std::hash<K>, eq null std::equal_to<K>. */
	public UMap(Fn.F1<K, Long> h, Fn.F2<K, K, Boolean> eq, Fn.F0<V> mk) {
	...
	UMap(HashOf<K> h, EqOf<K> eq, Fn.F0<V> mk) {          // package-private: for the runtime's own copies
```

J2Gv copied them as written. The Groovy runtime compiled, and the Java check program that tests the hash tables
against libstdc++ (tests/stdcheck/hashcheck.java, compiled against the Groovy runtime's classes) did not:

    hashcheck.java:38: error: reference to UMap is ambiguous
    			UMap<Integer, Integer> mi = new UMap<Integer, Integer>(null, null, () -> 0);
      both constructor UMap(Fn.F1<K,Long>,Fn.F2<K,K,Boolean>,Fn.F0<V>) in UMap and constructor UMap(HashTable.HashOf<K>,HashTable.EqOf<K>,Fn.F0<V>) in UMap match

because Groovy had made the second constructor public. J2Gv now puts `@groovy.transform.PackageScope` on every
class, method, constructor and field that has no access modifier in the Java (302 of them in the runtime), which is
Java's meaning; for a field it also stops Groovy from turning it into a private field with a getter and a setter.
The checks then ran, and agreed with libstdc++ on every sort permutation and hash-table order.

The engine is not affected: Cs2Groovy declares every engine member `public`, as the Java engine does.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
