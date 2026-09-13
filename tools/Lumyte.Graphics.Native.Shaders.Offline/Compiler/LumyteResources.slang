// Explicit host reference semantics. Numeric fields without this annotation are values.
[__AttributeUsage(_AttributeTargets.Var)]
struct LumyteResourceAttribute { string kind; };
