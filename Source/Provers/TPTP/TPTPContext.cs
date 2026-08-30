using Microsoft.Boogie.TypeErasure;
using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class TPTPContext : ProverContext
{

    private TheoryContext theoryContext;
    private VCExpressionGenerator vcExprGen;
    private readonly Boogie2VCExprTranslator translator;
    private readonly VCGenerationOptions vcGenOptions;
    private readonly TPTPOptions proverOptions;
    private readonly TypeAxiomBuilder? typeAxiomBuilder;
    private readonly TPTPTypeUtils typing;
    private readonly TPTPScopedNamer namer;

    // Accessors
    public TheoryContext TheoryContext => theoryContext;
    public TPTPTypeUtils Typing => typing;
    public TPTPScopedNamer Namer => namer;

    public override VCExpressionGenerator ExprGen => vcExprGen;

    public override Boogie2VCExprTranslator BoogieExprTranslator => translator;

    public override VCGenerationOptions VCGenOptions => vcGenOptions;
    public TPTPOptions ProverOptions => this.proverOptions;

    private readonly ILogger log = Factory.loggerFactory.CreateLogger<TPTPContext>();

    private static TPTPScopedNamer NewNamer(TPTPOptions vOptions)
    { 
        TPTPScopedNamer namer;
        if (vOptions.LibOptions.NormalizeNames)
        {
            namer = new TPTPNormalizeNamer();
        }
        else
        {
            namer = new TPTPKeepOriginalNamer();
        }
        return namer;
    }

    public static TPTPContext EmptyContext(TPTPOptions vOptions)
    {
        var vcExprGen = new VCExpressionGenerator();
        var vcGenOptions = new VCGenerationOptions(vOptions.LibOptions, []);
        var translator = new Boogie2VCExprTranslator(vcExprGen, vcGenOptions);
        var namer = NewNamer(vOptions);
        var typing = new TPTPTypeUtils(vOptions, namer);
        var tctx = new TheoryContext();
        return new TPTPContext(
            vcExprGen,
            translator,
            vcGenOptions,
            vOptions,
            namer,
            typing,
            tctx
        );
    }

    public TPTPContext(
        VCExpressionGenerator vcExprGen,
        Boogie2VCExprTranslator translator,
        VCGenerationOptions vcGenOptions,
        TPTPOptions proverOptions,
        TPTPScopedNamer namer,
        TPTPTypeUtils typing,
        TheoryContext theoryContext
    )
    {
        this.vcExprGen = vcExprGen;
        this.translator = translator;
        this.vcGenOptions = vcGenOptions;
        this.typing = typing;
        this.theoryContext = theoryContext;
        this.proverOptions = proverOptions;
        this.namer = namer;
        this.typeAxiomBuilder = SetupAxiomBuilder();
    }

    private TPTPContext(TPTPContext other)
    {
        this.vcExprGen = other.vcExprGen;
        this.translator = (Boogie2VCExprTranslator)other.translator.Clone();
        this.vcGenOptions = other.vcGenOptions;
        this.theoryContext = new TheoryContext(other.theoryContext);
        this.proverOptions = other.proverOptions;
        this.typing = other.typing;
        this.namer = (other.namer.Clone() as TPTPScopedNamer)!;
        this.typeAxiomBuilder = SetupAxiomBuilder();
    }

    private TypeAxiomBuilder? SetupAxiomBuilder()
    {
        log.LogDebug("Type encoding method: {}", proverOptions.LibOptions.TypeEncodingMethod);
        if (!proverOptions.EnableTypeErasure)
        {
            // No type erasure
            return null;
        }

        TypeAxiomBuilder ab;
        switch (proverOptions.LibOptions.TypeEncodingMethod)
        {
            case CoreOptions.TypeEncoding.Arguments:
                ab = new TypeAxiomBuilderArgumentsB(vcExprGen, proverOptions.LibOptions);
                ab.Setup(proverOptions.UsedTypes);
                return ab;
            case CoreOptions.TypeEncoding.Monomorphic:
                return null;
            default:
                ab = new TypeAxiomBuilderPremisses(vcExprGen, proverOptions.LibOptions);
                ab.Setup(proverOptions.UsedTypes);
                return ab;
        }
    }

    private VCExpr DoTypeErasure(VCExpr expr, int polarity)
    {
        if (!proverOptions.EnableTypeErasure)
        {
            // No type erasure
            return expr;
        }

        VCExpr exprWithoutTypes;
        switch (proverOptions.LibOptions.TypeEncodingMethod)
        {
            case CoreOptions.TypeEncoding.Arguments:
                {
                    TypeEraser eraser = new TypeEraserArguments((TypeAxiomBuilderArgumentsB) typeAxiomBuilder!, vcExprGen);
                    exprWithoutTypes = typeAxiomBuilder!.Cast(eraser.Erase(expr, polarity), Type.Bool);
                    break;
                }
            case CoreOptions.TypeEncoding.Monomorphic:
                {
                    exprWithoutTypes = expr;
                    break;
                }
            default:
                {
                    TypeEraser eraser = new TypeEraserPremisses((TypeAxiomBuilderPremisses)typeAxiomBuilder!, vcExprGen);
                    exprWithoutTypes = typeAxiomBuilder!.Cast(eraser.Erase(expr, polarity), Type.Bool);
                    break;
                }
        }
        return exprWithoutTypes;
    }

    // sort the let bindings + do type erasure + collect everything in the processed expression.
    // For processing axioms and the VC.
    // roughly equivalent to the VCExpr2String method from SMTLibProcessTheoremProver 
    private VCExpr ProcessVCExpr(VCExpr expr, int polarity)
    {
        lock (vcExprGen)
        {
            // do type erasure
            VCExpr exprWithoutTypes = DoTypeErasure(expr, polarity);
            VCExpr? newAxioms = typeAxiomBuilder?.GetNewAxioms();
            if (newAxioms != null)
            {
                // do not type erase the type axiom builder axioms, these can be fed just like that
                TheoryCollector.Collect(newAxioms, theoryContext);
                theoryContext.DefinedAxioms.Add(newAxioms);
            }

            // sort the expressions
            LetBindingSorter letSorter = new LetBindingSorter(vcExprGen);
            VCExpr sortedExpr = letSorter.Mutate(exprWithoutTypes, true);

            // mutate the theory context such that it contains declarations, types, etc. of the VCExpr
            TheoryCollector.Collect(sortedExpr, theoryContext);

            // usually after this method, the vcexpr should be added as an axiom, excepth when it is the vc,
            // then it is manually linearised as a conjecture. 
            return sortedExpr;
        }
    }

    // do not let the theory collector mutate the VCExpr. Including the let expression! it is already type erased...

    public VCExpr ProcessConjecture(VCExpr conjecture)
    {
        // polarity 1
        return ProcessVCExpr(conjecture, 1);
    }

    // boogie gives us the abstracted BPL function definitions, axioms, types, etc.

    // roughly equivalent to ProcessFunctionDefinitions() in SMTLibProcessTheoremProver
    public override void DeclareFunction(Function f, string attributes)
    {
        log.LogTrace("New function declared: {}", f.GetHashCode());

        // only define the function if it has a definition body. All other functions are collected via 
        // axiom declaration + theory collector.
        // f.Body ignored in SMTLibProcessTheoremProver, so ignored here too! actually i had it here before, but it caused typing issues

        var defBody = f.DefinitionBody;
        if (defBody != null)
        {
            var translated = (VCExprNAry)translator.Translate(f.DefinitionBody);
            var processed = ProcessVCExpr(translated[1], -1);
            theoryContext.AddFunctionDefinition(f, (VCExprNAry)translated[0], processed); // also declares the function
        }

        base.DeclareFunction(f, attributes);
    }

    public override void AddAxiom(Axiom a, string? attributes)
    {
        log.LogTrace("New axiom added: {} ({})", a, a.GetHashCode());
        // just process the axiom body
        var expr = translator.Translate(a.Expr);
        var assumeId = QKeyValue.FindStringAttribute(a.Attributes, "id");
        if (assumeId != null && proverOptions.LibOptions.TrackVerificationCoverage)
        {
            var v = vcExprGen.Variable(assumeId, Type.Bool, VCExprVarKind.Assume);
            expr = vcExprGen.Function(VCExpressionGenerator.NamedAssumeOp, v, vcExprGen.ImpliesSimp(v, expr));
        }
        log.LogTrace("Axiom became VCExpr: {} -> {}", a.GetHashCode(), expr.GetHashCode());
        AddAxiom(expr);
        base.AddAxiom(a, attributes);
    }

    public override void AddAxiom(VCExpr vc)
    {
        // process the vcexpr (i.e. type erasure, collect the functions/types/... of the axiom etc.)
        // and add it to the theory context
        log.LogTrace("New VCExpr axiom added: {}", vc.GetHashCode());
        var processed = ProcessVCExpr(vc, -1);
        log.LogTrace("VCExpr axiom processed: {} -> {}", vc.GetHashCode(), processed.GetHashCode());
        theoryContext.AddAxiomDefinition(processed);
    }

    public override void DeclareConstant(Constant c, bool uniq, string attributes)
    {
        log.LogTrace("New constant declared: {c}, unique: {uniq}, attributes: {attr}", c, uniq, attributes);

        // if the constant is unique, add it to distinct constants.
        if (uniq)
        {
            // get the type after erasure
            Type typeAfterErasure = c.TypedIdent.Type;
            if (typeAxiomBuilder != null)
            {
                typeAfterErasure = typeAxiomBuilder.TypeAfterErasure(typeAfterErasure);
            }


            theoryContext.AddDistinctConstant(typeAfterErasure, translator.LookupVariable(c));
        }

        base.DeclareConstant(c, uniq, attributes);
    }

    public override void DeclareGlobalVariable(GlobalVariable v, string attributes)
    {
        log.LogTrace("New global variable declared: {v}, attributes: {attr}", v, attributes);

        // ignored also in the SMTLib version...
        // log.LogWarning("Ignoring declared global variable {}", v.GetHashCode());

        base.DeclareGlobalVariable(v, attributes);
    }

    // the SMTLibProcessTheoremProver only processes DatatypeTypeCtorDecl
    // in the Ackermann function, all declared types are of type TypeCtorDecl
    // i.e. no types actually get declared, since PrepareDatatypes only does
    // something when KnownTypes.Size > 0, i.e. types declared via this method
    // that are DatatypeTypeCtorDecl
    public override void DeclareType(TypeCtorDecl t, string attributes)
    {
        log.LogTrace("DeclareType({t}, {attr})", t, attributes);
        if (t is DatatypeTypeCtorDecl)
        {
            throw new NotImplementedException("DatatypeTypeCtorDecl");
        }
        else
        {
            // ignored also in the SMTLib version...
            // log.LogWarning("Ignoring declared datatype {}", t.GetHashCode());
        }
        base.DeclareType(t, attributes);
    }

    public override void Clear()
    {
        throw new NotImplementedException("Clear");
    }

    public override object Clone()
    {
        return new TPTPContext(this);
    }

    public override string Lookup(VCExprVar var)
    {
        throw new NotImplementedException("Lookup");
    }

    public void Reset(VCExpressionGenerator gen)
    {
        log.LogTrace("TPTP Context {} reset with a new VCExpressionGenerator {}", GetHashCode(), gen.GetHashCode());
        this.vcExprGen = gen;
        Reset();
    }

    public override void Reset()
    {
        log.LogTrace("TPTP Context {} reset", GetHashCode());
        this.theoryContext = new TheoryContext();
        SetupAxiomBuilder();
    }
}