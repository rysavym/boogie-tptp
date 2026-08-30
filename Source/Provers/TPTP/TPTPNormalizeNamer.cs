using Microsoft.Boogie.VCExprAST;

namespace Microsoft.Boogie.TPTP;

public class TPTPNormalizeNamer : TPTPScopedNamer
{

    public TPTPNormalizeNamer() {
        Spacer = "__";
    }

    public TPTPNormalizeNamer(ScopedNamer namer) : base(namer) {
        Spacer = "__";
    }

    public override TPTPNormalizeNamer Clone()
    {
        return new TPTPNormalizeNamer(this);
    }

    public override string GetModifiedLocalName(string inherentName)
    {
        return "X";
    }

    protected override string GetModifiedName(string uniqueInherentName)
    {
        return "x";
    }
}