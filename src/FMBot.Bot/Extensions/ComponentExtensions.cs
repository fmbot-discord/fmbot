using System;
using System.Collections.Generic;
using System.Linq;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Extensions;

public static class ComponentExtensions
{
    public static List<IMessageComponentProperties> WithDisabled(
        this IReadOnlyList<IMessageComponent> components,
        string specificButtonOnly = null)
    {
        return components.Select(c => ToProperties(c, specificButtonOnly)).ToList();
    }

    private static IMessageComponentProperties ToProperties(IMessageComponent component, string specificButtonOnly)
    {
        return component switch
        {
            ComponentContainer container => new ComponentContainerProperties(
                container.Components.Select(c => ToContainerComponentProperties(c, specificButtonOnly)))
            {
                AccentColor = container.AccentColor
            },

            ActionRow row => new ActionRowProperties(
                row.Components.Select(c => ToRowComponentProperties(c, specificButtonOnly))),

            _ => throw new NotSupportedException($"Unknown component: {component.GetType().Name}")
        };
    }

    private static IComponentContainerComponentProperties ToContainerComponentProperties(
        IComponentContainerComponent component,
        string specificButtonOnly)
    {
        return component switch
        {
            ActionRow row => new ActionRowProperties(
                row.Components.Select(c => ToRowComponentProperties(c, specificButtonOnly))),

            StringMenu menu => new StringMenuProperties(menu.CustomId,
                menu.Options.Select(o => new StringMenuSelectOptionProperties(o.Label, o.Value)
                {
                    Description = o.Description,
                    Emoji = ToEmojiProperties(o.Emoji),
                    Default = o.Default
                }))
            {
                Placeholder = menu.Placeholder,
                MinValues = menu.MinValues,
                MaxValues = menu.MaxValues,
                Disabled = specificButtonOnly == null || menu.Disabled
            },

            TextDisplay text => new TextDisplayProperties(text.Content),

            ComponentSeparator sep => new ComponentSeparatorProperties
            {
                Divider = sep.Divider,
                Spacing = sep.Spacing
            },

            ComponentSection section => ToSectionProperties(section, specificButtonOnly),

            _ => throw new NotSupportedException($"Unknown container component: {component.GetType().Name}")
        };
    }

    private static ComponentSectionProperties ToSectionProperties(
        ComponentSection section,
        string specificButtonOnly)
    {
        var accessory = ToAccessoryProperties(section.Accessory, specificButtonOnly);
        var components = section.Components.Select(ToSectionComponentProperties);

        return new ComponentSectionProperties(accessory, components);
    }

    private static IComponentSectionComponentProperties ToSectionComponentProperties(
        IComponentSectionComponent component)
    {
        return component switch
        {
            TextDisplay text => new TextDisplayProperties(text.Content),
            _ => throw new NotSupportedException($"Unknown section component: {component.GetType().Name}")
        };
    }

    private static IComponentSectionAccessoryComponentProperties ToAccessoryProperties(
        IComponentSectionAccessoryComponent accessory,
        string specificButtonOnly)
    {
        return accessory switch
        {
            null => null,
            Button btn => new ButtonProperties(btn.CustomId, btn.Label, btn.Style)
            {
                Emoji = ToEmojiProperties(btn.Emoji),
                Disabled = specificButtonOnly == null || btn.CustomId == specificButtonOnly  || btn.Disabled
            },
            LinkButton btn => new LinkButtonProperties(btn.Url, btn.Label)
            {
                Emoji = ToEmojiProperties(btn.Emoji),
                Disabled = false
            },
            ComponentSectionThumbnail thumb => new ComponentSectionThumbnailProperties(
                new ComponentMediaProperties(thumb.Media.Url)),
            _ => throw new NotSupportedException($"Unknown accessory: {accessory.GetType().Name}")
        };
    }

    private static IActionRowComponentProperties ToRowComponentProperties(
        IActionRowComponent component,
        string specificButtonOnly)
    {
        return component switch
        {
            Button btn => new ButtonProperties(btn.CustomId, btn.Label, btn.Style)
            {
                Emoji = ToEmojiProperties(btn.Emoji),
                Disabled = specificButtonOnly == null || btn.CustomId == specificButtonOnly  || btn.Disabled
            },

            LinkButton btn => new LinkButtonProperties(btn.Url, btn.Label)
            {
                Emoji = ToEmojiProperties(btn.Emoji),
                Disabled = false
            },

            _ => throw new NotSupportedException($"Unknown row component: {component.GetType().Name}")
        };
    }

    private static EmojiProperties ToEmojiProperties(EmojiReference emoji)
    {
        if (emoji == null) return null;
        return emoji.Id.HasValue ? EmojiProperties.Custom(emoji.Id.Value) : EmojiProperties.Standard(emoji.Name!);
    }
}
