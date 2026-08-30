using Microsoft.Boogie.VCExprAST;

namespace Microsoft.Boogie.TPTP;

// Collects all type declarations, axioms, etc. needed for the VC itself.
public class TheoryCollector : BoundVarTraversingVCExprVisitor<bool, TheoryContext>
{
    protected override bool StandardResult(VCExpr node, TheoryContext arg)
    {
        return true;
    }

    private void RegisterStore(VCExprNAry node, TheoryContext arg)
    { 
        var map = node[0].Type.AsMap;
        arg.AddTypeDeclaration(map);
    }

    private void RegisterSelect(VCExprNAry node, TheoryContext arg)
    {
        var map = node[0].Type.AsMap;
        arg.AddTypeDeclaration(map);
    }

    private void RegisterBoogieFunction(VCExprBoogieFunctionOp op, VCExprNAry args, TheoryContext arg)
    {
        var f = op.Func;
        arg.AddFunctionDeclaration(f);
    }

    public override bool Visit(VCExprNAry node, TheoryContext tctx)
    {
        var op = node.Op;
        tctx.AddTypeDeclaration(node.Type);

        if (op is VCExprStoreOp)
        {
            RegisterStore(node, tctx);
        }
        else if (op is VCExprSelectOp)
        {
            RegisterSelect(node, tctx);
        } 
        else if (op is VCExprBoogieFunctionOp boogieFuncOp)
        {
            RegisterBoogieFunction(boogieFuncOp, node, tctx);
        }

        return base.Visit(node, tctx);
    }

    public override bool Visit(VCExprQuantifier node, TheoryContext ctx)
    {
        // mark the types of all bound vars for declaration
        foreach (VCExprVar v in node.BoundVars)
        {
            ctx.AddTypeDeclaration(v.Type);
        }

        return base.Visit(node, ctx);
    }

    public override bool Visit(VCExprVar node, TheoryContext ctx)
    {
        // if the variable is not bound, mark it for declaration
        if (!BoundTermVars.ContainsKey(node))
        {
            ctx.AddVariableDeclaration(node);
        }

        // no nesting
        return base.Visit(node, ctx);
    }

    // Collect all axioms, variable declarations, types etc. that are in a VCExpr
    // Types are collected as-is and added directly to a theory context, i.e. no type
    // erasure is taking place here.
    public static bool Collect(VCExpr expr, TheoryContext ctx) 
    {
        TheoryCollector collector = new TheoryCollector();
        return expr.Accept<bool, TheoryContext>(collector, ctx);
    }

}