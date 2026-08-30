using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class Factory : ProverFactory
{
    internal static readonly LogLevel DefaultLogLevel = LogLevel.Information;

    internal static readonly RuntimeLogLevel runtimeLogLevel = new RuntimeLogLevel
    { 
        Level = DefaultLogLevel
    };

    internal static readonly ILoggerFactory loggerFactory = LoggerFactory.Create(
        builder => builder
        .AddConsole()
        .AddFilter((category, level) => runtimeLogLevel.Level <= level)
        .SetMinimumLevel(DefaultLogLevel)
    );

    private readonly ILogger log = loggerFactory.CreateLogger<Factory>();

    public override ProverContext NewProverContext(ProverOptions options)
    {
        log.LogTrace("NewProverContext({options})", options);

        // these are already TPTPOptions
        TPTPOptions tptpOptions = (options as TPTPOptions)!;
        return TPTPContext.EmptyContext(tptpOptions);
    }

    public override ProverOptions BlankProverOptions(SMTLibOptions libOptions)
    {
        log.LogTrace("BlankProverOptions({libOptions})", libOptions);
        // libOptions are DafnyOptions, no cast here
        return new TPTPOptions(libOptions);
    }

    public override ProverInterface SpawnProver(
        SMTLibOptions libOptions, // dafny options
        ProverOptions options, // tptp options
        object context // tptp context
    )
    {
        log.LogTrace("SpawnProver({libOptions}, {options}, {ctxt})", libOptions, options, context);

        // cast the vampire options
        TPTPOptions tptpOptions = (options as TPTPOptions)!;
        VampireProverProcessFactory factory = new VampireProverProcessFactory(tptpOptions);
        TPTPContext vContext = (context as TPTPContext)!;
        return new TPTPProver(
            tptpOptions,
            vContext,
            factory
        );
    }
}
