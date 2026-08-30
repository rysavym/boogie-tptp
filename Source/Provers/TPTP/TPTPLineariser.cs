using System.Diagnostics.Contracts;
using System.Text;
using Microsoft.BaseTypes;
using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class TPTPLineariserOptions
{
    public static TPTPLineariserOptions Default = new();
}

/// <summary>
/// Lineariser from VCExpr to TPTP. Main difference to SMTLibLineariser is the pure recursive implementation (FIXME?)
/// </summary>
public class TPTPLineariser : IVCExprVisitor<bool, TPTPLineariserOptions>
{

    private readonly ILogger<TPTPLineariser> log = Factory.loggerFactory.CreateLogger<TPTPLineariser>();
    private readonly TPTPOpLineariser opLineariser;
    private readonly UniqueNamer namer;
    private readonly TextWriter wr;
    private readonly TPTPTypeUtils typing;
    private readonly TPTPContext context;

    public TPTPLineariser(TextWriter wr, UniqueNamer namer, TPTPTypeUtils typing, TPTPContext context)
    {
        this.opLineariser = new TPTPOpLineariser(this, wr, namer, context, typing);
        this.namer = namer;
        this.wr = wr;
        this.typing = typing;
        this.context = context;
    }

    public static string ToString(VCExpr e, TPTPLineariser lin)
    {
        StringWriter sw = new StringWriter();
        lin.Linearise(e, sw);
        return sw.ToString();
    }

    public void Linearise(VCExpr e, TextWriter wr2)
    {
        TPTPLineariser lin = new TPTPLineariser(wr2, this.namer, this.typing, context);
        lin.Linearise(e!, TPTPLineariserOptions.Default);
    }

    public void Linearise(VCExpr vc, TPTPLineariserOptions options)
    {
        Contract.Requires(vc != null);
        Contract.Requires(options != null);
        vc!.Accept<bool, TPTPLineariserOptions>(this, options!);
    }

    // ToDecimalString sometimes writes .5 instead of 0.5
    // or -.5 instead of -0.5, so here is a tiny workaround
    private string BigDecToTPTPReal(BigDec bigDec)
    { 
        var s = bigDec.ToDecimalString();

        if (s.StartsWith("."))
        { 
            return "0" + s;
        }

        if (s.StartsWith("-."))
        { 
            return "-0" + s[1..];
        }

        return s;
    }

    public bool Visit(VCExprLiteral node, TPTPLineariserOptions arg)
    {
        if (node == VCExpressionGenerator.True)
        {
            wr.Write("$true");
        }
        else if (node == VCExpressionGenerator.False)
        {
            wr.Write("$false");
        }
        else if (node is VCExprIntLit)
        {
            BigNum lit = ((VCExprIntLit)node).Val;
            wr.Write(lit.ToString());
        }
        else if (node is VCExprRealLit)
        {
            BigDec lit = ((VCExprRealLit)node).Val;
            wr.Write(BigDecToTPTPReal(lit));
        }
        else if (node is VCExprFloatLit)
        {
            // unsupported by TPTP
            throw new NotSupportedException("Floating point literals are not supported in TPTP");
        }
        else if (node is VCExprRModeLit)
        {
            // unsupported by TPTP
            throw new NotSupportedException("Rounding mode literals are not supported in TPTP");
        }
        else if (node is VCExprStringLit)
        {
            // unsupported by TPTP
            throw new NotSupportedException("String operations are not supported in TPTP");
        }
        else
        {
            Contract.Assert(false);
            throw new Cce.UnreachableException();
        }

        return true;
    }

    public bool Visit(VCExprNAry node, TPTPLineariserOptions options)
    {
        VCExprOp op = node.Op;

        // TODO: prevent stack overflows by linearising the boolean operators with a stack,
        // as done in the SMTLibLinearizer

        if (op.Equals(VCExpressionGenerator.MinimizeOp) || op.Equals(VCExpressionGenerator.MaximizeOp))
        {
            // https://microsoft.github.io/z3guide/docs/optimization/arithmeticaloptimization/
            log.LogWarning("The VC has a minimization/mazimization operator, which is not supported by TPTP. The operator will be ignored.");
            Linearise(node[1], options);
            return true;
        }
        if (op is VCExprSoftOp)
        {
            // no soft assertions in TPTP
            log.LogWarning("The VC has a soft assertion, which is not supported by TPTP. The soft assertion will be ignored.");
            Linearise(node[1], options);
            return true;
        }
        if (op.Equals(VCExpressionGenerator.NamedAssumeOp) || op.Equals(VCExpressionGenerator.NamedAssertOp))
        {
            // no named assertions in TPTP
            log.LogWarning("The VC has a named assertion, which is not supported by TPTP. The assertion will be taken into account, but the name will be ignored.");
            Linearise(node[1], options);
            return true;
        }

        return node.Accept<bool, TPTPLineariserOptions>(opLineariser, TPTPLineariserOptions.Default);
    }

