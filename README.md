# Vampire plugin for Boogie

## Introduction

A plugin for Boogie that allows verifying Boogie BPL files with TPTP.

## Important notes

- The plugin was developed for Dafny 4.11.0, which uses Boogie 3.5.5.

- The plugin does **not** have support for bitvectors, floats, strings and regular expressions,
and will reject all Boogie programs with those features.

- As TPTP is a language for theorem provers, any Boogie program with invalid specification
will always cause the plugin to search for a (non-existent) correctness proof forever. Hence, 
the plugin can prove a program correct, but it will not give you a counterexample when the 
input program is incorrect. Instead, it will run until the time limit is up.

- The plugin is highly experimental.

## Building

```sh
cd /path/to/boogie-tptp/Source/Provers/TPTP
dotnet tool restore
dotnet build
```

The dotnet tool restore is necessary for [ILRepack](https://github.com/gluck/il-repack), on which this project depends on.

## Running with Dafny

The compiled plugin is in the `bin` folder under `bin/<target>/<dotnet version>/Boogie.Provers.TPTP.dll`. 

This file should be copied to the `dafny/Binaries` folder (but does not require rebuilding Dafny).

After that, one can use the option `--solver-plugin` with `dafny verify` to use TPTP+Vampire instead of SMTLib+Z3.
For sound verification, you must also specify to use the `DafnyTPTPPrelude.bpl` and also tell Dafny that you
are using `vampire`:

```sh
dafny verify \
    --prelude /path/to/boogie-tptp/DafnyTPTPPrelude.bpl \
    --solver-plugin /path/to/dafny/Binaries/Boogie.Provers.TPTP.dll \
    --boogie "/proverOpt:SOLVER=vampire" \
    Program.dfy
```

## Specifying options

One can specify options to the plugin via the `/proverOpt` option of Boogie. If running with Dafny, this has to be combined with the `--boogie` argument:

```sh
dafny verify \
    --prelude /path/to/Boogie.Provers.TPTP/DafnyTPTPPrelude.bpl \
    --solver-plugin /path/to/dafny/Binaries/Boogie.Provers.TPTP.dll \
    --boogie "/proverOpt:C:-t /proverOpt:C:30s /proverOpt:SOLVER=vampire"
    Program.dfy
```

### Plugin options

Supported plugin options:

- `SOLVER=<string>`: the solver to use. The only currently supported value is `vampire`. If used with Dafny, this option *must* be specified, otherwise Dafny will assume that Z3 is in use and pass Z3-specific options that are unknown to this plugin, which will cause it to fail.
- `PROVER_PATH=<string>`: the path to the prover executable (i.e. to the Vampire execuable). If not specified, Boogie will search for `vampire` in `PATH`
- `LOG_FILE=<string>`: dump the full TPTP verification condition to the file specified by `<string>`
- `VERBOSITY=<int>`: follows Microsoft Logger conventions, i.e. `0` is `Trace`, `1` is `Debug`, `2` is `Information`, `3` is `Warning`, `4` is `Error`, `5` is `Critical` and `6` is `None`. Any other value will be ignored and fall back to the default value `2` (`Information`). On `Debug`, the `stdout` and `stderr` of the solver is written to console.
- `TIME_LIMIT=<uint>`: the time limit per verification condition in miliseconds.
- `MEMORY_LIMIT=<int>`: the memory limit for the prover in megabytes.
- `USE_ARRAY_THEORY=<bool>`: whether to use Vampire's built-in `$array` syntax to encode Boogie maps. Beware that Vampire's arrays are extensional, while Boogie maps are not extensional. This discrepancy may lead to unsoundness, and is [unsound with Dafny](https://github.com/dafny-lang/dafny/issues/2463). Defaults to `false`. Also note that the `/useArrayAxioms` option of Boogie is ignored.
- `C:<string>`: pass `<string>` as an additional option to the prover command line. If you need multiple options, repeat this multiple times, e.g. `--mode casc` can be specified as `/proverOpt:C:--mode /proverOpt:C:casc`
- `ENABLE_TYPE_ERASURE=<bool>`: do not use a polymorphic encoding and use Boogie's type erasure instead. Defaults to `false`. The `/typeEncoding` option of Boogie is ignored if this option is not explicitly set to true.

The generic Boogie prover options `BATCH_MODE` and `APPEND_LOG_FILE` are accepted, but ignored. This plugin always runs in batch mode, and the log file will always be overwritten.

### Compatible Boogie options

See Boogie documentation for more information.

- `/normalizeNames:<bool>`: Replace all names with generic labels during veirification. Defaults to `true`.
- `/typeEncoding:<a|p|m>`: Specify which type encoding should be used. This option is ignored unless `ENABLE_TYPE_ERASURE` is specified as a prover option. Defaults to `m` (`monomorphic`)

## Extending

This plugin supports Vampire as its only backend, however, it may be extended to support other TPTP theorem provers too.

The features this plugin uses are polymorphism, integer arithmetic, real arithmetic, booleans, and typed first order form (`tff`).

## Troubleshooting/FAQ

### Broken pipe error 

This most likely means that the prover has died unexpectedly (e.g. an option was malformed, there was an error while processing the VC etc.)

### Dafny 'Invalid value for --solver-plugin: Could not load file or assembly ... (0x80131621)'

Copy the `Boogie.Provers.TPTP.dll` to `dafny/Binaries`, and use `dafny/Binaries/Boogie.Provers.TPTP.dll` as an
argument to `--solver-plugin` (NOT `bin/<target>/<dotnet version>/Boogie.Provers.TPTP.dll`) 

### InvalidOperationException: An attempt was made to transition a task to a final state when it had already completed

Usually this means Vampire or the TPTP linearizer failed/rejected the input. 

Common cause is the use of bitvectors in the input program.

### My timeout is 30 seconds, but Dafny tells me the verification timed out after 28/29 seconds?

This is intentional, Vampire has a slightly smaller timeout to allow for some overhead spent on translating the verification condition into TPTP.

If this were not there, Boogie would forcefully terminate the verification task, which causes an exception instead of a graceful exit.
