﻿using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Entities;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MediaBrowser.Server.Implementations.FileOrganization
{
    public static class NameUtils
    {
        private static readonly CultureInfo UsCulture = new CultureInfo("en-US");

        internal static Tuple<T, int> GetMatchScore<T>(string sortedName, int? year, T series)
            where T : BaseItem
        {
            var score = 0;

            var seriesNameWithoutYear = series.Name;
            if (series.ProductionYear.HasValue)
            {
                seriesNameWithoutYear = seriesNameWithoutYear.Replace(series.ProductionYear.Value.ToString(UsCulture), String.Empty);
            }

            if (IsNameMatch(sortedName, seriesNameWithoutYear))
            {
                score++;

                if (year.HasValue && series.ProductionYear.HasValue)
                {
                    if (year.Value == series.ProductionYear.Value)
                    {
                        score++;
                    }
                    else
                    {
                        // Regardless of name, return a 0 score if the years don't match
                        return new Tuple<T, int>(series, 0);
                    }
                }
            }

            return new Tuple<T, int>(series, score);
        }


        private static bool IsNameMatch(string name1, string name2)
        {
            name1 = GetComparableName(name1);
            name2 = GetComparableName(name2);

            return String.Equals(name1, name2, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetComparableName(string name)
        {
            // TODO: Improve this - should ignore spaces, periods, underscores, most likely all symbols and 
            // possibly remove sorting words like "the", "and", etc.

            name = RemoveDiacritics(name);

            name = " " + name + " ";

            name = name.Replace(".", " ")
            .Replace("_", " ")
            .Replace(" and ", " ")
            .Replace(".and.", " ")
            .Replace("&", " ")
            .Replace("!", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace(":", " ")
            .Replace(",", " ")
            .Replace("-", " ")
            .Replace("'", " ")
            .Replace("[", " ")
            .Replace("]", " ")
            .Replace(" a ", String.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" the ", String.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", String.Empty);

            return name.Trim();
        }

        /// <summary>
        /// Removes the diacritics.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>System.String.</returns>
        private static string RemoveDiacritics(string text)
        {
            return String.Concat(
                text.Normalize(NormalizationForm.FormD)
                .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) !=
                                              UnicodeCategory.NonSpacingMark)
              ).Normalize(NormalizationForm.FormC);
        }
    }
}
