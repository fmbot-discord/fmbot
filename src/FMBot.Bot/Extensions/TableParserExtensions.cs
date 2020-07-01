using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace FMBot.Bot.Extensions
{
    public static class TableParserExtensions
    {
        public static string ToTasteTable<T>(this IEnumerable<T> values, string[] columnHeaders,
            params Func<T, object>[] valueSelectors)
        {
            return ToTasteTable(values.ToArray(), columnHeaders, valueSelectors);
        }

        public static string ToTasteTable<T>(this T[] values, string[] columnHeaders,
            params Func<T, object>[] valueSelectors)
        {
            Debug.Assert(columnHeaders.Length == valueSelectors.Length);

            var arrValues = new string[values.Length + 1, valueSelectors.Length];

            // Fill headers
            for (var colIndex = 0; colIndex < arrValues.GetLength(1); colIndex++)
                arrValues[0, colIndex] = columnHeaders[colIndex];

            // Fill table rows
            for (var rowIndex = 1; rowIndex < arrValues.GetLength(0); rowIndex++)
                for (var colIndex = 0; colIndex < arrValues.GetLength(1); colIndex++)
                {
                    var value = valueSelectors[colIndex].Invoke(values[rowIndex - 1]);

                    arrValues[rowIndex, colIndex] = value != null ? value.ToString() : "null";
                }

            return ToTasteTable(arrValues);
        }

        public static string ToTasteTable(this string[,] arrValues)
        {
            var maxColumnsWidth = GetMaxColumnsWidth(arrValues);
            var headerSplitter = new string('-', maxColumnsWidth.Sum(i => i));

            var sb = new StringBuilder();
            for (var rowIndex = 0; rowIndex < arrValues.GetLength(0); rowIndex++)
            {
                for (var colIndex = 0; colIndex < arrValues.GetLength(1); colIndex++)
                {
                    // Print cell
                    var cell = arrValues[rowIndex, colIndex];

                    if (colIndex == 0)
                    {
                        var amountToPad = maxColumnsWidth[colIndex];

                        if (rowIndex == 0)
                        {
                            amountToPad -= 1;
                        }

                        cell = cell.PadRight(amountToPad);
                    }
                    if (colIndex == 1)
                    {
                        var amountToPad = maxColumnsWidth[colIndex] - 1;

                        cell = cell.PadLeft(amountToPad);
                    }
                    if (colIndex == 2)
                    {
                        //sb.Append(" • ");
                    }

                    sb.Append(cell);
                }

                // Print end of line
                sb.AppendLine();

                // Print splitter
                if (rowIndex == 0)
                {
                    sb.AppendFormat("{0}", headerSplitter);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static int[] GetMaxColumnsWidth(string[,] arrValues)
        {
            var maxColumnsWidth = new int[arrValues.GetLength(1)];
            for (var colIndex = 0; colIndex < arrValues.GetLength(1); colIndex++)
                for (var rowIndex = 0; rowIndex < arrValues.GetLength(0); rowIndex++)
                {
                    var newLength = arrValues[rowIndex, colIndex].Length;
                    var oldLength = maxColumnsWidth[colIndex];

                    if (newLength > oldLength) maxColumnsWidth[colIndex] = newLength;
                }

            return maxColumnsWidth;
        }

        public static string ToTasteTable<T>(this IEnumerable<T> values,
            params Expression<Func<T, object>>[] valueSelectors)
        {
            var headers = valueSelectors.Select(func => GetProperty(func).Name).ToArray();
            var selectors = valueSelectors.Select(exp => exp.Compile()).ToArray();
            return ToTasteTable(values, headers, selectors);
        }

        private static PropertyInfo GetProperty<T>(Expression<Func<T, object>> expresstion)
        {
            if (expresstion.Body is UnaryExpression)
                if ((expresstion.Body as UnaryExpression).Operand is MemberExpression)
                    return ((expresstion.Body as UnaryExpression).Operand as MemberExpression).Member as PropertyInfo;

            if (expresstion.Body is MemberExpression)
                return (expresstion.Body as MemberExpression).Member as PropertyInfo;
            return null;
        }
    }
}
