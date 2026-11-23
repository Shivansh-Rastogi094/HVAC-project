using System.Text.RegularExpressions;
using HvacConnect.Core.Domain.Enums;

namespace HvacConnect.Core.Services;

public class DuplicateMatchingEngine
{
    private static readonly HashSet<string> LegalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "llc", "inc", "corp", "corporation", "co", "ltd", "group", "services"
    };

    private static readonly HashSet<string> FreeMailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "yahoo.com", "outlook.com", "hotmail.com", "icloud.com", "aol.com", "proton.me"
    };

    // Normalisation: lower-case, remove punctuation, collapse spaces, remove trailing legal words
    public static string NormalizeCustomerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // lower-case and remove punctuation except space
        string cleaned = Regex.Replace(name.ToLowerInvariant(), @"[^\w\s]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        string[] tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(tokens);

        // Remove trailing legal words repeatedly unless result would be < 3 chars
        while (list.Count > 0 && LegalWords.Contains(list[^1]))
        {
            string candidate = string.Join(" ", list.Take(list.Count - 1));
            if (candidate.Length < 3) break;
            list.RemoveAt(list.Count - 1);
        }

        return string.Join(" ", list);
    }

    public static (short score, DuplicateConfidenceLevel level, string reason)? EvaluateMatch(
        string importedName,
        string? importedEmail,
        string? importedPhone,
        string existingName,
        string? existingEmail,
        string? existingPhone)
    {
        string normImported = NormalizeCustomerName(importedName);
        string normExisting = NormalizeCustomerName(existingName);

        bool nameMatch = CheckNameMatch(normImported, normExisting);
        if (!nameMatch) return null; // None -> No candidate

        bool contactMatch = CheckContactMatch(importedEmail, importedPhone, existingEmail, existingPhone, out string contactDetails);

        if (contactMatch)
        {
            // High (90)
            return (90, DuplicateConfidenceLevel.High, $"Direct customer name match + matching corporate domain or phone ({contactDetails})");
        }

        // Medium (70)
        return (70, DuplicateConfidenceLevel.Medium, "Customer name match only");
    }

    private static bool CheckNameMatch(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a == b) return true;

        // Word-prefix match on word boundaries where shorter has at least 2 words or >= 6 chars
        string shorter = a.Length <= b.Length ? a : b;
        string longer = a.Length > b.Length ? a : b;

        if ((shorter.Split(' ').Length >= 2 || shorter.Length >= 6) && longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Trigram similarity >= 0.75
        double similarity = ComputeTrigramSimilarity(a, b);
        return similarity >= 0.75;
    }

    private static bool CheckContactMatch(string? email1, string? phone1, string? email2, string? phone2, out string matched)
    {
        matched = string.Empty;

        // Email domain check (ignoring freemail)
        string? domain1 = ExtractDomain(email1);
        string? domain2 = ExtractDomain(email2);
        if (!string.IsNullOrEmpty(domain1) && !string.IsNullOrEmpty(domain2) &&
            domain1.Equals(domain2, StringComparison.OrdinalIgnoreCase) &&
            !FreeMailDomains.Contains(domain1))
        {
            matched = $"@{domain1}";
            return true;
        }

        // Last 10 phone digits check
        string digits1 = ExtractLast10Digits(phone1);
        string digits2 = ExtractLast10Digits(phone2);
        if (!string.IsNullOrEmpty(digits1) && digits1.Length == 10 && digits1 == digits2)
        {
            matched = $"Phone: {digits1}";
            return true;
        }

        return false;
    }

    private static string? ExtractDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;
        return email.Split('@')[^1].Trim().ToLowerInvariant();
    }

    private static string ExtractLast10Digits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        string digits = Regex.Replace(phone, @"\D", "");
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    private static double ComputeTrigramSimilarity(string str1, string str2)
    {
        var tri1 = GetTrigrams("  " + str1 + " ");
        var tri2 = GetTrigrams("  " + str2 + " ");

        int intersection = tri1.Intersect(tri2).Count();
        int union = tri1.Union(tri2).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static List<string> GetTrigrams(string s)
    {
        var list = new List<string>();
        for (int i = 0; i <= s.Length - 3; i++)
        {
            list.Add(s.Substring(i, 3));
        }
        return list;
    }
}