    public bool Visit(VCExprVar node, TPTPLineariserOptions arg)
    {
        wr.Write(namer.GetQuotedVariableName(node));
        return true;
    }

    public bool Visit(VCExprQuantifier node, TPTPLineariserOptions arg)
    {
        string kind = (node.Quan == Quantifier.ALL) ? "!" : "?";
        wr.Write("{0}[", kind);
        namer.PushScope();

        // first quantify over all type parameters
        bool first = true;
        foreach (TypeVariable t in node.TypeParameters)
        {
            // TextWriter does not have a method to delete characters...
            if (!first) { wr.Write(", "); }
            first = false;
            
            wr.Write("{0}: $tType", TPTPNameUtils.GetLocalName(namer, t, t.Name));
        }

        for (int i = 0; i < node.BoundVars.Count; i++)
        {
            if (!first) { wr.Write(", "); }
            first = false;

            VCExprVar var = node.BoundVars[i];
            Contract.Assert(var != null);
            // use the modified GetLocalName from the TPTPNameUtils (capitalizes bound variables)
            string varName = TPTPNameUtils.GetLocalName(namer, var, var.Name);
            var varType = var.Type;

            string printedVarType = typing.TypeToString(varType);

            Contract.Assert(varName != null);
            wr.Write("{0}: {1}", varName, printedVarType);
        }
        wr.Write("]: (");
        // linearise the quantifier body
        Linearise(node.Body, arg);
        wr.Write(")");
        namer.PopScope();
        return true;
    }

    public bool Visit(VCExprLet node, TPTPLineariserOptions arg)
    {
        namer.PushScope();
        try
        {
            bool first = true;
            foreach (VCExprLetBinding b in node)
            {
                if (!first) { wr.Write(", "); }
                first = false;

                wr.Write("$let(");
                Contract.Assert(b != null);
                wr.Write("{0}: {1}, {0} := ", namer.GetQuotedVariableName(b.V), typing.TypeToString(b.E.Type));

                // write parentheses around bools but not around terms
                // this is due to TPTP interpreting terms as logic formulas if they are in parenthesis, which causes a syntax error 
                bool exprIsBool = b.E.Type.IsBool;
                if (exprIsBool)
                {
                    wr.Write("(");
                    Linearise(b.E, arg);
                    wr.Write(")");
                }
                else
                {
                    Linearise(b.E, arg);
                }
            }

            wr.Write(", ");

            // same with the body
            bool bodyIsBool = node.Body.Type.IsBool;
            if (bodyIsBool)
            {
                wr.Write("(");
                Linearise(node.Body, arg);
                wr.Write(")");
            }
            else
            {
                Linearise(node.Body, arg);
            }

            // close the opening let brackets
            foreach (VCExprLetBinding b in node)
            {
                wr.Write(")");
            }

            return true;
        }
        finally
        {
            namer.PopScope();
        }
    }
}

public class TPTPOpLineariser : IVCExprOpVisitor<bool, TPTPLineariserOptions>
{

    private readonly ILogger<TPTPLineariser> log = Factory.loggerFactory.CreateLogger<TPTPLineariser>();
    private readonly TPTPLineariser lin;
    private readonly TextWriter wr;
    private readonly UniqueNamer namer;
    private readonly TPTPContext context;
    private readonly TPTPTypeUtils typing;

    public TPTPOpLineariser(TPTPLineariser lin, TextWriter wr, UniqueNamer namer, TPTPContext context, TPTPTypeUtils typing)
    {
        this.lin = lin;
        this.wr = wr;
        this.namer = namer;
        this.context = context;
        this.typing = typing;
    }

