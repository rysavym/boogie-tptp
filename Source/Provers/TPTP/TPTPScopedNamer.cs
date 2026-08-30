using Microsoft.Boogie.VCExprAST;

namespace Microsoft.Boogie.TPTP;

public abstract class TPTPScopedNamer : ScopedNamer
{
    protected TPTPScopedNamer() : base()
    {

    }

    protected TPTPScopedNamer(ScopedNamer namer) : base(namer)
    {

    }

    // I do not know what is the benefit of letting Boogie determine some names and not others, except for some debugging (which can be done way better with the KeepOriginalNamer).
    // In any case, the field is not accessible in ScopedNamer, so we have to duplicate it here.
    // One possibility to fix this code duplication would be to make the 'boogieDeterminedNames' field protected
    // in ScopedNamer, however that would be changing Boogie's code.
    // Or - just rename everything! do not let the namer keep any name!
    private static ISet<string> boogieDeterminedNames = new HashSet<string>() { VCExpressionGenerator.ControlFlowName, "type" };

    public abstract string GetModifiedLocalName(string inherentName);

    public override string GetLocalName(Object thing, string inherentName)
    {
        if (!boogieDeterminedNames.Contains(inherentName))
        {
            inherentName = GetModifiedLocalName(inherentName);
        }

        string res = NextFreeName(thing, inherentName);
        LocalNames[^1][thing] = res;
        return res;
    }

    public bool IsLocal(object thing)
    {
        for (int i = LocalNames.Count - 1; i >= 0; --i)
        {
            if (LocalNames[i].ContainsKey(thing))
            {
                return true;
            }
        }
        return false;
    }
}