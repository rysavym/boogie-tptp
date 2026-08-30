using System.Text.RegularExpressions;
using Microsoft.Boogie.VCExprAST;

namespace Microsoft.Boogie.TPTP;

// quantified variables:
// <variable>   ::= <upper_word> 
// <upper_word> ::- <upper_alpha><alpha_numeric>*
// i.e. the following conditions must be met:
//    1. must start with an uppercase alpha character (A-Z)
//    2. after the uppercase alpha character, it must only contain alphanumeric characters.
// i.e. also no quoting!
//
// variables inside a type declaration:
// <tff_atom_typing>      ::= <untyped_atom> : <tff_top_level_type> | (<tff_atom_typing>)
// <untyped_atom>         ::= <constant> | <system_constant>
// <constant>             ::= <functor>
// <functor>              ::= <atomic_word>
// <atomic_word>          ::= <lower_word> | <single_quoted> | <back_quoted>
// i.e., the following conditions must be met:
//    1. must start with a lowercase
//    2. after the first lowercase, they can only contain [a-z] [A-Z] [0-9] or underscore _
//    3. OR they can be any printable ASCII character except for ' and \, if they are single-quoted (even in type declarations!)
//        literal ' or \ must be escaped, i.e. \' or \\
// The back quoting does not work for some reason in Vampire ('Bad character' error)
//
// Names of polymorphic types follow the same convention as quantified variables.
public static class TPTPNameUtils {

    private static string FilterReserved(string name) 
    {
        // Technically, no name is reserved in TPTP since each name
        // can be single quoted and escaped (even '$int', ...)
        return name;
    }

    // variable declarations
    private static bool NeedsQuotes(string name) {
        // only for type declarations and variables
        string allowed = @"^[a-z][a-zA-Z0-9_]*$";
        return !Regex.IsMatch(name, allowed);
    }

    private static bool QuantifierFriendly(string name)
    { 
        string allowed = @"^[A-Z][a-zA-Z0-9_]*$";
        return Regex.IsMatch(name, allowed);
    }

    public static string AddQuotes(string name) {
        // Add quotes if needed
        if (NeedsQuotes(name)) {
            // escape single quotes and backlashes
            name = name.Replace("'", "\\'");
            name = name.Replace("\\", "\\\\");

            return '\'' + name + '\'';
        }

        return name;
    }


    // get a name such that it may appear in a quantifier
    public static string GetLocalName(this UniqueNamer namer, object thingie, string inherentName) 
    {
      var name = namer.GetLocalName(thingie, FilterReserved(inherentName));

      // this is just a sanity check, this should never happen with the TPTPNormalizeNamer,
      // or any other namer that extends TPTPScopedNamer + GetModifiedLocalName returns a
      // quantifier friendly (i.e. uppercase alphanumeric) string.
      if (!QuantifierFriendly(name))
      {
          throw new InvalidOperationException("Quoted name in a quantifier: " + name);
      }
      
      return name;
    }

    // get a name such that it may appear in a typing declaration, or an already existing quantified name
    public static string GetQuotedName(this UniqueNamer namer, object thingie, string inherentName)
    {
        var name = namer.GetName(thingie, FilterReserved(inherentName));

        // if it is a local name, do not add quotes
        var tptpNamer = (namer as TPTPScopedNamer);
        if (tptpNamer != null && tptpNamer.IsLocal(thingie))
        {
            if (!QuantifierFriendly(name))
            {
                throw new InvalidOperationException("Quoted name of a bound variable: " + name);
            }
            return name;
        }

        return AddQuotes(name);
    }

    // just a shorthand for getting names of variables
    public static string GetQuotedVariableName(this UniqueNamer namer, VCExprVar var)
    { 
      return namer.GetQuotedName(var, var.Name);
    }

    public static string GetTypeName(this ScopedNamer namer, TypeVariable typeVariable)
    {
        var existing = namer[typeVariable];
        if (existing != null)
        {
            return existing;
        }
        else
        {
            return GetLocalName(namer, typeVariable, typeVariable.Name);
        }
    }

}