    // TODO: Some operators, like addition, can be n-ary in Z3, and not in TPTP (and as far as I know also 
    // not in plain SMT-LIB).
    //
    // It seems, however, that in practice, the operators that may be n-ary in Z3 usually 
    // have the expected arity when linearizing Boogie expressions (e.g. addition seems to only be
    // binary in practice).
    //
    // If that were not the case, some of the functions would need to use auxiliary variables,
    // in particular, the functions whose domain and range are not the same
    // (equality, less than, greater than or equal to, etc.).
    //
    // However, when the functions are associative and the domain and range are the same,
    // one can just nest them.
    // It just so happens to be the case that such 'nice' cases seem to be the only
    // exception to this rule, where they do not have the expected arity (e.g. ProverContext:247)
    //
    // So, where it is possible to nest the operators, we nest the operators. Where it would need
    // auxiliary variables, we just assume that they have the usual arity and do not do any
    // extra work.
    //
    // Seems to work just fine for now. If it breaks in the future, one could make a mutating visitor 
    // that replaces multiple arity functions with just binary functions, and collects some extra axioms 
    // about the new auxiliary variables if needed.

    public bool VisitAddOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // Addition can have multiple arguments in Z3, but not in TPTP.
        // Therefore it is implemented right-associative to make it also compatible with multiple arguments 
        // (left-associative would be fine as well).

        Contract.Requires(node.Arity > 1);

        // nest each argument
        int i;
        for (i = 0; i < node.Length - 2; i++)
        {
            wr.Write("$sum(");
            lin.Linearise(node[i], arg);
            wr.Write(", ");
        }

        // last two args are no longer nested
        wr.Write("$sum(");
        lin.Linearise(node[i], arg);
        wr.Write(",");
        lin.Linearise(node[i + 1], arg);
        wr.Write(")");

        // close each bracket
        for (i = 0; i < node.Arity - 2; i++)
        {
            wr.Write(")");
        }

