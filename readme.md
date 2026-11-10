VB3 Reverse Engineering tool for legacy lost code

DeME is a command-line application written entirely in **C# 10**. The core parses and decompiles executable data directly; it does not invoke Go, native decompilers, or the target program. It targets **.NET 10**, with `LangVersion` fixed to `10.0`.

## Build and run

Install the .NET 10 SDK, then run from this directory:

```powershell
dotnet build DeME.sln -c Release -m:1
dotnet run --project src/DeME.Cli -- inspect C:\legacy\APP.EXE --json
dotnet run --project src/DeME.Cli -- decompile C:\legacy\APP.EXE --out C:\recovered\APP
```

The output directory must not already exist. Put any companion VBX libraries next to the input executable so their control models can be decoded.

```powershell
dotnet run --project src/DeME.Cli -- decompile C:\legacy\APP.EXE --out C:\recovered\APP --raw --strict
dotnet publish src/DeME.Cli -c Release --no-self-contained -o artifacts/app
.\artifacts\app\deme.exe --help
```

The published application requires the .NET 10 runtime. For a standalone Windows x64 distribution, publish with `-r win-x64 --self-contained true` instead.

## Outputs and options

Exports include `.MAK`, `.FRM` with recovered event procedures, `.FRX`, code-only `.bas` modules, inferred `Globals.bas` where needed, standalone media in `assets/`, and raw procedure bytecode/listings in `pcode/`. `report.json` records the input SHA-256, recovered counts, file list, and warnings. Extraction writes into a temporary sibling directory and publishes the output only after all files are written. Existing output paths are refused.

| Option | Behavior |
| --- | --- |
| `--out`, `-o` | New output directory; defaults to `<name>_decompiled` |
| `--no-forms` | Omit visual form/FRX output |
| `--no-code` | Omit source and procedure P-code output |
| `--no-assets` | Omit standalone media files |
| `--raw` | Also export original NE resource payloads |
| `--utf8` | UTF-8 for modern readers; default is VB3 Windows-1252 |
| `--copy-dependencies` | Copy companion DLL/VBX files into the output |
| `--strict` | Exit 3 if recovery warnings occur; retain the recovered files |

Exit codes: `0` successful export/inspection, `1` input or I/O error, `2` invalid arguments, `3` warnings in strict mode, `130` cancellation. A successful export can contain incomplete source; review the report and generated comments.

## Scope and validation

This is a working C# reverse engineering application with **partial high-level decompilation**, not full parity with vb3dec. Unsupported operations are marked in recovered code and recorded in the report. API signature inference, array bounds, and some P-code constructs still require manual restoration. See [recovery limitations](docs/limitations.md) for the exact scope and validation boundary.

```powershell
dotnet run --project tests/DeME.Tests -c Release
dotnet format whitespace DeME.sln --verify-no-changes
```

The test executable has no test-framework dependencies and exits nonzero on failure. Tests generate synthetic VB3 fixtures; no proprietary legacy programs are included. `scripts/verify.ps1` runs the complete restore, Release build, test, and formatting checks. Builds enforce the existing `.editorconfig`, StyleCop analyzers, C# 10 language syntax, and warnings as errors.

The solution contains a reusable `DeME.Core` library, the `deme` CLI, and the test executable. All runtime tables are embedded JSON resources. Building does not fetch or build the reference project.

## References

* Based on [vb3dec](https://github.com/jrmcpeek/vb3dec)
* [Third-party attribution and pinned reference revision](THIRD-PARTY-NOTICES.md)
