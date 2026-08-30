using System.Diagnostics.Contracts;
using Microsoft.Boogie.TypeErasure;
using Microsoft.Boogie.VCExprAST;

namespace Microsoft.Boogie.TPTP;

public class TypeAxiomBuilderArgumentsB : TypeAxiomBuilderArguments
{
    public TypeAxiomBuilderArgumentsB(VCExpressionGenerator gen, CoreOptions options) : base(gen, options)
    {
    }

    protected override VCExpr GenReverseCastAxiom(Function castToU, Function castFromU)
    {
        Contract.Ensures(Contract.Result<VCExpr>() != null);
        VCExpr eq = GenReverseCastEq(castToU, castFromU, out var var, out var triggers);

        // Avoid hierarchy collapse without triggers
        // forall u: U (exists x: int (int_2_U(x) == u)) ==> int_2_U(U_2_int(u)) == u
        VCExprVar x = Gen.Variable("X", castFromU.OutParams[0].TypedIdent.Type);
        VCExpr matrix = Gen.Implies(
            Gen.Exists(
                [x],
                [],
                Gen.Eq(Gen.Function(castToU, x), var)
            ),
            eq
        );

        return Gen.Forall(
            Microsoft.Boogie.TypeErasure.HelperFuns.ToList(var), 
            [], 
            "cast:" + castFromU.Name, 
            1, 
            matrix
        );
    }
}