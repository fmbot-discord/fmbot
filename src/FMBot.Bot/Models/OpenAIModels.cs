using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FMBot.Bot.Models;

public class OpenAIModels
{

    public class InputContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class InputMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public List<InputContent> Content { get; set; }
    }

    public class TextFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    public class TextConfig
    {
        [JsonPropertyName("format")]
        public TextFormat Format { get; set; }

        [JsonPropertyName("verbosity")]
        public string Verbosity { get; set; }
    }

    public class ReasoningConfig
    {
        [JsonPropertyName("effort")]
        public string Effort { get; set; }

        [JsonPropertyName("summary")]
        public string Summary { get; set; }
    }

    public class ResponsesRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("input")]
        public List<InputMessage> Input { get; set; }

        [JsonPropertyName("text")]
        public TextConfig Text { get; set; }

        [JsonPropertyName("reasoning")]
        public ReasoningConfig Reasoning { get; set; }

    }

    public class OutputContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class OutputItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("content")]
        public List<OutputContent> Content { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; }
    }

    public class ResponsesResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Object { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("usage")]
        public Usage Usage { get; set; }

        [JsonPropertyName("output")]
        public List<OutputItem> Output { get; set; }

        public string Prompt { get; set; }
    }

    public class OpenAiResponse
    {
        public string Model { get; set; }
        public Usage Usage { get; set; }
        public string Prompt { get; set; }
        public string Output { get; set; }
    }

    public class Usage
    {
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
