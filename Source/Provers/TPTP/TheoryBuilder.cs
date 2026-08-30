using Microsoft.Boogie.SMTLib;
using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

/// <summary>
/// Aggregate for building the entire theory behind the VC, i.e. function declarations, definitions, 
/// type declarations, constants, etc.
/// 
/// Does not mutate the theory context.
/// </summary>
public class TheoryBuilder
{

    private readonly ILogger<TheoryBuilder> log = Factory.loggerFactory.CreateLogger<TheoryBuilder>();
    private readonly TextWriter wr;
    private readonly ScopedNamer namer;
    private readonly TPTPTypeUtils typing;
    private readonly TPTPLineariser lin;

    public TheoryBuilder(TextWriter wr, ScopedNamer namer, TPTPTypeUtils typing, TPTPLineariser lin)
    {
        this.wr = wr;
        this.namer = namer;
        this.typing = typing;
        this.lin = lin;
    }

    // === Declaration processing ===
    private int declaredVariables = 0;
    public void DeclareVariable(VCExprVar v)
    {
        string quotedName = namer.GetQuotedVariableName(v);
        string typeStr = typing.TypeToString(v.Type);

        wr.Write("tff(var{0}_decl, type, {1}: {2}).\n", declaredVariables++, quotedName, typeStr);
    }

    private int declaredFunctions = 0;

    private void DeclareFunction(IEnumerable<TypeVariable> typeVariables, string name, IEnumerable<Type> inParamTypes, Type resultType)
    { 
        namer.PushScope();
        try
        {
            wr.Write("tff(fun{0}_decl, type, {1}: ", declaredFunctions++, name);

            // write the polymorphic type parameters, if there are any
            if (typeVariables.Count() != 0)
            {
                wr.Write("!>[");

                bool first = true;
                foreach (TypeVariable t in typeVariables)
                {
                    if (!first) { wr.Write(", "); }
                    first = false;

                    string typeName = typing.TypeToString(t);
                    wr.Write("{0}: $tType", typeName);
                }

                wr.Write("]: ");
            }

            // write the function signature itself
            var inTypes = string.Join(" * ", inParamTypes.Select(t => typing.TypeToString(t)));
            var outType = typing.TypeToString(resultType);

            if (inParamTypes.Count() == 0)
            {
                wr.Write("{0}).\n", outType);
            }
            else
            {
                wr.Write("({0}) > {1}).\n", inTypes, outType);
            }
        }
        finally
        {
            namer.PopScope();
        }
    }

    public void DeclareFunction(Function f)
    {
        if (f.OutParams.Count != 1)
        {
            throw new NotSupportedException("Complex function return types are not supported in TPTP");
        }

        DeclareFunction(
            f.TypeParameters,
            namer.GetQuotedName(f, f.Name),
            f.InParams.Select(v => v.TypedIdent.Type),
            f.OutParams[0].TypedIdent.Type
        );
    }


    public void DeclareSelectStoreFunctions(IEnumerable<int> arities)
    {
        foreach (int arity in arities)
        {
            DeclareSelectFunction(arity);
            DeclareStoreFunction(arity);
        }
    }

    public void DeclareSelectFunction(int arity)
    {
        var select = ArrayHelper.SelectFunctionName(arity);
        select = TPTPNameUtils.GetQuotedName(namer, select, select);
        var mapType = ArrayHelper.GenericMapType(arity); // has only free vars
        DeclareFunction(
            mapType.FreeVariables, // bind the free variables with the polymorphic forall !>[...]
            select,
            [mapType, ..mapType.Arguments],
            mapType.Result
        );
    }

    public void DeclareStoreFunction(int arity)
    { 
        var store = ArrayHelper.StoreFunctionName(arity);
        store = TPTPNameUtils.GetQuotedName(namer, store, store);
        var mapType = ArrayHelper.GenericMapType(arity);
        DeclareFunction(
            mapType.FreeVariables, // bind the free variables with the polymorphic forall !>[...]
            store,
            [mapType, ..mapType.Arguments, mapType.Result],
            mapType
        );
    }

