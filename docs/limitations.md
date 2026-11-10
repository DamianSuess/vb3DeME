# Recovery scope

DeME is a native C# implementation, not a complete feature-for-feature port of vb3dec. It currently targets Windows 16-bit NE executables importing `VBRUN300`. DOS-only programs, 32-bit PE executables, and VB4/5/6 are unsupported.

## Supported recovery

- Bounds-checked NE headers, segment tables, integer/named resource tables, and imported modules.
- VB3 project titles, startup metadata, form directories, preserved names, and detection of stripped names.
- Built-in control properties from the runtime's property models, nested containers, control arrays, event bindings, font blocks, and FRX pictures.
- Custom VBX control properties/events recovered by inspecting CDK models in companion libraries. Libraries are read as data; they are never loaded or executed.
- Procedure/module descriptor chains, 16-bit descriptor rotation, NE relocation chains, and procedure-to-form mapping.
- Typed numeric/string literals, expression stacks, scalar/array references, assignments, properties, methods, procedure calls, event signatures, common runtime functions, and dialog calls.
- Common If/Else, For/Next, While/Wend, Do/Loop, Select Case, exits, labels, GoTo/GoSub, and error-handling statements.
- Basic Open/Close/Get/Put/Input/Print operations. Graphics statements and some file access variants remain incomplete.
- Global variable declarations derived from addressing modes, and shared module variables inferred from references. Inference is approximate.
- Windows-1252 text and CRLF for VB3, or optional UTF-8 for modern readers.

## Gaps requiring manual work

- High-level P-code reconstruction is incomplete. Unsupported tokens produce `DeME: Unhandled P-code` comments and report warnings; expression-stack underflow stops source recovery for that procedure. Unknown raw opcodes stop decoding rather than guessing the next instruction boundary.
- Raw bytecode and text listings are always exported with code recovery. A malformed procedure may have only a diagnostic listing; its complete raw bytecode is still retained.
- External API parameter types and ByVal/ByRef conventions are placeholders. Non-event procedure signatures and type inference require review. Inter-procedural signature inference is not implemented.
- Array dimensions, `ReDim`, user-defined records, and several runtime/graphics/file operations need further decoding. Array bounds can be missing. Do not treat generated declarations as proof of original types.
- Variables and procedures generally receive generated names because those names are not retained by the compiler. Comments cannot be recovered. Some cross-form control/property references remain placeholders.
- Missing or unsupported VBX models preserve the control but omit its properties, with warnings. Shape/Data control IDs are not mapped. Form window bounds use the same approximate border/caption sizes as upstream.
- `Sub Main` identification is incomplete. Set the startup entry in the VB3 IDE when the report requests it. `.MAK` files list recovered artifacts and use `IconForm` as the upstream project does; project settings need review.
- A clean diagnostic report is not a guarantee of compilable or behaviorally equivalent source. The recovered output is intended for reverse engineering and manual restoration.

## Validation boundary

The automated tests build synthetic NE/VB3 fixtures in C#, including a custom VBX model and a procedure bound to a button Click event. They cover form/source/asset export, text encodings, control flow, globals, corrupt lengths, descriptor/relocation cycles, output path validation, collision refusal, and cancellation.

The upstream repository does not distribute its FF, BasCode, or Empire executable fixtures. No real application executable has been supplied in this workspace, so those programs and full VB3 IDE round trips have not been validated. Add real-program regression fixtures only when you have permission to distribute them.
