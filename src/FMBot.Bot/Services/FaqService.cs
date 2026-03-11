using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FMBot.Bot.Models;

namespace FMBot.Bot.Services;

public class FaqService
{
    private readonly FaqData _faqData;

    public FaqService()
    {
        var faqJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "faqs.json");
        var faqJson = File.ReadAllBytes(faqJsonPath);
        _faqData = JsonSerializer.Deserialize<FaqData>(faqJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        });
    }

    public List<FaqCategory> GetCategories()
    {
        return _faqData.Categories;
    }

    public FaqCategory GetCategory(string categoryId)
    {
        return _faqData.Categories.FirstOrDefault(c =>
            c.Id.Equals(categoryId, StringComparison.OrdinalIgnoreCase));
    }

    public FaqQuestion GetQuestion(string categoryId, string questionId)
    {
        var category = GetCategory(categoryId);
        return category?.Questions.FirstOrDefault(q =>
            q.Id.Equals(questionId, StringComparison.OrdinalIgnoreCase));
    }
}
