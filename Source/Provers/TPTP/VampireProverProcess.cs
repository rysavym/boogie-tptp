using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public partial class VampireProverProcess : TPTPProverProcess
{

    private readonly ILogger log = Factory.loggerFactory.CreateLogger<VampireProverProcess>();

    private const int ExitSuccess = 0;
    private const int ExitResultUndetermined = 1;
    private const int ExitVampireInternalError = 2;
    private const int ExitInterrupt = 3;
    private const int ExitUserError = 4;
    private readonly Process process;
    private int rcount;
    private SolverOutcome outcome;
    private bool getResultCalled;
    private bool terminated => getResultCalled || process.HasExited;

    public TextWriter TPTPWriter => tptp;

    private readonly StringWriter tptp;

    public VampireProverProcess(Process process)
    {
        this.process = process;
        this.tptp = new StringWriter();
        this.rcount = 0;
        this.outcome = SolverOutcome.Undetermined;
        this.getResultCalled = false;
    }

    private void ParseLine(string line)
    {
        // try parse the resource count
        var match = Regex.Match(line, @"^% Instructions burned:\s+(?<instructions>\d+)\s+\(million\)$");
        if (match.Success)
        {
            this.rcount = Math.Max(int.MaxValue, rcount + int.Parse(match.Groups["instructions"].Value));
            return;
        }

        // try parse the termination reason
        // https://github.com/vprover/vampire/blob/ec9595e79e4890d82cf090223c6cda5b73805bbc/Shell/Statistics.hpp#L39
        // Refutation, Satisfiable, Refutation not found (?), Time limit, Instruction limit, Memory limit, Activation limit,
        // Unknown, Inappropriate 
        if (line.Contains("Termination reason"))
        {
            if (line.Contains("Refutation"))
            {
                if (line.Contains("Refutation not found"))
                {
                    outcome = SolverOutcome.Invalid;
                }
                else
                {
                    outcome = SolverOutcome.Valid;
                }
            }
            else if (line.Contains("Satisfiable"))
            {
                outcome = SolverOutcome.Invalid;
            }
            else if (line.Contains("Time limit"))
            {
                outcome = SolverOutcome.TimeOut;
            }
            else if (line.Contains("Memory limit"))
            {
                outcome = SolverOutcome.OutOfMemory;
            }
            else if (line.Contains("Activation limit"))
            {
                outcome = SolverOutcome.OutOfResource;
            }
            else if (line.Contains("Instruction limit"))
            { 
                outcome = SolverOutcome.OutOfResource;
            }
            else if (line.Contains("Unknown"))
            {
                outcome = SolverOutcome.Undetermined;
            }
            else if (line.Contains("Inappropriate"))
            {
                // inappropriate strategy
                outcome = SolverOutcome.Undetermined;
            }
            else
            {
                log.LogWarning("Could not determine termination reason: {}", line);
            }
        }
        // in CASC and Portfolio mode, the timeout does not get 
        // reported via 'Termination Reason' but via SZS
        else if (line.Contains("SZS status Timeout"))
        {
            outcome = SolverOutcome.TimeOut;
        }
    }

    private void ThrowOnUnexpectedTermination(string stdout = "", string stderr = "")
    {
        if (!process.HasExited)
        {
            return;
        }

        int exitCode = process.ExitCode;
        switch (exitCode)
        {
            case ExitSuccess:
                // OK
                return;
            case ExitResultUndetermined:
                // e.g. timeout, refutation not found
                return;
            case ExitVampireInternalError:
                // e.g. SIGSEGV
                log.LogWarning("Vampire terminated unexpectedly with exit code {}\n===stdout===\n{}\n===stderr===\n{}\n", exitCode, stdout, stderr);
                throw new ProverException("Vampire terminated unexpectedly with exit code " + ExitVampireInternalError);
            case ExitInterrupt:
                // e.g. Ctrl+C
                log.LogWarning("Vampire was interrupted.\n===stdout===\n{}\n===stderr===\n{}\n", stdout, stderr);
                throw new ProverException("Vampire was interrupted before it could terminate");
            case ExitUserError:
                // e.g. wrong option on command line, invalid TPTP syntax
                log.LogWarning("Vampire terminated due to a user error.\n===stdout===\n{}\n===stderr===\n{}\n", stdout, stderr);
                throw new ProverException("Vampire died due to a user error. Did you specify all Vampire options correctly?");
            default:
                // died with an unexpected error exit code
                log.LogWarning("Unknown Vampire exit code: {}.\n===stdout===\n{}\n===stderr===\n{}\n", exitCode, stdout, stderr);
                throw new ProverDiedException();
        }

    }

    public async Task<SolverOutcome> GetResult(CancellationToken cancellationToken = default)
    {
        log.LogTrace("GetResult()");
        if (getResultCalled)
        {
            return outcome;
        }

        // it is possible that Vampire died due to invalid options
        ThrowOnUnexpectedTermination();

        // write everything at once such that Vampire does not fail in the middle of TPTP linearization if there is some wrong encoding
        // this will cause the log file to be written no matter what Vampire responds (i.e. even when the TPTP encoding is somehow wrong -> useful for debugging).
        // also it has to be a WriteLine because Vampire sometimes does not terminate if there is no newline at the end
        log.LogDebug("Closing Vampire's stdin");
        TextWriter stdin = process.StandardInput;
        await stdin.WriteLineAsync(tptp.ToString());
        await stdin.FlushAsync();
        await stdin.DisposeAsync();


        log.LogDebug("Reading Vampire's stdout");

        // read the stdout and parse the results
        StringBuilder stdoutSb = new StringBuilder();;
        string? line;
        while ((line = process.StandardOutput.ReadLine()) != null)
        {
            ParseLine(line);
            stdoutSb.Append(line).Append('\n');
        }
        string stdout = stdoutSb.ToString();

        // also need to read the stderr, otherwise WaitForExitAsync may hang
        log.LogDebug("Reading Vampire's stderr");
        string stderr = await process.StandardError.ReadToEndAsync();

        // Write all stdout/stderr at once to avoid race conditions
        log.LogDebug("===stdout===\n{}\n===stderr===\n{}\n", stdout, stderr);

        // await termination
        log.LogDebug("Awaiting Vampire's termination");
        await process.WaitForExitAsync(cancellationToken);

        // Throw ProverException on unexpected terminations
        ThrowOnUnexpectedTermination(stdout, stderr);

        log.LogDebug("Vampire terminated\nTermination reason: {}\nInstructions burned: {}", outcome, rcount);
        getResultCalled = true;
        return outcome;
    }

    public void Kill()
    {
        if (terminated)
        {
            return;
        }
        process.Kill();
    }

    public int GetRCount()
    {
        if (!getResultCalled)
        {
            throw new InvalidOperationException("GetRCount is only supported after GetResult() has successfully terminated");
        }
        return this.rcount;
    }
}