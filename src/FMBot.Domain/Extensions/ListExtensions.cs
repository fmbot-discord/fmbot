using System.Collections.Generic;
using System.Linq;

namespace FMBot.Domain.Extensions;

public static class ListExtensions
{
    public static void ReplaceOrAddToList(this List<string> currentList, IEnumerable<string> optionsToAdd)
    {
        foreach (var optionToAdd in optionsToAdd)
        {
            var existingOption = currentList.FirstOrDefault(f => f.ToLower() == optionToAdd.ToLower());

            if (existingOption != null && existingOption != optionToAdd)
            {
                var index = currentList.IndexOf(existingOption);
                if (index != -1)
                {
                    currentList[index] = optionToAdd;
                }
            }
            else if (existingOption == null)
            {
                currentList.Add(optionToAdd);
            }
        }
    }
}
