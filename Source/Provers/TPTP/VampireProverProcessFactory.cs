using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class VampireProverProcessFactory : TPTPProverProcessFactory
{

    private readonly ILogger log = Factory.loggerFactory.CreateLogger<VampireProverProcessFactory>();

    private readonly TPTPOptions options;

    public VampireProverProcessFactory(TPTPOptions options)
    {
        this.options = options;
    }

    public static readonly int TimeLimitDelta = 1000;

    public VampireProverProcess Start()
    {
        string execPath = options.ExecutablePath();
        string solverArgs = string.Join(" ", options.SolverArguments
            .Append("-t")
            // overhead causes hard timeout, so that is why there has to be a timeout delta s.t. Vampire terminates a little earlier
            .Append(Math.Max((options.TimeLimit - TimeLimitDelta)/100, 0) + "d")
            .Append("-m")
            .Append(options.MemoryLimit.ToString())
            .Append("--input_syntax")
            .Append("tptp")
        );
        
        log.LogDebug("Starting vampire with: {execPath} {solverArgs}", execPath, solverArgs);
        ProcessStartInfo psi = new ProcessStartInfo(
            execPath,
            solverArgs
        )
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process? process = Process.Start(psi);
        if (process == null)
        {
            throw new FatalError("Vampire process failed to start");
        }
        return new VampireProverProcess(process);
    }

    TPTPProverProcess TPTPProverProcessFactory.Start()
    {
        return Start();
    }
}