using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public enum SolverKind
{
    VAMPIRE
}

public class TPTPOptions : ProverOptions
{
    private readonly ILogger log = Factory.loggerFactory.CreateLogger<TPTPOptions>();

    public bool UseArrayAxioms {
        get {
            // if type erasure is disabled (polymorphic encoding) or the type erasure setting is monomorphic,
            // then respect the user's choice
            if (!EnableTypeErasure || LibOptions.TypeEncodingMethod == CoreOptions.TypeEncoding.Monomorphic)
            {
                return !useBuiltinArrays;
            }

            // else do not use array axioms to avoid superfluous definitions (maps/arrays are type erased)
            return false;
        }
    }

    public bool EnableTypeErasure;

    private bool useBuiltinArrays;

    public List<string> SolverArguments;

    public SolverKind Solver;

    public TPTPOptions(SMTLibOptions libOptions) : base(libOptions)
    {
        // set defaults
        this.SolverArguments = new List<string>();
        this.Solver = SolverKind.VAMPIRE;
        this.ProverName = "vampire";
        this.UsedTypes = [Type.Int, Type.Real, Type.Bool];
        this.Verbosity = (int)Factory.DefaultLogLevel;
        this.useBuiltinArrays = false;
        this.EnableTypeErasure = false;
    }

    protected override bool Parse(string opt)
    {
        // '/proverOpt:C:--show_options /proverOpt:C:on'
        if (opt.StartsWith("C:"))
        {
            SolverArguments.Add(opt.Substring(2));
            return true;
        }

        string? solverStr = null;
        if (ParseString(opt, "SOLVER", ref solverStr))
        {
            switch (solverStr.ToLower())
            {
                case "vampire":
                    this.Solver = SolverKind.VAMPIRE;
                    this.ProverName = "vampire"; // needed for Boogie to automatically determine the vampire executable
                    break;
                default:
                    ReportError("Invalid SOLVER value; must be 'vampire'");
                    return false;
            }

            return true;
        }

        return ParseBool(opt, "ENABLE_TYPE_ERASURE", ref EnableTypeErasure)
        || ParseBool(opt, "USE_BUILTIN_ARRAYS", ref useBuiltinArrays)
        || base.Parse(opt);
    }

    private void SetLogLevel()
    {
        if (Verbosity >= 0 && Verbosity <= 6)
        {
            Factory.runtimeLogLevel.Level = (LogLevel)Verbosity;
        }
        else
        {
            log.LogWarning("Invalid verbosity value passed ({}), ignoring...", Verbosity);
        }
    }

    public override void PostParse()
    {
        SetLogLevel();
        base.PostParse();
    }

    public override string Help
    {
        get
        {
            return
              base.Help +
              @"
TPTP-specific options:
~~~~~~~~~~~~~~~~~~~~~
C:<string>                  Pass <string> to the TPTP solver on the command line.
USE_BUILTIN_ARRAYS:<bool>   Use Vampire's built-in (extensional) arrays (type $array(...)). If false, array axioms are used instead.
                            Note that /useArrayAxioms is unfortunately ignored and not supported. 
                            Defaults to false.
ENABLE_TYPE_ERASURE:<bool>  Enable type erasure. This avoids a polymorphic encoding. One can specify a type erasure via the /typeEncoding option.
                            Defaults to false.
";
        }
    }

}