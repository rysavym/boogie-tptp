using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class TheoryContext 
{

    private readonly ILogger log = Factory.loggerFactory.CreateLogger<TheoryContext>();

    public TheoryContext() 
    {
        DeclaredVariables = [];
        DeclaredFunctions = [];
        DefinedAxioms = [];
        DeclaredTypes = [];
        this.DefinedFunctions = new Dictionary<Function, (VCExprNAry, VCExpr)>();
        this.DistinctConstants = new Dictionary<Type, List<VCExprVar>>();
        this.DeclaredMapTypeArities = new HashSet<int>();
    }

    public TheoryContext(TheoryContext other)
    { 
        DeclaredVariables = [.. other.DeclaredVariables];
        this.DeclaredFunctions = [.. other.DeclaredFunctions];
        this.DefinedAxioms = [.. other.DefinedAxioms];
        this.DeclaredTypes = [.. other.DeclaredTypes];
        this.DefinedFunctions = new Dictionary<Function, (VCExprNAry, VCExpr)>(other.DefinedFunctions);
        this.DistinctConstants = new Dictionary<Type, List<VCExprVar>>(other.DistinctConstants);
        this.DeclaredMapTypeArities = new HashSet<int>(other.DeclaredMapTypeArities);
    }

    // declarations
    public readonly HashSet<VCExprVar> DeclaredVariables;
    public readonly HashSet<Function> DeclaredFunctions;
    public readonly HashSet<Type> DeclaredTypes;
    public readonly HashSet<int> DeclaredMapTypeArities;

    // definitions
    public readonly HashSet<VCExpr> DefinedAxioms;
    public readonly Dictionary<Function, (VCExprNAry, VCExpr)> DefinedFunctions;

    public readonly Dictionary<Type, List<VCExprVar>> DistinctConstants;

    // add a type declaration to the context, no (pre)processing
    public void AddTypeDeclaration(Type t) 
    {
        if (t.IsMap)
        {
            var map = t.AsMap;
            
            DeclaredMapTypeArities.Add(map.MapArity);

            // also resolve all types of the map
            foreach (Type t2 in map.Arguments)
            {
                AddTypeDeclaration(t2);
            }
            AddTypeDeclaration(map.Result);
        }
        else
        { 
            DeclaredTypes.Add(t);
        }
    }

    // add a function declaration to the context, no (pre)processing
    public void AddFunctionDeclaration(Function f)
    { 
        // declare the in and out types
        f.InParams.ForEach(p => AddTypeDeclaration(p.TypedIdent.Type));
        AddTypeDeclaration(f.OutParams[0].TypedIdent.Type);
        
        // if it is a builtin, it does not need declaration.
        string builtin = TPTPTypeUtils.ExtractBuiltin(f);
        if (builtin != null)
        {
            return;
        }

        DeclaredFunctions.Add(f);
    }

    public void AddFunctionDefinition(Function f, VCExprNAry call, VCExpr body)
    {
        AddFunctionDeclaration(f);
        DefinedFunctions.Add(f, (call, body));
    }

    public void AddVariableDeclaration(VCExprVar variable)
    { 
        DeclaredVariables.Add(variable);
        AddTypeDeclaration(variable.Type);
    }

    // add an axiom definition to the context, no (pre)processing
    public void AddAxiomDefinition(VCExpr axiom)
    {
        // again, no processing 
        DefinedAxioms.Add(axiom);
    }

    public void AddDistinctConstant(Type t, VCExprVar v)
    { 
        // add it to distinct list
        List<VCExprVar> l = DistinctConstants.GetOrCreate(t, () => new List<VCExprVar>());
        l.Add(v);
    }

}