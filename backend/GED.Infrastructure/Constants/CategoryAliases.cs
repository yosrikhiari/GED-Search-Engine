namespace GED.Infrastructure.Constants;

public static class CategoryAliases
{
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "contrat",      "Contract"     }, { "contrats",     "Contract"     },
        { "facture",      "Invoice"      }, { "factures",     "Invoice"      },
        { "rapport",      "Report"       }, { "rapports",     "Report"       },
        { "lettre",       "Letter"       }, { "lettres",      "Letter"       },
        { "courrier",     "Letter"       },
        { "devis",        "Invoice"      },
        { "note",         "Memo"         },
        { "présentation", "Presentation" }, { "presentation", "Presentation" },
        { "عقد",          "Contract"     }, { "عقود",         "Contract"     },
        { "فاتورة",       "Invoice"      }, { "فواتير",       "Invoice"      },
        { "تقرير",        "Report"       }, { "تقارير",       "Report"       },
        { "رسالة",        "Letter"       }, { "رسائل",        "Letter"       },
        { "مذكرة",        "Memo"         },
        { "عرض",          "Presentation" },
    };

    public static IReadOnlyDictionary<string, string> All => _aliases;

    public static bool TryGetEnglish(string alias, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? english)
        => _aliases.TryGetValue(alias, out english);

    public static IEnumerable<string> ExpandFromQuery(string query)
    {
        return _aliases
            .Where(kv => query.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .Distinct();
    }
}