    private int declaredTypes = 0;

    // just for better verbosity
    private void ThrowOnUnsupportedType(Type t)
    { 
        if (t.IsFloat)
        {
            throw new NotSupportedException("Floats are not supported in TPTP");
        }
        else if (t.IsRMode)
        { 
            throw new NotSupportedException("Rounding modes are not supported in TPTP");
        }
        else if (t.IsString)
        { 
            throw new NotSupportedException("Strings are not supported in TPTP");
        }
        else if (t.IsRegEx)
        { 
            throw new NotSupportedException("Regular expressions are not supported in TPTP");
        }
        else if (t.IsBv)
        { 
            throw new NotSupportedException("Bitvectors are not supported in TPTP");
        }
    }

    public void DeclareType(Type t)
    {
        // raw TPTP types do not have to be declared, as well as polymorphic types
        if (t.IsBool || t.IsInt || t.IsReal || t.IsVariable)
        {
            return;
        }

        ThrowOnUnsupportedType(t);

        if (t is CtorType ct)
        {
            // built-in types do not need declarations
            if (ct.GetBuiltin() != null)
            {
                return;
            }

            if (ct.IsDatatype())
            {
                return;
            }
        }

        // if it is a map, typeToString infers the actual instantiated map type
        // but the declaration only wants the name
        string typeStr;
        if (t.IsMap)
        {
            typeStr = ArrayHelper.ArrayTypeName(t.AsMap.MapArity);
        }
        else 
        { 
            typeStr = typing.TypeToString(t);
        }

        wr.Write("tff(type{0}_decl, type, {1}: ", declaredTypes++, typeStr);

        // write the free variables as polymorphic type parameters, if any
        if (t.FreeVariables.Count != 0)
        {
            wr.Write("(" + string.Join(" * ", t.FreeVariables.Select(p => "$tType")) + ") > ");
        }

        wr.WriteLine("$tType).");
    }

    public void DeclareTypes(IEnumerable<Type> types)
    {
        foreach (Type t in types)
        {
            DeclareType(t);
        }
    }

    public void DeclareFunctions(IEnumerable<Function> functions)
    {
        // functions
        foreach (Function f in functions)
        {
            DeclareFunction(f);
        }
    }

    public void DeclareVariables(IEnumerable<VCExprVar> vars)
    {
        foreach (VCExprVar v in vars)
        {
            DeclareVariable(v);
        }
    }

    // === Definition processing ===
    private int functionDefinitions = 0;
    private void DefineFunction(Function f, VCExprNAry call, VCExpr body)
    {
        // todo polymorphism?
        // todo function unused???
        var op = (VCExprBoogieFunctionOp)call.Op;
        var funcName = namer.GetQuotedName(op.Func, op.Func.Name);
        wr.Write("tff(fun{0}_def, definition, ![", functionDefinitions++);

        // for all arguments of the function ...
        bool first = true;
        foreach (var v in call.UniformArguments)
        {
            if (!first) wr.Write(", ");

            first = false;
            VCExprVar varExpr = (v as VCExprVar)!;
            string localName = namer.GetQuotedLocalName(varExpr, varExpr.Name);
            wr.Write("{0}: {1}", localName, typing.TypeToString(varExpr.Type));
        }

        // ... the function applied to these arguments ...
        wr.Write("]: ({0}(", funcName);
        first = true;
        foreach (var v in call.UniformArguments)
        {
            if (!first) wr.Write(", ");

            first = false;
            VCExprVar varExpr = (v as VCExprVar)!;
            string localName = namer.GetQuotedName(varExpr, varExpr.Name);
            wr.Write("{0}", localName);
        }

        // ... is equivalent to the definition body.
        string eqSign = call.Type.IsBool ? "<=>" : "=";
        wr.Write(") {0} (", eqSign);
        lin.Linearise(body, wr);
        wr.Write("))).\n");
    }

