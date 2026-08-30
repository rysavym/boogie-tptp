using Microsoft.Boogie.VCExprAST;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;
public class ArrayHelper
{

    public static readonly ILogger log = Factory.loggerFactory.CreateLogger<ArrayHelper>();

    public static string ArrayTypeName(int mapArity)
    {
        return "arr" + mapArity;
    }

    public static string SelectFunctionName(int mapArity)
    {
        return "arr" + mapArity + "_select";
    }

    public static string StoreFunctionName(int mapArity)
    {
        return "arr" + mapArity + "_store";
    }

    // note that this creates a map type where the index and result polymorphic types
    // are occurring as free variables -> they are expected to be bound somewhere when
    // they are used... (e.g. with row1, all of the parameters should be bound to the
    // generics)
    public static MapType GenericMapType(int arity)
    { 
        List<TypeVariable> allTypes = [];
        List<Type> indexTypes = [];
        for (int i = 0; i < arity; i++)
        {
            var tv = new TypeVariable(Token.NoToken, "T" + i);
            indexTypes.Add(tv);
            allTypes.Add(tv);
        }
        TypeVariable resultType = new TypeVariable(Token.NoToken, "R");
        allTypes.Add(resultType);

        return new MapType(
            Token.NoToken,
            [], // no bound type parameters, all type variables occur free.
            indexTypes,
            resultType
        );
    }

    public static VCExpr ReadOverWrite1(int arity)
    {
        MapType map = GenericMapType(arity);

        VCExpressionGenerator gen = new VCExpressionGenerator();
        string mapName = map.ToString();

        // For all indices and a return value
        List<VCExprVar> indices1 = [];
        List<VCExprVar> indices2 = [];
        List<VCExprVar> total = [];
        VCExpr? indicesEqual = null;
        var a = gen.Variable("A", map);
        total.Add(a);
        for (int i = 0; i < map.Arguments.Count; i++)
        {
            var arg = map.Arguments[i];
            var x = gen.Variable("X" + i, arg);
            var y = gen.Variable("Y" + i, arg);
            indices1.Add(x);
            indices2.Add(y);
            total.Add(x);
            total.Add(y);
            indicesEqual = indicesEqual == null ? gen.Eq(x, y) : gen.And(gen.Eq(x, y), indicesEqual);
        }
        var v = gen.Variable("V", map.Result);
        total.Add(v);

        return gen.Forall(
            map.FreeVariables, // the free variables of the map are bound by the quantifier
            total,
            [],
            new VCQuantifierInfo("row1_" + mapName, -1),
            gen.Implies(
                indicesEqual,
                gen.Eq(
                    gen.Select(
                        [
                            gen.Store(
                                [a, ..indices1, v] // here the array appears with the type paramters as free variables
                            ),
                            ..indices2
                        ]
                    ),
                    v
                )
            )
        );
    }

    public static VCExpr ReadOverWrite2(int arity)
    {
        MapType map = GenericMapType(arity);

        VCExpressionGenerator gen = new VCExpressionGenerator();
        string mapName = map.ToString();

        // For all indices and a return value
        List<VCExprVar> indices1 = [];
        List<VCExprVar> indices2 = [];
        List<VCExprVar> total = [];
        VCExpr? someIndexNotEqual = null;
        var a = gen.Variable("A", map);
        total.Add(a);
        for (int i = 0; i < map.Arguments.Count; i++)
        {
            var arg = map.Arguments[i];
            var x = gen.Variable("X" + i, arg);
            var y = gen.Variable("Y" + i, arg);
            indices1.Add(x);
            indices2.Add(y);
            total.Add(x);
            total.Add(y);
            someIndexNotEqual = someIndexNotEqual == null ? gen.Neq(x, y) : gen.Or(gen.Neq(x, y), someIndexNotEqual);
        }
        var v = gen.Variable("V", map.Result);
        total.Add(v);

        return gen.Forall(
            map.FreeVariables, // the free variables of the map are bound by the quantifier
            total,
            [],
            new VCQuantifierInfo("row2_" + mapName, -1),
            gen.Implies(
                someIndexNotEqual,
                gen.Eq(
                    gen.Select(
                        [
                            gen.Store(
                                [a, ..indices1, v] // here the map occurs only with free variables
                            ),
                            ..indices2
                        ]
                    ),
                    gen.Select(
                        [a, ..indices2]
                    )
                )
            )
        );
    }

    // none of the arrays in row1, row2 have type arguments, since the variables occur free,
    // in particular, there is no actual type, just the generics

}