        return true;
    }

    public bool VisitAndOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        bool first = true;
        foreach (VCExpr expr in node.Arguments)
        {
            if (!first) { wr.Write(" & "); }
            first = false;

            wr.Write("(");
            lin.Linearise(expr, arg);
            wr.Write(")");
        }

        return true;
    }

    private bool WriteFunctionApplication(
        string name,
        IEnumerable<Type> typeArguments,
        IEnumerable<VCExpr> arguments,
        TPTPLineariserOptions arg
    )
    { 
        // if it has no args, write it as a constant instead
        if (arguments.Count() == 0 && typeArguments.Count() == 0)
        {
            wr.Write("{0}", name);
        }
        // else write it as a function application
        else
        {
            bool first = true;
            wr.Write("{0}(", name);

            // first write the polymorphic in types, if there are any
            foreach (Type t in typeArguments)
            {
                if (!first)
                {
                    wr.Write(", ");
                }
                first = false;
                
                wr.Write("{0}", typing.TypeToString(t));
            }

            foreach (VCExpr expr in arguments)
            {
                if (!first)
                {
                    wr.Write(", ");
                }
                first = false;
                lin.Linearise(expr, arg);
            }
            wr.Write(")");
        }

        return true;
    }

    public bool VisitBoogieFunctionOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        VCExprBoogieFunctionOp op = (VCExprBoogieFunctionOp)node.Op;
        Function f = op.Func;
        Contract.Assert(op != null);

        string printedName;
        
        // if this is a builtin function, just pass it through
        var builtin = TPTPTypeUtils.ExtractBuiltin(op.Func);
        if (builtin != null)
        {
            printedName = builtin;
        }
        else
        { 
            // else this is a user defined function
            printedName = namer.GetQuotedName(f, f.Name);
        }

        return WriteFunctionApplication(
            printedName,
            node.TypeArguments,
            node.Arguments,
            arg
        );
    }

    // Bitvectors are not supported in TPTP.
    // One can manually axiomatize the hardware circuits, but that makes the verification
    // infeasible in practice.
    public bool VisitBvConcatOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Bitvector concatenation is not supported in TPTP");
    }

    public bool VisitBvExtractOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Bivector extraction not supported in TPTP");
    }

    public bool VisitBvOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Bivector construction not supported in TPTP");
    }

    public bool VisitCustomOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // custom boogie function
        VCExprCustomOp op = (VCExprCustomOp)node.Op;
        var functionName = op.Name;

        bool first = true;
        wr.Write("{0}(", functionName);
        foreach (VCExpr expr in node.Arguments)
        {
            if (!first) { wr.Write(", "); }
            first = false;
            lin.Linearise(expr, arg);
        }
        wr.Write(")");
        return true;
    }

    public bool VisitDistinctOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // distinct is good since it takes "one or more constants" so no need for
        // nesting :D
        // although one has to be careful -> SMTLib distinct can also take multiple types, 
        // while TPTP distinct only takes arguments of the same type!
        // This is handled in TPTPContext.
        wr.Write("$distinct(");

        bool first = true;
        foreach (VCExpr expr in node.Arguments)
        {
            if (!first) { wr.Write(", "); }
            first = false;

            lin.Linearise(expr, arg);
        }
        wr.Write(")");
        return true;
    }

    public bool VisitDivOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // Integer division.
        // Division can have multiple arguments in SMTLib, but not in TPTP
        // left-associative
        Contract.Requires(node.Arity > 1);

        // wrap in a quotient
        int i;
        for (i = 2; i < node.Length; i++)
        {
            // truncating quotient, i.e. integer division
            wr.Write("$quotient_t(");
        }

        // write first two arguments
        wr.Write("$quotient_t(");
        lin.Linearise(node[0], arg);
        wr.Write(",");
        lin.Linearise(node[1], arg);
        wr.Write(")");

        // nest all the other arguments
        for (i = 2; i < node.Length; i++)
        {
            wr.Write(", ");
            lin.Linearise(node[i], arg);
            wr.Write(")");
        }

        return true;
    }

    public bool VisitEqOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // Equality can have multiple arguments in SMTLib, but not in TPTP.
        //
        // A simplifying assumption is made here that equalities are always binary.
        //
        // If this assumption breaks, one can create an auxiliary variable for each
        // argument, impose the equality between the auxiliary variable and the argument,
        // and then impose equalities between the auxiliary variables themselves.
        // 
        // e.g. (= a b c) 
        // becomes
        // axiom aux0 = a
        // axiom aux1 = b
        // axiom aux2 = c
        // aux0 = aux1 & aux1 = aux2
        // ... due to transitivity this is enough.
        //
        // This is necessary to prevent linearizing the individual equality arguments, twice
        // (note that a,b,c may be very large expressions)

        if (node.Arity != 2)
        {
            throw new InvalidOperationException("Equality not binary");
        }

        // this linearization method is also used for logical equivalence
        var isBool = node[0].Type.IsBool;
        if (isBool)
        {
            wr.Write("(");
            lin.Linearise(node[0], arg);
            wr.Write(") <=> (");
            lin.Linearise(node[1], arg);
            wr.Write(")");
        }
        else
        { 
            wr.Write("(");
            lin.Linearise(node[0], arg);
            wr.Write(" = ");
            lin.Linearise(node[1], arg);
            wr.Write(")");
        }


        return true;
    }

    public bool VisitFieldAccessOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        var op = (VCExprFieldAccessOp)node.Op;
        var constructor = op.DatatypeTypeCtorDecl.Constructors[op.ConstructorIndex];
        Variable v = constructor.InParams[op.FieldIndex];
        var name = namer.GetQuotedName(v, v.Name);

        bool first = true;
        wr.Write("{0}(", name);
        foreach (VCExpr expr in node.Arguments)
        {
            if (!first) { wr.Write(", "); }
            first = false;

            lin.Linearise(expr, arg);
        }
        wr.Write(")");

        return true;
    }

    // Floating point operators unfortunately unsupported in TPTP...
    // Manual axiomatization is, as in the bitvector case, possible in principle,
    // but infeasible in practice.
    public bool VisitFloatAddOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float addition is not supported in TPTP");
    }

    public bool VisitFloatDivOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float division is not supported in TPTP");
    }

    public bool VisitFloatEqOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float equality is not supported in TPTP");
    }

    public bool VisitFloatGeqOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float >= is not supported in TPTP");
    }

    public bool VisitFloatGtOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float > is not supported in TPTP");
    }

    public bool VisitFloatLeqOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float <= is not supported in TPTP");
    }

    public bool VisitFloatLtOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float < is not supported in TPTP");
    }

    public bool VisitFloatMulOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float multiplication is not supported in TPTP");
    }

    public bool VisitFloatNeqOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float inequality is not supported in TPTP");
    }

    public bool VisitFloatSubOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotSupportedException("Float subtraction is not supported in TPTP");
    }

    // $less | $lesseq | $greater | $greatereq
    private bool VisitInequalityOp(VCExprNAry node, TPTPLineariserOptions arg, string funcName)
    {
        // <=, <, >, >= can all be n-ary in Z3
        // Similarly to the equality case, one can encode the inequalities with n auxiliary 
        // variables to aviod linearising the terms twice.

        if (node.Arity != 2)
        {
            throw new InvalidOperationException(funcName + " operator not binary");
        }

        wr.Write("{0}(", funcName);
        lin.Linearise(node[0], arg);
        wr.Write(", ");
        lin.Linearise(node[1], arg);
        wr.Write(")");

        return true;
    }

    public bool VisitGeOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        return VisitInequalityOp(node, arg, "$greatereq");
    }

    public bool VisitGtOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        return VisitInequalityOp(node, arg, "$greater");
    }

    public bool VisitIfThenElseOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // $ite(<thf_logic_formula>,<thf_logic_formula>, <thf_logic_formula>)
        // can only be ternary, both in smtlib and in tptp
        if (node.Length != 3)
        {
            throw new InvalidOperationException("if-then-else node not ternary");
        }

        // condition
        wr.Write("$ite(");
        lin.Linearise(node[0], arg);
        wr.Write(", ");

        // then
        lin.Linearise(node[1], arg);
        wr.Write(", ");

        // else
        lin.Linearise(node[2], arg);
        wr.Write(")");

        return true;
    }

    public bool VisitImpliesOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // => logical connective
        // note that <= is also a logical connective in TPTP
        // can also be n-ary
        // the implication is right-associative in both SMTLib and TPTP, so once can actually
        // linearize the terms without the parentheses
        // but, why not be explicit about the intentions

        // nest each argument
        int i;
        for (i = 0; i < node.Length - 2; i++)
        {
            // boolean operators can be surrounded by parentheses
            // function operators/terms not
            wr.Write("((");
            lin.Linearise(node[i], arg);
            wr.Write(") => ");
        }

        // last two args are no longer nested
        wr.Write("((");
        lin.Linearise(node[i], arg);
        wr.Write(") => (");
        lin.Linearise(node[i + 1], arg);
        wr.Write("))");

        // close each bracket
        for (i = 0; i < node.Arity - 2; i++)
        {
            wr.Write(")");
        }

        return true;
    }

    public bool VisitIsConstructorOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        var op = (VCExprIsConstructorOp)node.Op;
        var constructor = op.DatatypeTypeCtorDecl.Constructors[op.ConstructorIndex];
        var constructorName = namer.GetName(constructor, constructor.Name);
        // the regular lineariser does not request the name of the is_constructor
        // function, it just directly add quotes if needed. This is semantically
        // different, as it does not rename the constructor via the namer!
        var funcName = TPTPNameUtils.AddQuotes($"is_{constructorName}");

        bool first = true;
        wr.Write("{0}(", funcName);
        foreach (VCExpr expr in node.Arguments)
        {
            if (!first) { wr.Write(", "); }
            first = false;

            lin.Linearise(expr, arg);
        }
        wr.Write(")");
        return true;
    }

    public bool VisitLeOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        return VisitInequalityOp(node, arg, "$lesseq");
    }

    public bool VisitLtOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        return VisitInequalityOp(node, arg, "$less");
    }

    public bool VisitModOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // $remainder_t (truncating)
        // binary in smtlib as well as TPTP
        // seems to work only for binary mod operations, e.g.
        // 
        // (assert (= (mod 5.2 0.3) 4))
        // (check-sat)
        // 
        // is sat for some reason. Also with asserting = 1, 2, 3...???

        if (node.Length != 2)
        {
            throw new InvalidOperationException("mod is not binary");
        }

        wr.Write("$remainder_t(");
        lin.Linearise(node[0], arg);
        wr.Write(", ");
        lin.Linearise(node[1], arg);
        wr.Write(")");

        return true;
    }

    public bool VisitMulOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // smtlib *
        // real x real -> real
        // int x int -> int
        // $product
        // again, z3 can be n-ary while tptp is strictly binary
        // do right-associativity

        int i;
        for (i = 0; i < node.Length - 2; i++)
        {
            wr.Write("$product(");
            lin.Linearise(node[i], arg);
            wr.Write(", ");
        }

        // last two args are no longer nested
        wr.Write("$product(");
        lin.Linearise(node[i], arg);
        wr.Write(", ");
        lin.Linearise(node[i + 1], arg);
        wr.Write(")");

        // close each bracket
        for (i = 0; i < node.Arity - 2; i++)
        {
            wr.Write(")");
        }

        return true;
    }

    public bool VisitNeqOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // Inequality != can be n-ary in z3, and is strictly binary in TPTP.
        // Can be equivalently encoded via auxiliary variables, similarly to the equality case
        //
        // e.g. (a != b != c)
        // becomes
        // axiom aux0 = a
        // axiom aux1 = b
        // axiom aux2 = c
        // aux0 != aux1 | aux1 != aux2
        // ... due to transitivity this is enough.

        if (node.Length != 2)
        {
            throw new InvalidOperationException("Inequality operator not binary");
        }

        // TPTP has native support for inequalities
        var isBool = node[0].Type.IsBool;
        if (isBool)
        { 
            wr.Write("((");
            lin.Linearise(node[0], arg);
            wr.Write(") <~> (");
            lin.Linearise(node[1], arg);
            wr.Write("))");
        }
        else
        { 
            wr.Write("(");
            lin.Linearise(node[0], arg);
            wr.Write(" != ");
            lin.Linearise(node[1], arg);
            wr.Write(")");
        }
        return true;
    }

    public bool VisitNotOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // always unary
        if (node.Length != 1)
        {
            throw new InvalidOperationException("'not' operator is not unary");
        }

        wr.Write("~(");
        lin.Linearise(node[0], arg);
        wr.Write(")");

        return true;
    }

    public bool VisitOrOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        bool first = true;
        foreach (VCExpr expr in node.Arguments)
        {
            if (!first) { wr.Write(" | "); }
            first = false;

            wr.Write("(");
            lin.Linearise(expr, arg);
            wr.Write(")");
        }

        return true;
    }

    public bool VisitPowOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // The power function just seems to be declared, not axiomatized, and also
        // unused...?
        throw new NotImplementedException("power not implemented");
        
        // in case its needed, one can for example try to define the power recursively:
        // 0. define pow: int x int -> int and/or real x real -> real (and/or rat x rat -> rat??)
        // 1. axiom forall b: pow(b, 0) = 1
        // 2. axiom forall b, e: pow(b, e) = pow(b, e-1)*b
        
        // if (node.Arity != 2) {
        //     throw new Exception("power op not binary");
        // }
        // 
        // wr.Write("real_pow(");
        // lin.Linearise(node[0], arg);
        // wr.Write(", ");
        // lin.Linearise(node[1], arg);
        // wr.Write(")");
        // return true;
    }

    public bool VisitRealDivOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // just $quotient, unbounded real division
        // also n-ary in smtlib 
        // left-associative
        Contract.Requires(node.Arity > 1);

        // wrap in a quotient
        int i;
        for (i = 2; i < node.Length; i++)
        {
            // 'normal' quotient, i.e. real division
            wr.Write("$quotient(");
        }

        // write first two arguments
        wr.Write("$quotient(");
        lin.Linearise(node[0], arg);
        wr.Write(", ");
        lin.Linearise(node[1], arg);
        wr.Write(")");

        // nest all the other arguments
        for (i = 2; i < node.Length; i++)
        {
            wr.Write(", ");
            lin.Linearise(node[i], arg);
            wr.Write(")");
        }

        return true;
    }

    public bool VisitSelectOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        if (context.ProverOptions.UseArrayAxioms)
        {
            // the first argument is the array, which has all the typing information
            var map = node[0].Type.AsMap;
            var selectOpName = ArrayHelper.SelectFunctionName(map.MapArity);
            selectOpName = TPTPNameUtils.GetQuotedName(namer, selectOpName, selectOpName);

            return WriteFunctionApplication(
                selectOpName,
                [..map.Arguments, map.Result],
                node.Arguments,
                arg
            );
        }
        else
        { 
            // select is left-associative
            int i;
            for (i = 2; i < node.Length; i++)
            {
                wr.Write("$select(");
            }

            // write first two arguments
            wr.Write("$select(");
            lin.Linearise(node[0], arg);
            wr.Write(", ");
            lin.Linearise(node[1], arg);
            wr.Write(")");

            // nest all the other arguments
            for (i = 2; i < node.Length; i++)
            {
                wr.Write(", ");
                lin.Linearise(node[i], arg);
                wr.Write(")");
            }
        }

        return true;
    }

    public bool VisitStoreOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // the first argument is the array, which has all the typing information
        var map = node[0].Type.AsMap;

        if (context.ProverOptions.UseArrayAxioms)
        {
            var storeOpName = ArrayHelper.StoreFunctionName(map.MapArity);
            storeOpName = TPTPNameUtils.GetQuotedName(namer, storeOpName, storeOpName);
            
            return WriteFunctionApplication(
                storeOpName,
                [..map.Arguments, map.Result],
                node.Arguments,
                arg
            );
        }
        else
        {
            bool first = true;

            // reconstruct all array levels
            var arity = map.MapArity;
            int depth;
            for (depth = 0; depth < arity; depth++)
            {
                if (!first) { wr.Write(", "); }
                first = false;

                wr.Write("$store(");

                // select the array from the current depth
                for (int i = 0; i < depth; i++)
                {
                    wr.Write("$select(");
                }

                lin.Linearise(node[0], arg);
                
                for (int i = 0; i < depth; i++)
                {
                    wr.Write(", ");
                    lin.Linearise(node[i + 1], arg);
                    wr.Write(")");
                }

                // the current index
                wr.Write(", ");
                lin.Linearise(node[depth + 1], arg);
            }

            // finally, write the value
            wr.Write(", ");
            lin.Linearise(node[arity + 1], arg);

            // then close all the brackets
            for (depth = 0; depth < arity; depth++)
            {
                wr.Write(")");
            }
        }

        return true;
    }

    public bool VisitSubOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // '$uminus' is the 'unary minus'
        // the function to use is $difference, which is the difference (for real numbers)
        // in smtlib, the - seems to be both for integers and reals
        // smtlib is n-ary, $difference is binary
        // also left-associative
        Contract.Requires(node.Arity > 1);

        int i;
        for (i = 2; i < node.Length; i++)
        {
            wr.Write("$difference(");
        }

        // write first two arguments
        wr.Write("$difference(");
        lin.Linearise(node[0], arg);
        wr.Write(", ");
        lin.Linearise(node[1], arg);
        wr.Write(")");

        // nest all the other arguments
        for (i = 2; i < node.Length; i++)
        {
            wr.Write(", ");
            lin.Linearise(node[i], arg);
            wr.Write(")");
        }

        return true;
    }

    // Similarly to the power op, subtyping ops are hard-coded into the SMTLibProcessTheoremProver 
    // (variable 'backgroundPredicates'), and it also seems like they are never used (VisitSubtypeOp/VisitSubtype3Op 
    // is to my knowledge not called anywhere within Boogies code)

    public bool VisitSubtype3Op(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotImplementedException("Subtype3 not implemented");
        
        // if (node.Arity != 3)
        // {
        //     throw new Exception("UOrdering3 not ternary");
        // }
        // wr.Write("uordering3(");
        // lin.Linearise(node[0], arg);
        // wr.Write(", ");
        // lin.Linearise(node[1], arg);
        // wr.Write(", ");
        // lin.Linearise(node[2], arg);
        // wr.Write(")");
        // return true;
    }

    public bool VisitSubtypeOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        throw new NotImplementedException("Subtype2 not implemented");

        // if (node.Arity != 2)
        // {
        //     throw new Exception("UOrdering2 not binary");
        // }
        // wr.Write("uordering2(");
        // lin.Linearise(node[0], arg);
        // wr.Write(", ");
        // lin.Linearise(node[1], arg);
        // wr.Write(")");
        // return true;
    }

    public bool VisitToIntOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // $to_int
        // identical to smtlib, unary
        if (node.Length != 1)
        {
            throw new InvalidOperationException("to_int not unary");
        }

        wr.Write("$to_int(");
        lin.Linearise(node[0], arg);
        wr.Write(")");

        return true;
    }

    public bool VisitToRealOp(VCExprNAry node, TPTPLineariserOptions arg)
    {
        // $to_real
        // identical to smtlib, unary        
        if (node.Length != 1)
        {
            throw new InvalidOperationException("to_real not unary");
        }

        wr.Write("$to_real(");
        lin.Linearise(node[0], arg);
        wr.Write(")");

        return true;
    }
}