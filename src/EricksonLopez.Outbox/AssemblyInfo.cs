using System;

// ISSUE-BIN2: [assembly: CLSCompliant(true)] declares that the public API surface of this library
// conforms to the Common Language Specification, enabling use from all CLS-compliant languages
// (VB.NET, F#, etc.). Individual members that necessarily use non-CLS types (e.g., uint, byte*,
// or provider-specific types like NpgsqlDataSource) are individually annotated with
// [CLSCompliant(false)] — this is the correct pattern used by the BCL itself (e.g., Span<T>).
// DO NOT change this to CLSCompliant(false) at assembly level: that would make the entire
// public API surface unchecked and would break F# and VB.NET consumers.
[assembly: CLSCompliant(true)]
