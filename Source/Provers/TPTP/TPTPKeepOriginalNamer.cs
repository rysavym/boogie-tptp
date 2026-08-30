using Microsoft.Boogie.VCExprAST;

namespace Microsoft.Boogie.TPTP;

public class TPTPKeepOriginalNamer : TPTPScopedNamer
{

    public TPTPKeepOriginalNamer() {
        Spacer = "__";
    }

    public TPTPKeepOriginalNamer(ScopedNamer namer) : base(namer) {
        Spacer = "__";
    }

    public override TPTPKeepOriginalNamer Clone()
    {
        return new TPTPKeepOriginalNamer(this);
    }

    public override string GetModifiedLocalName(string inherentName)
    {
        // can not safely return the inherent name for quantifiers, 
        // as bound variables can not contain quoted names.
        return "X";
    }

    protected override string GetModifiedName(string uniqueInherentName)
    {
        return uniqueInherentName;
    }
}