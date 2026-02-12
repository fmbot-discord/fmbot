namespace FMBot.Bot.Services;

public class LocaleAccessor
{
    private readonly LocalizationService _service;
    private readonly string _locale;

    public LocaleAccessor(LocalizationService service, string locale)
    {
        this._service = service;
        this._locale = locale;
    }

    public string this[string key] => this._service.Get(this._locale, key);

    public string Get(string key, params (string name, string value)[] replacements)
    {
        return this._service.Get(this._locale, key, replacements);
    }
}
