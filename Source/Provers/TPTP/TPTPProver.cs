
using System.Diagnostics.Contracts;
using Microsoft.Boogie.SMTLib;
using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class TPTPProver : ProverInterface
{
    public override ProverContext Context => context;
    public override VCExpressionGenerator VCExprGen => context.ExprGen;
    private readonly TPTPOptions options;
    private readonly TPTPContext context;
    private readonly TPTPProverProcessFactory processFactory;
    private readonly TPTPTypeUtils typing;
    private int rcount;

    private readonly ILogger log = Factory.loggerFactory.CreateLogger<TPTPProver>();

    public TPTPProver(
        TPTPOptions options,
        TPTPContext context,
        TPTPProverProcessFactory processFactory
    )
    {
        this.options = options;
        this.context = context;
        this.processFactory = processFactory;
        this.typing = context.Typing;
        this.rcount = 0;
    }

    private TextWriter? GetLogFile()
    {
        string logFilename = options.LogFilename;
        if (logFilename == null)
        {
            return null;
        }

        var (filename, reused) = Helpers.GetLogFilename("", logFilename, false);
        return new StreamWriter(filename, reused);
    }

    private TPTPScopedNamer GetNamer() 
    {
        return this.context.Namer;
    }

    public override async Task<SolverOutcome> Check(
        string descriptiveName,
        VCExpr vc,
        ErrorHandler handler,
        int errorLimit,
        CancellationToken cancellationToken
    )
    {   
        // create the process, log writer and tptp writer
        TextWriter? log = GetLogFile();
        TPTPProverProcess process = processFactory.Start();
        TextWriter tptpWriter = process.TPTPWriter;

        // create a shared log + tptp writer for writing the TPTP both to the log file and to the prover process
        TextWriter wr = log == null ? tptpWriter : new MultiTextWriter(tptpWriter, log);

        // create the lineariser
        TPTPScopedNamer namer = GetNamer();
        TPTPLineariser lin = new TPTPLineariser(wr, namer, typing, context);

        // preprocess the vc
        vc = context.ProcessConjecture(vc);

        // write first all function declarations and axioms to the prover
        TheoryBuilder.Build(context, wr, namer, typing, lin);
        
        // then convert the vc to string
        await wr.WriteAsync("tff(vc, conjecture, ");
        lin.Linearise(vc, new TPTPLineariserOptions());
        await wr.WriteAsync(").\n");

        // vc written -> flush and dispose of the log
        if (log != null)
        { 
            await log.FlushAsync();
            await log.DisposeAsync();
        }

        // send everything in batch and await the response
        SolverOutcome result = await process.GetResult(cancellationToken);
        process.Kill();

        // try get the resource count
        try
        {
            this.rcount = process.GetRCount();
        } 
        catch (NotSupportedException) 
        { 
            // unsupported -> ignore
        }
        
        return result;
    }

    public override void FullReset(VCExpressionGenerator gen)
    {
        // reset the context
        log.LogTrace("Prover did a full reset with a new VCExpressionGenerator: {}", gen.GetHashCode());
        this.context.Reset(gen);
    }

    public override async Task GoBackToIdle()
    {
        // no interactive mode -> no idling
        await Task.CompletedTask;
    }

    public override async Task Reset(VCExpressionGenerator gen)
    {
        // not in interactive mode -> no reset
        await Task.CompletedTask;
    }

    public override void PushVCExpression(VCExpr vc)
    {
        Contract.Requires(vc != null);
        throw new NotImplementedException("PushVCExpression");
    }

    public override string VCExpressionToString(VCExpr vc)
    {
        Contract.Requires(vc != null);
        Contract.Ensures(Contract.Result<string>() != null);
        throw new NotImplementedException("VCExpressionToString");
    }

    public override void Pop()
    {
        Contract.EnsuresOnThrow<UnexpectedProverOutputException>(true);
        throw new NotImplementedException("Pop");
    }

    public override int NumAxiomsPushed()
    {
        throw new NotImplementedException("NumAxiomsPushed");
    }

    public override int FlushAxiomsToTheoremProver()
    {
        throw new NotImplementedException("FlushAxiomsToTheoremProver");
    }

    public override void Assert(VCExpr vc, bool polarity, bool isSoft = false, int weight = 1, string name = "")
    {
        throw new NotImplementedException("Assert");
    }

    public override Task<List<string>> UnsatCore()
    {
        throw new NotImplementedException("UnsatCore");
    }

    public override void AssertAxioms()
    {
        throw new NotImplementedException("AssertAxioms");
    }

    // (check-sat)
    public override void Check()
    {
        throw new NotImplementedException("Check");
    }

    public override Task<(SolverOutcome, List<int>)> CheckAssumptions(List<VCExpr> assumptions, ErrorHandler handler,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException("CheckAssumptions 1");
    }

    public override Task<(SolverOutcome, List<int>)> CheckAssumptions(List<VCExpr> hardAssumptions, List<VCExpr> softAssumptions,
        ErrorHandler handler, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("CheckAssumptions 2");
    }

    public override void Push()
    {
        throw new NotImplementedException("Push");
    }

    public override void SetTimeout(uint ms)
    {
        log.LogTrace("Timeout set to {ms} ms", ms);
        this.options.TimeLimit = ms;
    }

    public override void SetRlimit(uint limit)
    {
        log.LogTrace("Resource limit set to {limit}", limit);
        options.ResourceLimit = limit;
    }

    public override void SetAdditionalSmtOptions(IEnumerable<OptionValue> entries)
    {        
        // no options to set
        if (!entries.Any())
        {
            return;
        }

        // else ignore
        log.LogWarning("Ignoring additional SMT Options: {}", string.Join("; ", entries.Select(x => $"{x.Option}={x.Value}")));
    }

    public override int GetRCount()
    {
        return this.rcount;
    }

    public override void DefineMacro(Macro fun, VCExpr vc)
    {
        throw new NotImplementedException("DefineMacro");
    }

    public override Task<object> Evaluate(VCExpr expr)
    {
        throw new NotImplementedException("Evaluate");
    }

    public override void AssertNamed(VCExpr vc, bool polarity, string name)
    {
        throw new NotImplementedException("AssertNamed");
    }

}