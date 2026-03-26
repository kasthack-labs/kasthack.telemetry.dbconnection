// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Basically, type casts for tests. Strings are not involved.", Scope = "module")]
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Test assembly", Scope = "module")]
[assembly: SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "Test assembly, we can ignore this", Scope = "module")]
