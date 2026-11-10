## vb3DeME - Reverse Engineering tool for legacy lost code

vb3DeME (_**De**compile **Me**_) is a command-line application written entirely in **C# 10**. The core parses and decompiles executable data directly.

DeME can handle large file extractions and even parse through anti-decompilers!

## Build and run

Run `build.bat` or `build.ps1` to build the solution in Release mode.

> Requires, the .NET 10 SDK, then run from this directory:

Release CLI builds place `deme.exe`, its dependencies, and license notices directly in the repository's root `publish/` folder. This also applies to `dotnet build DeME.sln -c Release`. Run `./publish/deme.exe --help` after building. Library and test project build outputs remain in their project `bin` folders.

## Running

* `inspect` - _Checks the target application and provides a pre-report_
* `decompile` - _Decompiles the application_

```powershell
./deme inspect Z:/legacy/FateZero/FATEZERO.EXE
./deme decompile Z:/legacy/FateZero/FATEZERO.EXE
```

**Specifying the output:**

```powershell
./deme inspect {application.exe} --json
./deme decompile {application.exe} --out C:\recovered\APP
```

The output directory must not already exist. Put any companion VBX libraries next to the input executable so their control models can be decoded.

```powershell
./deme decompile C:\legacy\APP.EXE --out C:\recovered\APP --raw --strict
```

## Build for Publishing

```powershell
dotnet publish src/DeME.Cli -c Release --no-self-contained -o artifacts/app
.\artifacts\app\deme.exe --help
```

The published application requires the .NET 10 runtime. For a standalone Windows x64 distribution, publish with `-r win-x64 --self-contained true` instead.

## Outputs and Options

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

This is a working C# reverse engineering application with **partial high-level decompilation**. Unsupported operations are marked in recovered code and recorded in the report. API signature inference, array bounds, and some P-code constructs still require manual restoration. See [recovery limitations](docs/limitations.md) for the exact scope and validation boundary.

```powershell
dotnet run --project tests/DeME.Tests -c Release
dotnet format whitespace DeME.sln --verify-no-changes
```

The test executable has no test-framework dependencies and exits nonzero on failure. Tests generate synthetic VB3 fixtures; no proprietary legacy programs are included. `scripts/verify.ps1` runs the complete restore, Release build, test, and formatting checks.

The solution contains a reusable `DeME.Core` library, the `deme` CLI, and the test executable. All runtime tables are embedded JSON resources. Building does not fetch or build the reference project.

## References

* Based on DoDI Decompiler and [vb3dec](https://github.com/jrmcpeek/vb3dec) - _amazing work, check them out_
* [Third-party attribution and pinned reference revision](THIRD-PARTY-NOTICES.md)
