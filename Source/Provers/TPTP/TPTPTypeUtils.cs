using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.Boogie.TPTP;

public class TPTPTypeUtils
{
    private readonly ILogger<TPTPTypeUtils> log = Factory.loggerFactory.CreateLogger<TPTPTypeUtils>();

    public static string ExtractBuiltin(Function f)
    {
        return f.FindStringAttribute("bvbuiltin") ?? f.FindStringAttribute("builtin");
    }

    private readonly TPTPOptions options;
    private readonly TPTPScopedNamer namer;

    public TPTPTypeUtils(TPTPOptions options, TPTPScopedNamer namer)
    {
        this.options = options;
        this.namer = namer;
    }

    public string TypeToString(Type t)
    {
        if (t.IsBool)
        {
            return "$o";
        }
        else if (t.IsInt)
        {
            return "$int";
        }
        else if (t.IsReal)
        {
            return "$real";
        }
        else if (t.IsFloat)
        {
            throw new NotSupportedException("Floating point types are not supported in TPTP");
        }
        else if (t.IsBv)
        {
            throw new NotSupportedException("Bitvector types are not supported in TPTP");
        }
        else if (t.IsRMode)
        {
            throw new NotSupportedException("Rounding mode types are not supported in TPTP");
        }
        else if (t.IsString)
        {
            throw new NotSupportedException("String types are not supported in TPTP");
        }
        else if (t.IsRegEx)
        {
            throw new NotSupportedException("Regular expression types are not supported in TPTP");
        }
        else if (t.IsSeq)
        {
            throw new NotSupportedException("Sequence types are not supported in TPTP");
        }
        else if (t.IsVariable)
        {
            var tv = t.AsVariable;
            return TPTPNameUtils.GetTypeName(namer, tv);
        }
        else if (t.IsMap)
        {
            var map = t.AsMap;
            if (map.Arguments.Count == 0)
            { 
                return TypeToString(map.Result);
            }
            else if (options.UseArrayAxioms)
            {
                string result = ArrayHelper.ArrayTypeName(map.MapArity);

                // no matter whether the argument/result is free (e.g. bound somewhere else)
                // or a typeArgument (e.g. bound in the current context), it should always
                // just be linearized to the type itself.
                List<Type> types = [..map.Arguments, map.Result];
                result += "(" + string.Join(", ", types.Select(p => TypeToString(p))) + ")";
                return result;
            }
            else
            { 
                StringWriter wr = new StringWriter();
                foreach (Type indexType in map.Arguments)
                {
                    wr.Write("$array({0}, ", TypeToString(indexType));
                }

                wr.Write("{0}", TypeToString(map.Result));

                foreach (Type indexType in map.Arguments)
                {
                    wr.Write(")");
                }

                return wr.ToString(); // no quotes needed -> nested args are properly quoted already
            }
        }
        else
        {
            StringBuilder sb = new StringBuilder();
            TypeToStringHelper(t, sb);
            return TPTPNameUtils.AddQuotes("type_" + sb.ToString());
        }
    }

    private void TypeToStringHelper(Type t, StringBuilder sb)
    { 
        if (t is TypeSynonymAnnotation syn)
        {
            TypeToStringHelper(syn.ExpandedType, sb);
        }
        else if (t.IsMap)
        {
            var map = t.AsMap;
            sb.Append("[");
            for (int i = 0; i < map.MapArity; i++)
            {
                if (i != 0)
                {
                    sb.Append(",");
                }
                TypeToStringHelper(map.Arguments[i], sb);

            }
            sb.Append("]");
            TypeToStringHelper(map.Result, sb);
        }
        else if (t.IsBool || t.IsInt || t.IsReal || t.IsFloat || t.IsBv || t.IsRMode || t.IsString)
        {
            sb.Append(TypeToString(t));
        }
        else
        {
            var buffer = new StringWriter();
            using (TokenTextWriter stream = new TokenTextWriter("<buffer>", buffer, false, false, options.LibOptions))
            {
                t.Emit(stream);
            }

            sb.Append(buffer.ToString());
        }
    }
}