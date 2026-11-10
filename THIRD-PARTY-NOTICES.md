# Third-party attribution

DeME is an independent, managed C# implementation based on [vb3dec](https://github.com/jrmcpeek/vb3dec), copyright (c) 2026 Jamie McPeek, licensed under the MIT License.

The reference revision is `af94edf9456fd1f8d2bfa12a95624d80fe3aca28`. The NE, VB3 project, form, CDK control model, descriptor rotation, relocation and P-code decoding algorithms are adapted from that revision. Embedded JSON tables in `src/DeME.Core/Tables` are direct data conversions of its opcode, flag-token, control-token and VBRUN control-model tables. They contain 512 opcodes, 10,838 flag tokens, 32,514 control bytes and 26 control/object models, plus standard CDK properties/events.

The full upstream license is retained in `third_party/vb3dec.LICENSE` and copied into published builds. No upstream Go source, executable, runtime, or toolchain is required to build or run DeME.

The upstream project credits Dr. Hans-Peter Diettrich (DoDi) and VBDIS3 for the original research into VB3 P-code and runtime structures.
