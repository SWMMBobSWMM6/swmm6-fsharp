// astfilter - stream clang's JSON AST (-Xclang -ast-dump=json) and keep only the top-level declarations whose
// source file lies under one of the given directory prefixes. Writes one compact JSON object per line, each with
// an added "_file" field naming the file its location was in (clang omits "file" when it repeats the last one,
// so a declaration's file can depend on everything dumped before it).
//
//	clang++ ... -fsyntax-only -Xclang -ast-dump=json | astfilter PREFIX [PREFIX...] > out.jsonl
//
// One pass over the bytes: depth and string state only, no JSON decoding, so a 1.2 GB dump costs a few seconds.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
package main

import (
	"bufio"
	"bytes"
	"fmt"
	"io"
	"os"
	"strconv"
)

var (
	keyFile     = []byte(`"file":"`)
	keyIncluded = []byte(`"includedFrom":{`)
)

func main() {
	prefixes := os.Args[1:]
	if len(prefixes) == 0 {
		fail(fmt.Errorf("usage: astfilter PREFIX [PREFIX...]"))
	}
	in := bufio.NewReaderSize(os.Stdin, 1<<22)
	out := bufio.NewWriterSize(os.Stdout, 1<<22)
	defer out.Flush()

	depth := 0
	var kinds []byte // '{' or '[' for each open container
	inStr, esc := false, false
	var elem []byte // compact bytes of the current top-level declaration
	cur := ""       // the last location file named so far
	kept, total := 0, 0
	for {
		c, err := in.ReadByte()
		if err == io.EOF {
			break
		}
		if err != nil {
			fail(err)
		}
		if inStr {
			if depth >= 3 && len(kinds) >= 2 && kinds[1] == '[' {
				elem = append(elem, c)
			}
			if esc {
				esc = false
			} else if c == '\\' {
				esc = true
			} else if c == '"' {
				inStr = false
			}
			continue
		}
		switch c {
		case ' ', '\n', '\r', '\t':
			continue
		case '"':
			inStr = true
		case '{', '[':
			depth++
			kinds = append(kinds, c)
		case '}', ']':
			depth--
			kinds = kinds[:len(kinds)-1]
		}
		// a declaration is an object directly inside the TranslationUnitDecl's "inner" array (depth 2, '[')
		inInner := len(kinds) >= 2 && kinds[1] == '['
		if (depth >= 3 && inInner) || (depth == 2 && c == '}' && len(elem) > 0) {
			elem = append(elem, c)
		}
		if depth == 2 && c == '}' && len(elem) > 0 {
			// one top-level declaration complete
			total++
			_, last := scanFiles(elem)
			file := locFile(elem, cur)
			if last != "" {
				cur = last
			}
			for _, p := range prefixes {
				if len(file) >= len(p) && file[:len(p)] == p {
					kept++
					out.WriteString(`{"_file":`)
					out.WriteString(strconv.Quote(file))
					out.WriteByte(',')
					out.Write(elem[1:])
					out.WriteByte('\n')
					break
				}
			}
			elem = elem[:0]
		}
	}
	fmt.Fprintf(os.Stderr, "astfilter: kept %d of %d top-level declarations\n", kept, total)
}

// scanFiles returns the first and last location file named in a compact element, in document order, skipping
// the "includedFrom" objects, which name the including file rather than a location.
func scanFiles(b []byte) (first, last string) {
	i := 0
	for {
		j := bytes.Index(b[i:], keyFile)
		if j < 0 {
			return
		}
		at := i + j
		start := at + len(keyFile)
		end := bytes.IndexByte(b[start:], '"')
		if end < 0 {
			return
		}
		i = start + end
		if at >= len(keyIncluded) && bytes.Equal(b[at-len(keyIncluded):at], keyIncluded) {
			continue
		}
		f := string(b[start : start+end])
		if first == "" {
			first = f
		}
		last = f
	}
}

// locFile returns the file of a top-level element's own "loc": its expansion location's file for a macro, else
// its file. Clang omits "file" when it repeats the last location printed, so a "loc" without one is in cur (or, for
// a macro whose expansion is in the same file as its spelling, in the spelling's file). Taking the first "file"
// anywhere in the element instead would pick up a nested macro spelling location in some header and drop a whole
// namespace block of the main file.
func locFile(b []byte, cur string) string {
	k := bytes.Index(b, []byte(`"loc":{`))
	if k < 0 {
		return cur
	}
	loc := object(b, k+len(`"loc":`))
	if e := bytes.Index(loc, []byte(`"expansionLoc":{`)); e >= 0 {
		spell := cur
		if s := bytes.Index(loc, []byte(`"spellingLoc":{`)); s >= 0 {
			if f := ownFile(object(loc, s+len(`"spellingLoc":`))); f != "" {
				spell = f
			}
		}
		if f := ownFile(object(loc, e+len(`"expansionLoc":`))); f != "" {
			return f
		}
		return spell
	}
	if f := ownFile(loc); f != "" {
		return f
	}
	return cur
}

// object returns the JSON object starting at b[i] ('{'), braces matched outside strings.
func object(b []byte, i int) []byte {
	d, in, esc := 0, false, false
	for j := i; j < len(b); j++ {
		c := b[j]
		if in {
			if esc {
				esc = false
			} else if c == '\\' {
				esc = true
			} else if c == '"' {
				in = false
			}
			continue
		}
		switch c {
		case '"':
			in = true
		case '{':
			d++
		case '}':
			d--
			if d == 0 {
				return b[i : j+1]
			}
		}
	}
	return b[i:]
}

// ownFile returns the "file" of a location object, skipping its "includedFrom".
func ownFile(loc []byte) string {
	if inc := bytes.Index(loc, keyIncluded); inc >= 0 {
		loc = loc[:inc]
	}
	k := bytes.Index(loc, keyFile)
	if k < 0 {
		return ""
	}
	start := k + len(keyFile)
	end := bytes.IndexByte(loc[start:], '"')
	if end < 0 {
		return ""
	}
	return string(loc[start : start+end])
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, "astfilter:", err)
	os.Exit(1)
}