    private void DefineFunctions(Dictionary<Function, (VCExprNAry, VCExpr)> functions)
    {
        foreach (Function f in functions.Keys)
        {
            var (call, body) = functions[f];
            DefineFunction(f, call, body);
        }

    }

    private int numAxioms = 0;

    private void DefineAxiom(VCExpr expr)
    {
        StringWriter sw = new StringWriter();
        
        sw.Write("tff(axiom{0}_def, axiom, ", numAxioms++);
        lin.Linearise(expr, sw);
        sw.Write(").\n");

        string axiom = sw.ToString();
        log.LogTrace("Axiom {} became {}", expr.GetHashCode(), axiom);
        wr.Write("{0}", axiom);
    }

    public void DefineAxioms(IEnumerable<VCExpr> axioms)
    {
        foreach (VCExpr a in axioms)
        {
            DefineAxiom(a);
        }
    }

    public void DeclareDistinctConstants(Dictionary<Type, List<VCExprVar>> distinctConstants)
    { 
        foreach (List<VCExprVar> distincts in distinctConstants.Values)
        {
            if (distincts.Count <= 1)
            {
                // single constant always distinct
                continue;
            }
            
            // then declare them
            DeclareDistinctConstants(distincts);
        }
    }

    private int numDistinct = 0;
    public void DeclareDistinctConstants(List<VCExprVar> distinct)
    { 
        if (distinct.Count <= 1)
        {
            // single constant always distinct
            return;
        }

        wr.Write("tff(axiom_distinct{0}, axiom, $distinct(", numDistinct++);

        bool first = true;
        foreach (VCExprVar v in distinct)
        {
            if (!first) { wr.Write(", "); }
            first = false;

            lin.Linearise(v, wr);
        }

        wr.Write(")).\n");
    }


    // Declare all needed functions, types etc. from the given tptp context.
    // Write it to the given textwriter.
    // Uses the given linearizer and namer to linearize expressions contained in function definitions etc.
    // Everything is readonly, i.e. no mutation of the context/namer/typing happens (exception for the writer, where
    // the axiomatizations are written).
    public static bool Build(TPTPContext ctx, TextWriter wr, ScopedNamer namer, TPTPTypeUtils typing, TPTPLineariser lin)
    {
        // axiomatizers just provide methods for declaration, this methods orchestrates all of them
        TheoryBuilder builder = new TheoryBuilder(wr, namer, typing, lin);
        TheoryContext tctx = ctx.TheoryContext;

        // types are just names, they do not depend on anything, so declare them first
        builder.DeclareTypes(tctx.DeclaredTypes);
        if (ctx.ProverOptions.UseArrayAxioms)
        { 
            builder.DeclareTypes(tctx.DeclaredMapTypeArities.Select(ArrayHelper.GenericMapType));
        }

        // declarations
        builder.DeclareVariables(tctx.DeclaredVariables);
        if (ctx.ProverOptions.UseArrayAxioms)
        { 
            builder.DeclareSelectStoreFunctions(tctx.DeclaredMapTypeArities);
        }
        builder.DeclareFunctions(tctx.DeclaredFunctions);

        // definitions
        if (ctx.ProverOptions.UseArrayAxioms)
        {
            foreach (int arity in tctx.DeclaredMapTypeArities)
            {
                builder.DefineAxiom(ArrayHelper.ReadOverWrite1(arity));
                builder.DefineAxiom(ArrayHelper.ReadOverWrite2(arity));
            }
        }
        builder.DefineFunctions(tctx.DefinedFunctions);
        builder.DefineAxioms(tctx.DefinedAxioms);
        builder.DeclareDistinctConstants(tctx.DistinctConstants);

        return true;
    }
}